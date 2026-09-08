using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CopperSulfateStationarySceneBuilder
{
    public const string ScenePath = "Assets/Scenes/ContinuousExperiment_CopperSulfate_Stationary.unity";
    private const string RequestPath = "Temp/CopperSulfateStationary.request";
    private static bool _busy;
    public static readonly Vector2 StandingXZ = new Vector2(4.25f, 6.8f);
    public static readonly string[] RequiredIds = { "forceps", "copper_sulfate_crude", "graduated_cylinder",
        "water_source", "beaker", "spirit_lamp", "glass_funnel", "china_dish", "tripod_stand" };

    private sealed class TablePlan
    {
        public string name;
        public Vector2 center;
        public Vector2 size;
        public float yaw;
        public (string id, Vector2 position)[] objects;
    }

    private static readonly TablePlan[] Tables =
    {
        new TablePlan { name = "PreparationStation", center = new Vector2(-0.44f, 0.27f),
            size = new Vector2(0.50f, 0.35f), yaw = -50f,
            objects = new[] { ("forceps", new Vector2(0f, -0.12f)),
                ("copper_sulfate_crude", new Vector2(0.17f, 0.055f)),
                ("graduated_cylinder", new Vector2(-0.15f, 0.045f)),
                ("water_source", new Vector2(0f, 0.08f)) } },
        new TablePlan { name = "MixingStation", center = new Vector2(0f, 0.655f),
            size = new Vector2(0.50f, 0.35f), yaw = 0f,
            objects = new[] { ("beaker", new Vector2(-0.12f, -0.085f)),
                ("spirit_lamp", new Vector2(0.12f, -0.085f)) } },
        new TablePlan { name = "FilteringStation", center = new Vector2(0.457f, 0.205f),
            size = new Vector2(0.65f, 0.50f), yaw = 65f,
            objects = new[] { ("tripod_stand", new Vector2(0.09f, 0f)),
                ("glass_funnel", new Vector2(-0.207f, -0.135f)),
                ("china_dish", new Vector2(-0.207f, 0.06f)) } }
    };

    static CopperSulfateStationarySceneBuilder() => EditorApplication.update += ProcessRequest;

    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (command == "audit") Audit(SceneManager.GetActiveScene());
            else if (command == "create") Create();
            else if (command == "verify") CopperSulfateStationaryVerification.Run();
            else throw new InvalidOperationException("Unknown stationary scene command: " + command);
        }
        catch (Exception exception)
        {
            File.WriteAllText("Temp/CopperSulfateStationary.result.txt", exception.ToString());
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Create Stationary Variant")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode first.");
        string sourcePath = CopperSulfateTransferInstaller.ScenePath;
        Scene active = SceneManager.GetActiveScene();
        if (active.path != sourcePath && active.path != ScenePath)
            throw new InvalidOperationException("Open a copper sulfate scene first.");
        string backup = "MigrationBackups/BeforeCopperStationary_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        Directory.CreateDirectory(backup);
        File.Copy(sourcePath, Path.Combine(backup, Path.GetFileName(sourcePath)));
        File.Copy("ProjectSettings/EditorBuildSettings.asset", Path.Combine(backup, "EditorBuildSettings.asset"));
        if (File.Exists(ScenePath)) File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
        if (active.isDirty)
        {
            EditorSceneManager.SaveScene(active, Path.Combine(backup, "UnsavedScene.unity"), true);
            if (!EditorSceneManager.SaveScene(active)) throw new IOException("Could not preserve the current scene.");
        }
        byte[] sourceBefore = File.ReadAllBytes(sourcePath);
        Scene source = active.path == sourcePath ? active : EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
        if (!EditorSceneManager.SaveScene(source, ScenePath, true))
            throw new IOException("Could not create the stationary scene copy.");
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Configure(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the stationary scene.");
        var buildScenes = EditorBuildSettings.scenes.Select(s => new EditorBuildSettingsScene(s.path, false)).ToList();
        buildScenes.RemoveAll(s => s.path == ScenePath);
        buildScenes.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = buildScenes.ToArray();
        if (!sourceBefore.SequenceEqual(File.ReadAllBytes(sourcePath)))
            throw new InvalidOperationException("The source scene changed during variant creation.");
        Audit(scene);
        File.WriteAllText("Temp/CopperSulfateStationary.result.txt", "Created: " + ScenePath +
            "\nPreserved source scene byte-for-byte. Backup: " + backup);
        Debug.Log("[CopperStationary] Created separate stationary scene; original preserved.");
    }

    private static void Configure(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var semantics = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true))
            .Where(s => RequiredIds.Contains(s.StableId)).ToDictionary(s => s.StableId);
        if (semantics.Count != RequiredIds.Length) throw new InvalidOperationException("Experiment apparatus is missing.");
        foreach (var item in semantics.Values)
            if (Tables.Any(plan => item.transform.IsChildOf(roots.Single(r => r.name == plan.name).transform)))
                throw new InvalidOperationException("Detach apparatus from furniture before resizing.");
        float floorY = roots.Single(r => r.name == "floor (1)").GetComponent<Renderer>().bounds.max.y;
        foreach (var plan in Tables)
        {
            Transform station = roots.Single(r => r.name == plan.name).transform;
            Renderer top = FindTabletop(station);
            float height = top.bounds.max.y;
            station.rotation = Quaternion.identity;
            station.localScale = Vector3.one;
            Bounds flatBounds = ProjectedBounds(top.gameObject, Vector3.zero, Quaternion.identity);
            station.localScale = new Vector3(plan.size.x / flatBounds.size.x, 1f, plan.size.y / flatBounds.size.z);
            station.rotation = Quaternion.Euler(0f, plan.yaw, 0f);
            Vector3 center = new Vector3(StandingXZ.x + plan.center.x, height, StandingXZ.y + plan.center.y);
            station.position += center - new Vector3(top.bounds.center.x, top.bounds.max.y, top.bounds.center.z);
            foreach (Transform cabinet in station.Cast<Transform>().Where(t => t.name.StartsWith("shelf showcase", StringComparison.Ordinal)))
            {
                Bounds cabinetBounds = ProjectedBounds(cabinet.gameObject, center, station.rotation);
                float x = Mathf.Clamp(cabinetBounds.center.x,
                    -plan.size.x * 0.5f + cabinetBounds.extents.x + 0.005f,
                    plan.size.x * 0.5f - cabinetBounds.extents.x - 0.005f);
                cabinet.position += station.rotation * new Vector3(x - cabinetBounds.center.x, 0f, 0f);
                PrefabUtility.RecordPrefabInstancePropertyModifications(cabinet);
                EditorUtility.SetDirty(cabinet);
            }
            EditorUtility.SetDirty(station);
            foreach (var placement in plan.objects)
            {
                var item = semantics[placement.id];
                item.transform.rotation = station.rotation * item.transform.rotation;
                Bounds bounds = ProjectedBounds(item.gameObject, Vector3.zero, station.rotation);
                Vector3 desired = center + station.rotation * new Vector3(placement.position.x, 0f, placement.position.y);
                Vector3 actual = station.rotation * new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                item.transform.position += desired + Vector3.up * 0.002f - actual;
                if (item.StableId == "glass_funnel")
                {
                    // The old box used world-aligned dimensions in local space, inflating the tilted funnel.
                    var box = item.GetComponent<BoxCollider>();
                    Bounds meshBounds = item.GetComponent<MeshFilter>().sharedMesh.bounds;
                    box.center = meshBounds.center;
                    box.size = meshBounds.size;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(box);
                    EditorUtility.SetDirty(box);
                }
                PrefabUtility.RecordPrefabInstancePropertyModifications(item.transform);
                EditorUtility.SetDirty(item.transform);
            }
        }
        // This unregistered tube belonged to the old counter and has no task interaction.
        var looseTube = roots.FirstOrDefault(r => r.name == "Glass_Lab_test_tube");
        if (looseTube != null && looseTube.GetComponent<SemanticObject>() == null) looseTube.SetActive(false);
        var origin = roots.SelectMany(r => r.GetComponentsInChildren<XROrigin>(true)).Single();
        var calibration = origin.GetComponent<PicoExperimentStartPose>();
        if (calibration == null) throw new InvalidOperationException("Initial pose calibration is missing.");
        Transform start = calibration.PreparationStart;
        start.name = "Stationary Start Pose";
        start.SetPositionAndRotation(new Vector3(StandingXZ.x, floorY, StandingXZ.y), Quaternion.identity);
        origin.Origin.transform.SetPositionAndRotation(start.position, start.rotation);
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
        EditorUtility.SetDirty(start);
        EditorUtility.SetDirty(origin.transform);
        EditorUtility.SetDirty(origin);
        roots.SelectMany(r => r.GetComponentsInChildren<SceneObjectRegistry>(true)).Single().Refresh();
    }

    public static Renderer FindTabletop(Transform station) => station.GetComponentsInChildren<Renderer>()
        .Single(r => r.name == "shelf (1)");

    public static Bounds ProjectedBounds(GameObject item, Vector3 origin, Quaternion rotation)
    {
        Quaternion inverse = Quaternion.Inverse(rotation);
        Bounds result = default;
        bool initialized = false;
        foreach (var renderer in item.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r.gameObject.activeInHierarchy))
        {
            Bounds bounds = renderer.localBounds;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = bounds.center + Vector3.Scale(bounds.extents, new Vector3(
                    (corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                point = inverse * (renderer.transform.TransformPoint(point) - origin);
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
        }
        if (!initialized) throw new InvalidOperationException("Missing visual bounds for " + item.name);
        return result;
    }

    private static string Hierarchy(Transform item) => item.parent != null
        ? Hierarchy(item.parent) + "/" + item.name : item.name;

    private static void Audit(Scene scene)
    {
        var report = new StringBuilder();
        var roots = scene.GetRootGameObjects();
        report.AppendLine("SCENE " + scene.path);
        foreach (var root in roots.Where(r => r.activeInHierarchy))
            report.AppendLine($"ROOT {root.name}: pos={root.transform.position:F4} scale={root.transform.lossyScale:F4}");
        foreach (var semantic in roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Where(s => s.gameObject.activeInHierarchy))
        {
            var renderers = semantic.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) continue;
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            report.AppendLine($"OBJECT {semantic.StableId} path={Hierarchy(semantic.transform)} center={bounds.center:F4} size={bounds.size:F4} min={bounds.min:F4} rot={semantic.transform.eulerAngles:F2}");
            foreach (var collider in semantic.GetComponentsInChildren<Collider>().Where(c => c.enabled))
                report.AppendLine($"COLLIDER {semantic.StableId} {collider.GetType().Name} {Hierarchy(collider.transform)} center={collider.bounds.center:F4} size={collider.bounds.size:F4}" +
                    (collider is BoxCollider box ? $" localCenter={box.center:F4} localSize={box.size:F4}" : ""));
        }
        foreach (var renderer in roots.SelectMany(r => r.GetComponentsInChildren<Renderer>()).Where(r => r.gameObject.activeInHierarchy))
        {
            var p = renderer.bounds.center;
            if (p.x < 2f || p.x > 6.5f || p.z < 4.5f || p.z > 8.5f || p.y > 1.55f) continue;
            report.AppendLine($"MESH {Hierarchy(renderer.transform)} center={p:F4} size={renderer.bounds.size:F4} min={renderer.bounds.min:F4}");
        }
        File.WriteAllText("Temp/CopperSulfateStationary.audit.txt", report.ToString());
        bool stationary = scene.path == ScenePath;
        RenderInspection(scene, "Temp/CopperSulfateStationary.top.png", stationary ? new Vector3(4.25f, 3.8f, 7.15f) : new Vector3(4.25f, 3.8f, 6.6f),
            Vector3.down, Vector3.forward, stationary ? 0.95f : 2.15f);
        if (stationary)
        {
            RenderInspection(scene, "Temp/CopperSulfateStationary.front.png", new Vector3(4.25f, 1.71662f, 6.8f),
                Quaternion.Euler(55f, 0f, 0f) * Vector3.forward, Vector3.up, 0.82f);
            foreach (int yaw in new[] { -60, 0, 60 })
                RenderInspection(scene, "Temp/CopperSulfateStationary.head-" + yaw + ".png", new Vector3(4.25f, 1.71662f, 6.8f),
                    Quaternion.Euler(45f, yaw, 0f) * Vector3.forward, Vector3.up, 0.8f, true);
        }
        Debug.Log("[CopperStationary] Scene audit completed.");
    }

    private static void RenderInspection(Scene scene, string path, Vector3 position, Vector3 forward, Vector3 up, float size, bool perspective = false)
    {
        var cameraObject = new GameObject("Stationary Layout Inspection") { hideFlags = HideFlags.HideAndDontSave };
        SceneManager.MoveGameObjectToScene(cameraObject, scene);
        var camera = cameraObject.AddComponent<Camera>();
        var texture = new RenderTexture(1200, 1000, 24);
        var previous = RenderTexture.active;
        Texture2D image = null;
        try
        {
            camera.enabled = false;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(forward, up));
            camera.orthographic = !perspective;
            camera.fieldOfView = 75f;
            camera.orthographicSize = size;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.8f, 0.84f, 0.86f);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
        }
        finally
        {
            camera.targetTexture = null;
            RenderTexture.active = previous;
            if (image != null) UnityEngine.Object.DestroyImmediate(image);
            texture.Release();
            UnityEngine.Object.DestroyImmediate(texture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }
    }
}
