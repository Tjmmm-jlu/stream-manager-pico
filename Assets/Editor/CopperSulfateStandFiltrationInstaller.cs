using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfateStandFiltrationInstaller
{
    private const string RequestPath = "Temp/CopperSulfateStandFiltration.request";
    private static bool _busy;
    static CopperSulfateStandFiltrationInstaller() => EditorApplication.update += ProcessRequest;
    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim(); File.Delete(RequestPath);
            if (command == "audit") Audit();
            else if (command == "install") Install();
            else if (command == "verify")
            {
                PicoPinchToggleVerification.Run();
                CopperSulfateLiquidVerification.Run();
                File.WriteAllText("Temp/CopperSulfateStandFiltration.result.txt", "PASS: stand filtration, pinch toggle, and liquid regression.");
            }
            else if (command == "build")
            {
                const string apk = "Builds/CopperSulfate-StandFiltration.apk";
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes = new[] { CopperSulfateLiquidInstaller.ScenePath },
                    locationPathName = apk, target = BuildTarget.Android, options = BuildOptions.Development });
                File.WriteAllText("Temp/CopperSulfateStandFiltration.build.txt", report.summary.result + "\n" + apk + "\nErrors: " + report.summary.totalErrors);
            }
            else throw new InvalidOperationException("Unknown stand filtration command: " + command);
        }
        catch (Exception error)
        {
            File.WriteAllText("Temp/CopperSulfateStandFiltration.result.txt", error.ToString()); Debug.LogException(error);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Install Stand Filtration")]
    public static void Install()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != CopperSulfateLiquidInstaller.ScenePath || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Open the stationary scene outside Play Mode.");
        var roots = scene.GetRootGameObjects();
        var filter = roots.SelectMany(r => r.GetComponentsInChildren<CopperSulfateFiltrationDetector>(true)).Single();
        if (filter.StandConfigured) throw new InvalidOperationException("Stand filtration is already installed.");
        string backup = "MigrationBackups/BeforeStandFiltrationInstall_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup); File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty) EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true);
        var semantic = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Single(s => s.StableId == "tripod_stand");
        var stand = semantic.GetComponent<XRGrabInteractable>();
        var ring = stand.GetComponentsInChildren<MeshFilter>().Single(m => m.name.StartsWith("pTorus"));
        var baseMesh = stand.GetComponentsInChildren<MeshFilter>().Single(m => m.name == "pCube89");
        var receiver = filter.Receiver;
        var vesselBounds = receiver.GetComponent<MeshFilter>().sharedMesh.bounds;
        var funnelBounds = filter.Funnel.GetComponent<MeshFilter>().sharedMesh.bounds;
        Transform Point(Transform parent, string name, Vector3 worldPosition)
        {
            var point = new GameObject(name).transform; point.SetParent(parent, false);
            point.SetPositionAndRotation(worldPosition, stand.transform.rotation); return point;
        }
        // Match the ring to the cone, and put the outlet just inside the beaker near its inner wall.
        filter.Seat.localPosition = new Vector3(funnelBounds.center.x,
            Mathf.Lerp(funnelBounds.min.y + funnelBounds.size.y * 0.6f, funnelBounds.max.y, 0.65f), funnelBounds.center.z);
        var ringCenter = ring.GetComponent<Renderer>().bounds.center;
        Vector3 direction = stand.transform.right;
        Vector3 receiverBottom = ringCenter + direction * (receiver.WorldRadius * 0.72f);
        receiverBottom.y = baseMesh.GetComponent<Renderer>().bounds.max.y + 0.002f +
            receiver.transform.TransformVector(Vector3.up * (receiver.Bottom.y - vesselBounds.min.y)).magnitude;
        float mouthY = receiverBottom.y + receiver.transform.TransformVector(Vector3.up * receiver.Height).magnitude;
        float outletY = funnelBounds.min.y + 0.003f;
        float holderY = mouthY - 0.012f + filter.Funnel.transform.TransformVector(Vector3.up * (filter.Seat.localPosition.y - outletY)).magnitude;
        ring.transform.parent.position += Vector3.up * (holderY - ringCenter.y);
        PrefabUtility.RecordPrefabInstancePropertyModifications(ring.transform.parent);
        // The old solid bounding box filled the ring opening and blocked the installed apparatus.
        foreach (var collider in stand.GetComponentsInChildren<Collider>())
        { collider.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(collider); }
        stand.colliders.Clear();
        foreach (var mesh in stand.GetComponentsInChildren<MeshFilter>())
        {
            if (mesh == ring)
            {
                var collider = mesh.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = mesh.sharedMesh;
                continue;
            }
            var box = mesh.gameObject.AddComponent<BoxCollider>();
            box.center = mesh.sharedMesh.bounds.center; box.size = mesh.sharedMesh.bounds.size;
            stand.colliders.Add(box);
        }
        PrefabUtility.RecordPrefabInstancePropertyModifications(stand);
        var holder = Point(stand.transform, "Filter Funnel Mount", ring.GetComponent<Renderer>().bounds.center);
        var dock = Point(stand.transform, "Filter Receiver Mount", receiverBottom);
        var outlet = new GameObject("Filter Outlet").transform; outlet.SetParent(filter.Funnel.transform, false);
        outlet.localPosition = new Vector3(funnelBounds.center.x, outletY, funnelBounds.center.z);
        var rodTarget = new GameObject("Filter Rod Target").transform; rodTarget.SetParent(filter.Funnel.transform, false);
        rodTarget.localPosition = filter.Inlet.localPosition + new Vector3(-funnelBounds.extents.x * 0.45f, -0.022f, 0f);
        var guide = new GameObject("Receiver Placement Ring"); guide.transform.SetParent(stand.transform, false);
        var line = guide.AddComponent<LineRenderer>();
        line.sharedMaterial = CopperSulfateLiquidInstaller.MaterialAsset("StandPlacementGuide", new Color(0.05f, 0.65f, 0.5f), false);
        line.useWorldSpace = false; line.loop = true; line.positionCount = 48; line.startWidth = line.endWidth = 0.0025f;
        guide.transform.position = new Vector3(receiverBottom.x, baseMesh.GetComponent<Renderer>().bounds.max.y + 0.003f, receiverBottom.z);
        for (int i = 0; i < line.positionCount; i++)
        {
            float angle = i * 2f * Mathf.PI / line.positionCount;
            line.SetPosition(i, new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * receiver.WorldRadius * 1.15f);
        }
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        filter.ConfigureStand(stand, holder, dock, outlet, rodTarget, guide);
        semantic.ConfigureDetailed(semantic.StableId, "\u94c1\u67b6\u53f0", "support_stand", semantic.Category,
            "supporting_filtration_and_heating", semantic.Color, semantic.Transparency, "ring_stand", semantic.Materials,
            semantic.Contents, semantic.ContentColor, semantic.StateTags,
            semantic.Aliases.Concat(new[] { "\u94c1\u67b6\u53f0", "\u8fc7\u6ee4\u652f\u67b6", "ring stand", "filter stand" }).ToArray(), true, true);
        var sequence = filter.GetComponent<PreparationSequenceController>();
        sequence.Steps.Single(s => s.id == "glass_funnel").instruction =
            "\u5c06\u6f0f\u6597\u5b89\u88c5\u5728\u94c1\u67b6\u53f0\u652f\u6491\u73af\u4e0a\uff0c\u63a5\u6536\u70e7\u676f\u653e\u5165\u5e95\u5ea7\u5b9a\u4f4d\u533a\uff1b\u53cc\u624b\u6301\u539f\u70e7\u676f\u548c\u73bb\u7483\u68d2\u5f15\u6d41\u8fc7\u6ee4\uff0c\u5b8c\u6210\u540e\u53d6\u51fa\u63a5\u6536\u70e7\u676f\u3002";
        EditorUtility.SetDirty(filter); EditorUtility.SetDirty(sequence); EditorUtility.SetDirty(semantic); EditorUtility.SetDirty(stand);
        EditorSceneManager.MarkSceneDirty(scene); AssetDatabase.SaveAssets();
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save stand filtration.");
        Audit();
        File.WriteAllText("Temp/CopperSulfateStandFiltration.result.txt", "Installed stand filtration. Backup: " + backup);
    }

    private static void Audit()
    {
        var scene = SceneManager.GetActiveScene();
        var semantics = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true)).ToArray();
        var stand = semantics.Single(s => s.StableId == "tripod_stand");
        var filter = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<CopperSulfateFiltrationDetector>(true)).Single();
        var lines = new System.Collections.Generic.List<string> { "Scene: " + scene.path + " dirty=" + scene.isDirty };
        foreach (var item in new[] { stand.gameObject, filter.Funnel.gameObject, filter.Receiver.gameObject })
        {
            lines.Add(item.name + " position=" + item.transform.position.ToString("F5") + " rotation=" + item.transform.eulerAngles + " scale=" + item.transform.lossyScale);
            foreach (var mesh in item.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.GetComponent<Renderer>() != null))
                lines.Add(AnimationUtility.CalculateTransformPath(mesh.transform, item.transform) + " active=" + mesh.gameObject.activeInHierarchy +
                    " local=" + mesh.transform.localPosition.ToString("F5") + " mesh=" + mesh.sharedMesh.bounds + " world=" + mesh.GetComponent<Renderer>().bounds);
        }
        lines.Add("Funnel seat=" + filter.Seat.localPosition.ToString("F5") + " inlet=" + filter.Inlet.localPosition.ToString("F5"));
        lines.Add("Receiver bottom=" + filter.Receiver.Bottom.ToString("F5") + " height=" + filter.Receiver.Height + " radius=" + filter.Receiver.WorldRadius);
        File.WriteAllLines("Temp/CopperSulfateStandFiltration.audit.txt", lines);
        var focus = stand.GetComponentsInChildren<Renderer>().Aggregate(new Bounds(stand.transform.position, Vector3.zero), (a, r) => { a.Encapsulate(r.bounds); return a; }).center;
        var eye = focus + new Vector3(-0.25f, 0.16f, -0.6f);
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateStandFiltration.before.png", eye, Quaternion.LookRotation(focus - eye));
    }
}
