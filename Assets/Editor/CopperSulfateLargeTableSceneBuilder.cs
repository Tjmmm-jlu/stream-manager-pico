using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CopperSulfateLargeTableSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/ContinuousExperiment_CopperSulfate_LargeTables.unity";
    private const string RequestPath = "Temp/CopperSulfateLargeTables.request";
    private const string ResultPath = "Temp/CopperSulfateLargeTables.result.txt";
    private static readonly string[] TableNames = { "PreparationStation", "MixingStation", "FilteringStation" };
    private static bool _busy;

    static CopperSulfateLargeTableSceneBuilder() => EditorApplication.update += ProcessRequest;

    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            if (command == "audit") AuditSources();
            else if (command == "create") Create();
            else if (command == "verify") Verify();
            else throw new InvalidOperationException("Unknown large-table command: " + command);
        }
        catch (Exception exception)
        {
            File.WriteAllText(ResultPath, exception.ToString());
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    private static T[] Components<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();

    private static Transform Table(Scene scene, string name) =>
        scene.GetRootGameObjects().Single(root => root.name == name).transform;

    private static Renderer Top(Transform table) => CopperSulfateStationarySceneBuilder.FindTabletop(table);

    private static readonly (string id, string table, Vector2 offset, Vector3 angles)[] Placements =
    {
        ("water_source", "PreparationStation", new Vector2(-0.48f, 0.28f), new Vector3(0f, -20f, 0f)),
        ("forceps", "PreparationStation", new Vector2(-0.35f, 0.05f), new Vector3(0f, 90f, 0f)),
        ("graduated_cylinder", "PreparationStation", new Vector2(-0.40f, -0.10f), new Vector3(0f, 20f, 0f)),
        ("copper_sulfate_crude", "PreparationStation", new Vector2(-0.58f, 0.10f), new Vector3(0f, -15f, 0f)),
        ("long_lighter", "MixingStation", new Vector2(-0.39f, 0.443f), new Vector3(90f, 90f, 0f)),
        ("spirit_lamp", "MixingStation", new Vector2(-0.26f, 0.54f), new Vector3(0f, 15f, 0f)),
        ("glass_rod", "MixingStation", new Vector2(-0.025f, 0.442f), new Vector3(0f, 0f, 90f)),
        ("beaker", "MixingStation", new Vector2(0.08f, 0.585f), new Vector3(0f, -15f, 0f)),
        ("china_dish", "PreparationStation", new Vector2(-0.60f, -0.12f), new Vector3(0f, -25f, 0f)),
        ("glass_funnel", "MixingStation", new Vector2(0.35f, 0.50f), new Vector3(0f, 20f, 0f)),
        ("tripod_stand", "FilteringStation", new Vector2(0.49f, -0.015f), new Vector3(0f, 180f, 0f)),
        ("filtrate_beaker", "FilteringStation", new Vector2(0.43f, 0.31f), new Vector3(0f, -35f, 0f))
    };

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Create Large Table Variant")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode first.");
        string sourcePath = CopperSulfateStationarySceneBuilder.ScenePath;
        string backup = "MigrationBackups/BeforeLargeTables_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        var preserved = new[] { sourcePath, CopperSulfateTransferInstaller.ScenePath }
            .ToDictionary(path => path, File.ReadAllBytes);
        foreach (string path in preserved.Keys)
            File.Copy(path, Path.Combine(backup, Path.GetFileName(path)));
        File.Copy("ProjectSettings/EditorBuildSettings.asset", Path.Combine(backup, "EditorBuildSettings.asset"));
        if (File.Exists(ScenePath)) File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene loaded = SceneManager.GetSceneAt(i);
            if (loaded.isDirty && !EditorSceneManager.SaveScene(loaded,
                Path.Combine(backup, "Unsaved_" + i + "_" + loaded.name + ".unity"), true))
                throw new IOException("Could not preserve unsaved scene: " + loaded.name);
        }
        Scene source = SceneManager.GetSceneByPath(sourcePath);
        if (!source.IsValid() || !source.isLoaded) source = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Additive);
        Scene previousVariant = SceneManager.GetSceneByPath(ScenePath);
        if (previousVariant.IsValid() && previousVariant.isLoaded)
        {
            SceneManager.SetActiveScene(source);
            EditorSceneManager.CloseScene(previousVariant, true);
        }
        if (!EditorSceneManager.SaveScene(source, ScenePath, true)) throw new IOException("Could not clone the current workflow.");
        AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Configure(scene);
        CopperSulfateRoomInstaller.Configure(scene);
        var report = new List<string>();
        RenderLayout(scene);
        try { CheckLayout(scene, report); }
        finally { File.WriteAllLines("Temp/CopperSulfateLargeTables.layout.txt", report); }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save the large-table scene.");
        foreach (var entry in preserved)
            if (!entry.Value.SequenceEqual(File.ReadAllBytes(entry.Key)))
                throw new InvalidOperationException("Source scene changed: " + entry.Key);
        var builds = EditorBuildSettings.scenes.Where(s => s.path != ScenePath)
            .Select(s => new EditorBuildSettingsScene(s.path, false)).ToList();
        builds.Add(new EditorBuildSettingsScene(ScenePath, true));
        EditorBuildSettings.scenes = builds.ToArray();
        File.WriteAllText(ResultPath, "Created: " + ScenePath + "\nBackup: " + backup +
            "\nBoth source scenes preserved byte-for-byte. Large-table scene enabled for builds.");
        Debug.Log("[CopperLargeTables] Created and checked: " + ScenePath);
    }

    private static void Record(Transform transform)
    {
        PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
        EditorUtility.SetDirty(transform);
    }

    private static void Configure(Scene scene)
    {
        var start = Components<PicoExperimentStartPose>(scene).Single().PreparationStart;
        Quaternion frame = Quaternion.Euler(0f, start.eulerAngles.y, 0f);
        var items = Components<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy).ToDictionary(s => s.StableId);
        if (items.Count != Placements.Length + 1 || !items.ContainsKey("lamp_cap"))
            throw new InvalidOperationException("Unexpected apparatus set; review the layout before regenerating.");
        foreach (var item in items.Values)
            if (TableNames.Any(name => item.transform.IsChildOf(Table(scene, name))))
                throw new InvalidOperationException("Apparatus must not inherit furniture scale: " + item.StableId);
        Scene original = EditorSceneManager.OpenScene(CopperSulfateTransferInstaller.ScenePath, OpenSceneMode.Additive);
        try
        {
            foreach (string name in TableNames)
            {
                Transform table = Table(scene, name), reference = Table(original, name);
                float height = Top(table).bounds.max.y;
                table.localScale = new Vector3(reference.localScale.x, table.localScale.y, reference.localScale.z);
                foreach (Transform cabinet in table.Cast<Transform>().Where(t => t.name.StartsWith("shelf showcase", StringComparison.Ordinal)))
                {
                    Transform previous = reference.Find(cabinet.name);
                    cabinet.localPosition = new Vector3(previous.localPosition.x, cabinet.localPosition.y, previous.localPosition.z);
                    Record(cabinet);
                }
                Bounds dimensions = MeshBounds(Top(reference).gameObject, reference.rotation);
                bool front = name == "MixingStation";
                float sign = name == "PreparationStation" ? -1f : 1f;
                var offset = front ? new Vector3(0f, 0f, 0.42f + dimensions.extents.z) :
                    new Vector3(sign * (0.30f + dimensions.extents.z), 0f, 0.40f - dimensions.extents.x);
                table.rotation = frame * Quaternion.Euler(0f, front ? 0f : sign * 90f, 0f);
                Vector3 desired = start.position + frame * offset;
                desired.y = height;
                table.position += desired - new Vector3(Top(table).bounds.center.x, Top(table).bounds.max.y, Top(table).bounds.center.z);
                Record(table);
            }
        }
        finally { EditorSceneManager.CloseScene(original, true); }
        foreach (var plan in Placements)
        {
            Transform item = items[plan.id].transform;
            item.rotation = frame * Quaternion.Euler(plan.angles);
            Bounds bounds = MeshBounds(item.gameObject, Quaternion.identity);
            Vector3 desired = start.position + frame * new Vector3(plan.offset.x, 0f, plan.offset.y);
            desired.y = Top(Table(scene, plan.table)).bounds.max.y + 0.002f;
            item.position += desired - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            Record(item);
        }
        var heating = Components<CopperSulfateHeatingDetector>(scene).Single();
        heating.Cap.transform.SetPositionAndRotation(heating.CapMount.position, heating.CapMount.rotation);
        Record(heating.Cap.transform);
        foreach (var region in Components<ExperimentLayoutRegion>(scene))
            if (region.PlacementSurface != null)
            {
                region.RefreshFromSurface();
                Record(region.transform);
                EditorUtility.SetDirty(region);
            }
        Components<SceneObjectRegistry>(scene).Single().Refresh();
        Physics.SyncTransforms();
    }

    public static void CheckLayout(Scene scene, List<string> report)
    {
        void Check(bool condition, string message)
        {
            report.Add((condition ? "PASS: " : "FAIL: ") + message);
            if (!condition) throw new InvalidOperationException("FAIL: " + message);
        }
        var start = Components<PicoExperimentStartPose>(scene).Single().PreparationStart;
        var items = Components<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy).ToDictionary(s => s.StableId);
        Check(items.Count == 13 && Placements.All(p => items.ContainsKey(p.id)) && items.ContainsKey("lamp_cap"),
            "All 13 existing apparatus IDs are present; no distractors added");
        var tops = TableNames.Select(name => Top(Table(scene, name))).ToArray();
        foreach (var top in tops)
        {
            Bounds local = MeshBounds(top.gameObject, top.transform.parent.rotation);
            Check(Mathf.Abs(local.size.x - 2.07414f) < 0.001f && Mathf.Abs(local.size.z - 0.94893f) < 0.001f,
                top.transform.parent.name + " restored to 2.074 x 0.949 m");
        }
        for (int i = 0; i < tops.Length; i++)
        for (int j = i + 1; j < tops.Length; j++)
        {
            Bounds a = MeshBounds(tops[i].gameObject, start.rotation), b = MeshBounds(tops[j].gameObject, start.rotation);
            Check(a.max.x <= b.min.x || b.max.x <= a.min.x || a.max.z <= b.min.z || b.max.z <= a.min.z,
                TableNames[i] + " and " + TableNames[j] + " tabletops do not intersect");
        }
        foreach (var plan in Placements)
        {
            var item = items[plan.id];
            Transform table = Table(scene, plan.table);
            Bounds physical = MeshBounds(item.gameObject, Quaternion.identity);
            GameObject support = plan.id == "tripod_stand" ? item.GetComponentsInChildren<MeshFilter>()
                .Single(m => m.name == "pCube89").gameObject : item.gameObject;
            Bounds bounds = MeshBounds(support, table.rotation), top = MeshBounds(Top(table).gameObject, table.rotation);
            Check(bounds.min.x >= top.min.x - 0.001f && bounds.max.x <= top.max.x + 0.001f &&
                bounds.min.z >= top.min.z - 0.001f && bounds.max.z <= top.max.z + 0.001f &&
                bounds.min.y >= top.max.y - 0.001f && bounds.min.y <= top.max.y + 0.005f,
                item.StableId + " is supported by " + plan.table);
            float reach = Vector3.ProjectOnPlane(physical.center - start.position, Vector3.up).magnitude;
            Check(reach <= 0.65f, item.StableId + " horizontal reach=" + reach.ToString("F3") + " m");
            Vector3 relative = Quaternion.Inverse(start.rotation) * (physical.center - start.position);
            Check(Mathf.Abs(Mathf.Atan2(relative.x, relative.z) * Mathf.Rad2Deg) <= 105f,
                item.StableId + " is in the front or side working area");
            report.Add($"POSE {item.StableId} position={item.transform.position:F4} rotation={item.transform.eulerAngles:F2} scale={item.transform.lossyScale:F4}");
        }
        var filter = Components<CopperSulfateFiltrationDetector>(scene).Single();
        var heat = Components<CopperSulfateHeatingDetector>(scene).Single();
        Check(filter.StandConfigured && filter.Stand.gameObject == items["tripod_stand"].gameObject,
            "The original boiling stand owns the filtration mounts");
        foreach (var anchor in new[] { filter.FunnelMount, filter.ReceiverMount, heat.DishMount, heat.LampMount, heat.CapParking })
        {
            Check(anchor.IsChildOf(filter.Stand.transform), anchor.name + " follows the stand");
            float reach = Vector3.ProjectOnPlane(anchor.position - start.position, Vector3.up).magnitude;
            Check(reach <= 0.65f, anchor.name + " horizontal reach=" + reach.ToString("F3") + " m");
        }
        Check(Vector3.Distance(heat.Cap.transform.position, heat.CapMount.position) < 0.001f &&
            Quaternion.Angle(heat.Cap.transform.rotation, heat.CapMount.rotation) < 0.1f, "Lamp cap starts on the moved lamp");
        Physics.SyncTransforms();
        var apparatus = items.Values.Where(s => s.StableId != "lamp_cap").ToArray();
        for (int i = 0; i < apparatus.Length; i++)
        for (int j = i + 1; j < apparatus.Length; j++)
        foreach (var a in apparatus[i].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy))
        foreach (var b in apparatus[j].GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy))
            if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position,
                b.transform.rotation, out _, out float depth) && depth > 0.001f)
                Check(false, "Apparatus overlap: " + apparatus[i].StableId + " / " + apparatus[j].StableId + " depth=" + depth);
        report.Add("PASS: Apparatus physical colliders do not overlap at rest (assembled lamp cap excluded)");
    }

    private static void RenderLayout(Scene scene)
    {
        var start = Components<PicoExperimentStartPose>(scene).Single().PreparationStart;
        CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateLargeTables.top.png",
            start.position + Vector3.up * 3.2f, start.rotation * Quaternion.Euler(90f, 0f, 0f));
        foreach (int yaw in new[] { -70, 0, 70 })
            CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateLargeTables.head" + yaw + ".png",
                start.position + Vector3.up * 1.13f, start.rotation * Quaternion.Euler(35f, yaw, 0f));
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Verify Large Table Variant")]
    public static void Verify()
    {
        CopperSulfateLiquidVerification.RunForScene(ScenePath, CheckLayout, "Temp/CopperSulfateLargeTables.verification.txt");
        File.WriteAllText(ResultPath, "PASS: Large-table geometry and complete chemistry workflow verification.");
    }

    // Ignore placement lines and particle bounds when measuring the physical apparatus.
    internal static Bounds MeshBounds(GameObject item, Quaternion frame)
    {
        Quaternion inverse = Quaternion.Inverse(frame);
        Bounds result = default;
        bool initialized = false;
        foreach (var mesh in item.GetComponentsInChildren<MeshFilter>())
        {
            var renderer = mesh.GetComponent<MeshRenderer>();
            if (mesh.sharedMesh == null || renderer == null || !renderer.enabled || !mesh.gameObject.activeInHierarchy) continue;
            Bounds bounds = mesh.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = inverse * mesh.transform.TransformPoint(corner);
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
        }
        if (!initialized) throw new InvalidOperationException("No active mesh: " + item.name);
        return result;
    }

    private static void AuditSources()
    {
        var report = new List<string>();
        Scene active = SceneManager.GetActiveScene();
        foreach (string path in new[] { CopperSulfateStationarySceneBuilder.ScenePath, CopperSulfateTransferInstaller.ScenePath })
        {
            Scene scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                report.Add("SCENE " + path + " dirty=" + scene.isDirty);
                foreach (string name in TableNames)
                {
                    var table = Table(scene, name);
                    Bounds bounds = MeshBounds(Top(table).gameObject, table.rotation);
                    report.Add($"TABLE {name} scale={table.localScale:F5} size={bounds.size:F5} center={Top(table).bounds.center:F5}");
                    foreach (Transform child in table)
                        report.Add($"CHILD {child.name} position={child.localPosition:F5} scale={child.localScale:F5}");
                }
                foreach (var item in Components<SemanticObject>(scene).Where(item => item.gameObject.activeInHierarchy))
                {
                    Bounds bounds = MeshBounds(item.gameObject, Quaternion.identity);
                    report.Add($"OBJECT {item.StableId} position={item.transform.position:F5} rotation={item.transform.eulerAngles:F3} size={bounds.size:F5} center={bounds.center:F5} min={bounds.min:F5}");
                }
                var heat = Components<CopperSulfateHeatingDetector>(scene).SingleOrDefault();
                if (heat != null)
                    foreach (var anchor in new[] { heat.CapMount, heat.CapParking, heat.DishMount, heat.LampMount })
                        report.Add($"ANCHOR {anchor.name} parent={anchor.parent.name} position={anchor.position:F5}");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
        SceneManager.SetActiveScene(active);
        File.WriteAllLines("Temp/CopperSulfateLargeTables.sources.txt", report);
        File.WriteAllText(ResultPath, "Source audit complete.");
    }
}
