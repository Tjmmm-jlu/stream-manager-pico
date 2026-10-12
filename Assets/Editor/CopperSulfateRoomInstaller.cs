using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class CopperSulfateRoomInstaller
{
    private const string RequestPath = "Temp/CopperSulfateRoom.request";
    private const string ResultPath = "Temp/CopperSulfateRoom.result.txt";
    private const string WallPrefab = "Assets/3D Laboratory Environment with Appratus/Prefabs/wall01.prefab";
    private const string RoomName = "Laboratory Room Completion";
    private static bool _busy;

    static CopperSulfateRoomInstaller() => EditorApplication.update += ProcessRequest;

    private static void ProcessRequest()
    {
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != CopperSulfateLargeTableSceneBuilder.ScenePath)
                throw new InvalidOperationException("Open the large-table scene first.");
            if (command == "audit") Audit(scene);
            else if (command == "install") Install();
            else throw new InvalidOperationException("Unknown room command: " + command);
        }
        catch (Exception exception) { File.WriteAllText(ResultPath, exception.ToString()); Debug.LogException(exception); }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Copper Sulfate/Complete Laboratory Walls")]
    public static void Install()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != CopperSulfateLargeTableSceneBuilder.ScenePath)
            throw new InvalidOperationException("Open the large-table scene outside Play Mode first.");
        string backup = "MigrationBackups/BeforeLaboratoryWalls_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(scene.path, Path.Combine(backup, Path.GetFileName(scene.path)));
        if (scene.isDirty && !EditorSceneManager.SaveScene(scene, Path.Combine(backup, "UnsavedScene.unity"), true))
            throw new IOException("Could not preserve unsaved scene changes.");
        var roots = scene.GetRootGameObjects();
        var semantics = roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>(true)).ToArray();
        var catalog = semantics.ToDictionary(s => s, s => JsonUtility.ToJson(SemanticObjectSummary.From(s)));
        var unchanged = semantics.SelectMany(s => s.GetComponentsInChildren<Transform>(true))
            .Concat(roots.Where(r => r.name.EndsWith("Station") || r.name == "PICO XR Origin" ||
                r.name == "Stationary Start Pose" || r.name == "floor (1)")
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true))).Distinct()
            .ToDictionary(t => t, t => (t.position, t.rotation, t.lossyScale));
        Configure(scene);
        var report = new List<string>();
        foreach (var entry in unchanged)
            if (Vector3.Distance(entry.Key.position, entry.Value.position) > 0.00001f ||
                Quaternion.Angle(entry.Key.rotation, entry.Value.rotation) > 0.001f ||
                Vector3.Distance(entry.Key.lossyScale, entry.Value.lossyScale) > 0.00001f)
                throw new InvalidOperationException("Unexpected task transform change: " + entry.Key.name);
        if (catalog.Any(entry => entry.Value != JsonUtility.ToJson(SemanticObjectSummary.From(entry.Key))))
            throw new InvalidOperationException("Apparatus semantics changed.");
        report.Add("PASS: All apparatus, table, floor and XR transforms are preserved; semantic catalog is unchanged.");
        ValidateEnclosure(scene, report);
        RenderViews(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save laboratory walls.");
        AssetDatabase.SaveAssets();
        report.Add("Backup: " + backup);
        File.WriteAllLines("Temp/CopperSulfateRoom.verification.txt", report);
        File.WriteAllText(ResultPath, "PASS: Laboratory walls and ceiling completed.\nBackup: " + backup);
        Debug.Log("[CopperRoom] Completed walls and ceiling in " + scene.path);
    }

    public static void Configure(Scene scene)
    {
        var roots = scene.GetRootGameObjects();
        var roofs = roots.Where(r => r.name == "roof" || r.name.StartsWith("roof (", StringComparison.Ordinal)).ToArray();
        if (roofs.Length == 0) throw new InvalidOperationException("Existing laboratory ceiling is missing.");
        Bounds room = CopperSulfateLargeTableSceneBuilder.MeshBounds(roofs[0], Quaternion.identity);
        foreach (var roof in roofs.Skip(1)) room.Encapsulate(CopperSulfateLargeTableSceneBuilder.MeshBounds(roof, Quaternion.identity));
        var floor = roots.Single(r => r.name == "floor (1)").GetComponent<Renderer>().bounds;
        if (room.min.x < floor.min.x || room.max.x > floor.max.x || room.min.z < floor.min.z || room.max.z > floor.max.z)
            throw new InvalidOperationException("Ceiling footprint extends past the existing floor.");
        Transform group = roots.FirstOrDefault(r => r.name == RoomName)?.transform;
        if (group == null)
        {
            var go = new GameObject(RoomName);
            SceneManager.MoveGameObjectToScene(go, scene);
            group = go.transform;
        }
        const string wallMaterialPath = "Assets/Guidance/AStage/LaboratoryWalls.mat";
        var wallMaterial = AssetDatabase.LoadAssetAtPath<Material>(wallMaterialPath);
        if (wallMaterial == null)
        {
            wallMaterial = new Material(Shader.Find("Standard"))
            {
                name = "Laboratory Walls",
                color = new Color(0.60f, 0.63f, 0.64f)
            };
            wallMaterial.SetFloat("_Glossiness", 0.05f);
            wallMaterial.SetFloat("_Metallic", 0f);
            AssetDatabase.CreateAsset(wallMaterial, wallMaterialPath);
        }
        GameObject NewWall(string name)
        {
            Transform found = group.Find(name);
            if (found != null) return found.gameObject;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(WallPrefab);
            if (prefab == null) throw new InvalidOperationException("Laboratory wall prefab is missing.");
            var wall = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            wall.name = name;
            wall.transform.SetParent(group, true);
            return wall;
        }
        void FitWall(GameObject wall, float yaw, float length, Vector2 center)
        {
            Transform transform = wall.transform;
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
            Bounds original = CopperSulfateLargeTableSceneBuilder.MeshBounds(wall, Quaternion.identity);
            float height = room.max.y - floor.max.y + 0.04f;
            transform.localScale = new Vector3(length / original.size.x, height / original.size.y, 0.2451068f / original.size.z);
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            Bounds fitted = CopperSulfateLargeTableSceneBuilder.MeshBounds(wall, Quaternion.identity);
            transform.position += new Vector3(center.x, floor.max.y - 0.02f, center.y) -
                new Vector3(fitted.center.x, fitted.min.y, fitted.center.z);
            foreach (var renderer in wall.GetComponentsInChildren<MeshRenderer>())
            {
                renderer.sharedMaterial = wallMaterial;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(transform);
            EditorUtility.SetDirty(transform);
        }
        const float halfThickness = 0.1225534f;
        // Extend past each corner so adjacent wall faces cannot leave a visible slit.
        FitWall(roots.Single(r => r.name == "wall01 (4)"), 0f, room.size.x + halfThickness * 4f,
            new Vector2(room.center.x, room.min.z - halfThickness + 0.003f));
        FitWall(roots.Single(r => r.name == "wall01 (5)"), 90f, room.size.z + halfThickness * 4f,
            new Vector2(room.max.x + halfThickness - 0.003f, room.center.z));
        FitWall(NewWall("Laboratory Wall Front"), 180f, room.size.x + halfThickness * 4f,
            new Vector2(room.center.x, room.max.z + halfThickness - 0.003f));
        FitWall(NewWall("Laboratory Wall Left"), 270f, room.size.z + halfThickness * 4f,
            new Vector2(room.min.x - halfThickness + 0.003f, room.center.z));

        var backing = group.Find("Ceiling Closure");
        if (backing == null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Ceiling Closure";
            SceneManager.MoveGameObjectToScene(go, scene);
            go.transform.SetParent(group, true);
            backing = go.transform;
        }
        backing.SetPositionAndRotation(new Vector3(room.center.x, room.max.y + 0.03f, room.center.z), Quaternion.identity);
        backing.localScale = new Vector3(room.size.x + 0.05f, 0.06f, room.size.z + 0.05f);
        const string materialPath = "Assets/Guidance/AStage/LaboratoryCeiling.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Standard")) { name = "Laboratory Ceiling", color = new Color(0.8f, 0.82f, 0.83f) };
            material.SetFloat("_Glossiness", 0.08f);
            AssetDatabase.CreateAsset(material, materialPath);
        }
        backing.GetComponent<Renderer>().sharedMaterial = material;
        Physics.SyncTransforms();
    }

    private static void ValidateEnclosure(Scene scene, List<string> report)
    {
        var roots = scene.GetRootGameObjects();
        var start = roots.SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>(true)).Single().PreparationStart;
        var shell = roots.Where(r => r.name == RoomName || r.name == "wall01 (4)" || r.name == "wall01 (5)")
            .SelectMany(r => r.GetComponentsInChildren<Collider>()).Where(c => c.enabled).ToArray();
        var enclosure = shell.Concat(roots.Single(r => r.name == "floor (1)").GetComponentsInChildren<Collider>()).ToArray();
        foreach (float height in new[] { 1.13f, 1.65f })
        for (int yaw = 0; yaw < 360; yaw += 10)
        foreach (float pitch in new[] { -85f, -45f, 0f, 45f, 85f })
        {
            var ray = new Ray(start.position + Vector3.up * height, Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward);
            if (!enclosure.Any(c => c.Raycast(ray, out _, 15f)))
                throw new InvalidOperationException($"Open room boundary: height={height}, yaw={yaw}, pitch={pitch}");
        }
        report.Add("PASS: 360 direction/height samples hit walls, floor or ceiling; no open boundary around the user.");
        foreach (var item in roots.SelectMany(r => r.GetComponentsInChildren<SemanticObject>()).Where(s => s.gameObject.activeInHierarchy))
        {
            Vector3 eye = start.position + Vector3.up * 1.13f;
            Vector3 target = CopperSulfateLargeTableSceneBuilder.MeshBounds(item.gameObject, Quaternion.identity).center;
            Vector3 delta = target - eye;
            if (shell.Any(c => c.Raycast(new Ray(eye, delta.normalized), out _, delta.magnitude)))
                throw new InvalidOperationException("Room geometry blocks apparatus: " + item.StableId);
        }
        report.Add("PASS: The room shell does not obstruct any active apparatus from the seated viewpoint.");
    }

    private static void RenderViews(Scene scene)
    {
        var start = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PicoExperimentStartPose>(true))
            .Single().PreparationStart;
        foreach (var view in new[] { ("front", 0f, 20f), ("left", -90f, 20f), ("right", 90f, 20f),
            ("back", 180f, 10f), ("ceiling", 0f, -75f) })
            CopperSulfateLiquidVerification.Render(scene, "Temp/CopperSulfateRoom." + view.Item1 + ".png",
                start.position + Vector3.up * 1.13f, start.rotation * Quaternion.Euler(view.Item3, view.Item2, 0f), false);
    }

    private static void Audit(Scene scene)
    {
        var report = new List<string>();
        foreach (var root in scene.GetRootGameObjects().Where(r => r.activeInHierarchy))
        {
            if (!(root.name.StartsWith("wall01") || root.name.StartsWith("roof") ||
                root.name.StartsWith("floor") || root.name.StartsWith("pillar") || root.name.StartsWith("stand glass"))) continue;
            Bounds bounds = CopperSulfateLargeTableSceneBuilder.MeshBounds(root, Quaternion.identity);
            report.Add($"{root.name} center={bounds.center:F4} size={bounds.size:F4} min={bounds.min:F4} max={bounds.max:F4} rotation={root.transform.eulerAngles:F2}");
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>())
                report.Add("MATERIAL " + renderer.name + " " + string.Join(", ", renderer.sharedMaterials.Select(AssetDatabase.GetAssetPath)));
        }
        foreach (var light in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)))
            report.Add($"LIGHT {light.name} enabled={light.enabled} active={light.gameObject.activeInHierarchy} type={light.type} position={light.transform.position:F4} intensity={light.intensity} shadows={light.shadows}");
        File.WriteAllLines("Temp/CopperSulfateRoom.audit.txt", report);
        File.WriteAllText(ResultPath, "Room audit complete.");
    }
}
