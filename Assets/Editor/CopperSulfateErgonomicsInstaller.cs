using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfateErgonomicsInstaller
{
    private const string RequestPath = "Temp/CopperSulfateErgonomics.request";
    private static bool _busy;

    static CopperSulfateErgonomicsInstaller()
    {
        EditorApplication.update += ProcessRequest;
    }

    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string request = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (request == "audit") Audit();
            else if (request == "install") Install();
            else if (request == "verify") CopperSulfateErgonomicsVerification.Run();
            else if (request == "refresh") AssetDatabase.Refresh();
            else throw new InvalidOperationException("Unknown ergonomics request: " + request);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/CopperSulfateErgonomics.result.txt", exception.ToString());
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Apply Ergonomics And Start Pose")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != CopperSulfateTransferInstaller.ScenePath)
            throw new InvalidOperationException("Open the copper sulfate scene outside Play Mode first.");
        string backup = "MigrationBackups/BeforeCopperErgonomics_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        ConfigureScene(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save ergonomics setup.");
        Audit();
        File.WriteAllText("Temp/CopperSulfateErgonomics.result.txt", "Installed ergonomics. Backup: " + backup);
        Debug.Log("[CopperErgonomics] Installed 17 cm forceps and preparation start pose.");
    }

    public static void ConfigureScene(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var tool = roots.SelectMany(root => root.GetComponentsInChildren<SemanticObject>(true))
            .Single(s => s.StableId == "forceps");
        var origin = roots.SelectMany(root => root.GetComponentsInChildren<XROrigin>(true)).Single();
        if ((origin.Origin.transform.lossyScale - Vector3.one).sqrMagnitude > 0.00001f)
            throw new InvalidOperationException("XR Origin must keep real-world scale.");

        Bounds bounds = tool.GetComponent<MeshFilter>().sharedMesh.bounds;
        Transform toolTransform = tool.transform;
        float bottomBefore = tool.GetComponent<Renderer>().bounds.min.y;
        float length = toolTransform.TransformVector(Vector3.right * bounds.size.x).magnitude;
        toolTransform.localScale *= 0.17f / length;
        toolTransform.position += Vector3.up * (bottomBefore - tool.GetComponent<Renderer>().bounds.min.y);
        Transform grip = toolTransform.Find("ForcepsGrip");
        if (grip == null)
        {
            grip = new GameObject("ForcepsGrip").transform;
            grip.SetParent(toolTransform, false);
        }
        grip.localPosition = new Vector3(bounds.min.x + bounds.size.x * 0.65f, bounds.center.y, bounds.center.z);
        grip.localRotation = Quaternion.identity;
        var grab = tool.GetComponent<XRGrabInteractable>();
        grab.attachTransform = grip;
        grab.useDynamicAttach = true;
        grab.matchAttachPosition = false;
        // Pinch rotation is the thumb-tip joint rotation: retain the picked-up orientation for either hand.
        grab.matchAttachRotation = true;
        grab.snapToColliderVolume = false;
        PrefabUtility.RecordPrefabInstancePropertyModifications(toolTransform);
        PrefabUtility.RecordPrefabInstancePropertyModifications(grab);
        EditorUtility.SetDirty(toolTransform);
        EditorUtility.SetDirty(grab);

        var sequence = roots.SelectMany(root => root.GetComponentsInChildren<PreparationSequenceController>(true)).Single();
        Transform start = roots.Select(root => root.transform).FirstOrDefault(t => t.name == "Preparation Start Pose");
        if (start == null)
        {
            start = new GameObject("Preparation Start Pose").transform;
            SceneManager.MoveGameObjectToScene(start.gameObject, scene);
        }
        start.position = FindStandingPosition(scene, toolTransform, bottomBefore, origin.Origin.transform.position);
        Vector3 forward = Vector3.ProjectOnPlane(toolTransform.position - start.position, Vector3.up);
        start.rotation = Quaternion.LookRotation(forward, Vector3.up);
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        origin.Origin.transform.SetPositionAndRotation(start.position, start.rotation);
        var calibration = origin.GetComponent<PicoExperimentStartPose>() ?? origin.gameObject.AddComponent<PicoExperimentStartPose>();
        calibration.Configure(origin, start, sequence.GetComponent<ExperimentLogger>());
        var receiver = sequence.GetComponent<GuidanceDataChannelReceiver>();
        var serialized = new SerializedObject(receiver);
        serialized.FindProperty("experimentStartPose").objectReferenceValue = calibration;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(origin);
        EditorUtility.SetDirty(origin.transform);
        EditorUtility.SetDirty(calibration);
        EditorUtility.SetDirty(receiver);
    }

    private static Vector3 FindStandingPosition(Scene scene, Transform tool, float toolBottom, Vector3 oldStart)
    {
        var support = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>())
            .Where(r => !r.transform.IsChildOf(tool) && r.bounds.size.x > 0.65f && r.bounds.size.z > 0.65f &&
                r.bounds.min.x <= tool.position.x && r.bounds.max.x >= tool.position.x &&
                r.bounds.min.z <= tool.position.z && r.bounds.max.z >= tool.position.z &&
                r.bounds.max.y <= toolBottom + 0.02f && r.bounds.max.y >= toolBottom - 0.15f)
            .OrderByDescending(r => r.bounds.max.y).FirstOrDefault();
        if (support == null) throw new InvalidOperationException("Could not locate the preparation table beneath the forceps.");
        Bounds table = support.bounds;
        const float clearance = 0.3f;
        float x = Mathf.Clamp(tool.position.x, table.min.x + clearance, table.max.x - clearance);
        float z = Mathf.Clamp(tool.position.z, table.min.z + clearance, table.max.z - clearance);
        var options = new[]
        {
            new Vector3(table.max.x + clearance, oldStart.y, z),
            new Vector3(table.min.x - clearance, oldStart.y, z),
            new Vector3(x, oldStart.y, table.max.z + clearance),
            new Vector3(x, oldStart.y, table.min.z - clearance)
        };
        // Start at the nearest table edge to the first tool, with 30 cm tabletop clearance.
        Vector3 standing = options.OrderBy(point => Vector3.ProjectOnPlane(point - tool.position, Vector3.up).sqrMagnitude)
            .ThenBy(point => (point - oldStart).sqrMagnitude).First();
        var floor = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Renderer>())
            .Where(r => r.name.StartsWith("floor", StringComparison.OrdinalIgnoreCase) &&
                r.bounds.min.x <= standing.x && r.bounds.max.x >= standing.x &&
                r.bounds.min.z <= standing.z && r.bounds.max.z >= standing.z &&
                r.bounds.max.y < toolBottom - 0.3f)
            .OrderByDescending(r => r.bounds.max.y).FirstOrDefault();
        if (floor == null) throw new InvalidOperationException("Could not find the laboratory floor at the start position.");
        standing.y = floor.bounds.max.y;
        return standing;
    }

    private static void Audit()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateTransferInstaller.ScenePath)
            throw new InvalidOperationException("Open the copper sulfate scene first.");
        var report = new StringBuilder();
        var transforms = scene.GetRootGameObjects().SelectMany(root =>
            root.GetComponentsInChildren<Transform>(true)).ToArray();
        foreach (var t in transforms.Where(t => t.name.Contains("Station") ||
            t.name == "PICO XR Origin" || t.name == "Main Camera" || t.name == "Camera Offset"))
            report.AppendLine($"POSE {t.name}: world={t.position:F5} rot={t.eulerAngles:F2} scale={t.lossyScale:F5}");
        foreach (var semantic in transforms.Select(t => t.GetComponent<SemanticObject>())
            .Where(s => s != null && new[] { "forceps", "copper_sulfate_crude", "beaker" }.Contains(s.StableId)))
        {
            report.AppendLine($"OBJECT {semantic.StableId}: pos={semantic.transform.position:F5} parent={semantic.transform.parent?.name}");
            foreach (var r in semantic.GetComponentsInChildren<Renderer>())
                report.AppendLine($"RENDER {r.name}: center={r.bounds.center:F5} size={r.bounds.size:F5} min={r.bounds.min:F5}");
            var grab = semantic.GetComponent<XRGrabInteractable>();
            if (grab != null) report.AppendLine($"GRAB {semantic.StableId}: attach={(grab.attachTransform != null ? grab.attachTransform.name : "none")} dynamic={grab.useDynamicAttach} matchPos={grab.matchAttachPosition} matchRot={grab.matchAttachRotation}");
        }
        foreach (var r in transforms.Select(t => t.GetComponent<Renderer>()).Where(r => r != null &&
            r.gameObject.activeInHierarchy && (r.name.IndexOf("floor", StringComparison.OrdinalIgnoreCase) >= 0 ||
            r.bounds.size.x > 0.65f && r.bounds.size.z > 0.65f && r.bounds.min.y < 1.2f && r.bounds.max.y > 0.7f)))
            report.AppendLine($"SURFACE {r.name}: center={r.bounds.center:F5} size={r.bounds.size:F5} min={r.bounds.min:F5} max={r.bounds.max:F5}");
        foreach (var origin in transforms.Select(t => t.GetComponent<XROrigin>()).Where(o => o != null))
            report.AppendLine($"XR: camera={origin.Camera?.name} floorOffset={origin.CameraFloorOffsetObject?.name} requested={origin.RequestedTrackingOriginMode} yOffset={origin.CameraYOffset}");
        File.WriteAllText("Temp/CopperSulfateErgonomics.audit.txt", report.ToString());
        Debug.Log("[CopperErgonomics] Audit completed.");
    }
}
