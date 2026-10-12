using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[InitializeOnLoad]
public static class CopperSulfatePairedLayoutInstaller
{
    public const string ScenePath = CopperSulfateLargeTableSceneBuilder.ScenePath;
    private const string ContainerName = "Paired Layout Apparatus";
    private const string Output = "Temp/PairedLayout";
    private static readonly string[] Stations = { "PreparationStation", "MixingStation", "FilteringStation" };
    private static bool _busy;

    static CopperSulfatePairedLayoutInstaller() => EditorApplication.update += ProcessRequest;

    private static void ProcessRequest()
    {
        const string request = "Temp/PairedLayout.request";
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (command == "install") Install();
            else if (command == "verify") Verify();
            else if (command == "runtime") PairedLayoutPlayModeVerification.Run();
            else if (command == "audit")
            {
                Directory.CreateDirectory(Output);
                var scene = SceneManager.GetActiveScene();
                File.WriteAllLines(Output + "/audit.txt", All<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy).Select(s =>
                    $"{s.StableId} active={s.gameObject.activeInHierarchy} root={s.transform.root.name} parent={s.transform.parent?.name} size={BoundsOf(s.gameObject).size}"));
                Render(scene, "audit");
            }
            else throw new InvalidOperationException("Unknown paired layout command: " + command);
        }
        catch (Exception exception)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/result.txt", exception.ToString());
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static Bounds BoundsOf(GameObject item) => CopperSulfateLargeTableSceneBuilder.MeshBounds(item, Quaternion.identity);

    [MenuItem("Tools/Stream Manager/Two Level Layout/Install Paired Layout")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        var active = SceneManager.GetActiveScene();
        var backup = "MigrationBackups/BeforePairedLayoutScene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, Path.Combine(backup, Path.GetFileName(ScenePath)));
        if (active.IsValid() && active.isDirty && !EditorSceneManager.SaveScene(active, backup + "/UnsavedScene.unity", true))
            throw new IOException("Could not preserve the unsaved scene.");
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var generator = All<ExperimentDistractorLayoutGenerator>(scene).Single();
        if (generator.PairedLayout != null) throw new InvalidOperationException("Paired layout already installed. Use Low/High or Verify.");
        foreach (var unfinished in scene.GetRootGameObjects().Where(root => root.name == ContainerName))
            UnityEngine.Object.DestroyImmediate(unfinished);
        Directory.CreateDirectory(Output);
        var originals = All<SemanticObject>(scene).Where(item => item.gameObject.activeInHierarchy).ToArray();
        if (originals.Length != 13) throw new InvalidOperationException("Expected the clean scene with 13 task objects, found " + originals.Length + ": " + string.Join(", ", originals.Select(s => s.StableId + "@" + s.transform.root.name)));
        var transforms = All<Transform>(scene).ToDictionary(t => t, t => (t.position, t.rotation, t.lossyScale));
        var catalog = originals.ToDictionary(s => s, s => JsonUtility.ToJson(SemanticObjectSummary.From(s)));
        var protectedBounds = originals.Select(item => BoundsOf(item.gameObject)).ToList();
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
        var definitions = generator.Definitions.ToDictionary(def => def.objectType);
        var container = new GameObject(ContainerName);
        var items = new List<ExperimentPairedLayoutController.Item>();
        var occupied = new List<Bounds>();
        var random = new System.Random(20260811);
        var sharedTypes = new[] { new[] { "graduated_cylinder", "test_tube" },
            new[] { "beaker", "erlenmeyer_flask" }, new[] { "beaker", "crucible" } };
        var extraTypes = new[] { "erlenmeyer_flask", "florence_flask", "test_tube", "crucible_tongs" };
        for (int tableIndex = 0; tableIndex < Stations.Length; tableIndex++)
        {
            string station = Stations[tableIndex];
            var table = scene.GetRootGameObjects().Single(root => root.name == station).transform;
            var surface = CopperSulfateStationarySceneBuilder.FindTabletop(table);
            var tableBounds = BoundsOf(surface.gameObject);
            var group = new GameObject(station + " Extras").transform;
            group.SetParent(container.transform, false);
            var types = sharedTypes[tableIndex].Concat(extraTypes).ToArray();
            for (int i = 0; i < types.Length; i++)
            {
                var definition = definitions[types[i]];
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(definition.prefab, scene);
                PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                instance.transform.SetParent(group, false);
                instance.name = definition.displayName;
                NormalizeSize(instance, types[i]);
                Place(instance, tableBounds, protectedBounds, occupied, seat, random);
                ConfigureInteraction(instance);
                string id = $"layout_a_{tableIndex + 1}_{i + 1}_{types[i]}";
                var semantic = GetOrAdd<SemanticObject>(instance);
                semantic.ConfigureDetailed(id, definition.displayName, definition.objectType, definition.category,
                    definition.function, definition.color, definition.transparency, definition.shape, definition.materials,
                    "none", "none", new[] { "empty" }, definition.aliases, true, true);
                items.Add(new ExperimentPairedLayoutController.Item(instance, station, i < 2));
                occupied.Add(BoundsOf(instance));
            }
        }
        var registry = All<SceneObjectRegistry>(scene).Single();
        var registrySettings = new SerializedObject(registry);
        registrySettings.FindProperty("includeInactiveObjects").boolValue = false;
        registrySettings.ApplyModifiedPropertiesWithoutUndo();
        var paired = generator.gameObject.AddComponent<ExperimentPairedLayoutController>();
        paired.Configure(All<PreparationSequenceController>(scene).Single(), All<ExperimentStateController>(scene).Single(),
            registry, All<ExperimentLogger>(scene).Single(), items);
        generator.SetPairedLayout(paired);
        foreach (var entry in transforms)
            if (Vector3.Distance(entry.Key.position, entry.Value.position) > 0.00001f ||
                Quaternion.Angle(entry.Key.rotation, entry.Value.rotation) > 0.001f ||
                Vector3.Distance(entry.Key.lossyScale, entry.Value.lossyScale) > 0.00001f)
                throw new InvalidOperationException("An existing transform changed: " + entry.Key.name);
        if (catalog.Any(entry => entry.Value != JsonUtility.ToJson(SemanticObjectSummary.From(entry.Key))))
            throw new InvalidOperationException("Task object semantics changed.");
        Validate(scene, true);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        File.WriteAllText(Output + "/result.txt", "PASS: Low=19, High=31. Original transforms and semantics preserved.\nScene backup: " + backup);
    }

    private static void NormalizeSize(GameObject instance, string type)
    {
        instance.transform.position = Vector3.zero;
        instance.transform.rotation = Quaternion.identity;
        var bounds = BoundsOf(instance);
        float dimension = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
        float size = type == "graduated_cylinder" ? 0.20f : type == "beaker" ? 0.12f :
            type == "test_tube" ? 0.15f : type == "crucible" ? 0.095f : type == "crucible_tongs" ? 0.19f : 0.15f;
        instance.transform.localScale *= size / dimension;
        if ((type == "test_tube" || type == "crucible_tongs") && bounds.size.y > Mathf.Max(bounds.size.x, bounds.size.z))
            instance.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
    }

    private static bool OverlapXZ(Bounds a, Bounds b, float clearance)
    {
        return a.min.x < b.max.x + clearance && a.max.x > b.min.x - clearance &&
            a.min.z < b.max.z + clearance && a.max.z > b.min.z - clearance;
    }

    private static void Place(GameObject item, Bounds table, List<Bounds> targets, List<Bounds> occupied,
        Transform seat, System.Random random)
    {
        Quaternion flatRotation = item.transform.rotation;
        var eye = seat.position + Vector3.up * 1.13f;
        Vector3 bestPosition = default;
        Quaternion bestRotation = default;
        float bestScore = float.PositiveInfinity;
        int reachRejected = 0, targetRejected = 0, occupiedRejected = 0, viewRejected = 0;
        for (int attempt = 0; attempt < 1600; attempt++)
        {
            item.transform.rotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f) * flatRotation;
            item.transform.position = Vector3.zero;
            var local = BoundsOf(item);
            float x = Mathf.Lerp(table.min.x + 0.045f + local.extents.x, table.max.x - 0.045f - local.extents.x, (float)random.NextDouble());
            float z = Mathf.Lerp(table.min.z + 0.045f + local.extents.z, table.max.z - 0.045f - local.extents.z, (float)random.NextDouble());
            item.transform.position = new Vector3(x - local.center.x, table.max.y + 0.002f - local.min.y, z - local.center.z);
            var bounds = BoundsOf(item);
            float reach = Vector2.Distance(new Vector2(x, z), new Vector2(seat.position.x, seat.position.z));
            if (reach > 1.35f) { reachRejected++; continue; }
            if (targets.Any(target => OverlapXZ(bounds, target, 0.08f))) { targetRejected++; continue; }
            if (occupied.Any(other => OverlapXZ(bounds, other, 0.045f))) { occupiedRejected++; continue; }
            // Keep the central seated view of each task object clear.
            bool blocks = targets.Any(target => {
                var delta = target.center - eye;
                return bounds.IntersectRay(new Ray(eye, delta.normalized), out float hit) && hit < delta.magnitude;
            });
            if (blocks) { viewRejected++; continue; }
            float nearest = targets.Min(target => Vector2.Distance(new Vector2(x, z), new Vector2(target.center.x, target.center.z)));
            float score = Mathf.Abs(nearest - 0.27f) + Mathf.Max(0f, reach - 0.8f) * 1.4f + (float)random.NextDouble() * 0.025f;
            if (score >= bestScore) continue;
            bestScore = score; bestPosition = item.transform.position; bestRotation = item.transform.rotation;
        }
        if (float.IsPositiveInfinity(bestScore)) throw new InvalidOperationException(
            $"No safe placement for {item.name}; placed={occupied.Count}; table={table}; size={BoundsOf(item).size}; " +
            $"seat={seat.position}; rejected reach={reachRejected}, targets={targetRejected}, occupied={occupiedRejected}, view={viewRejected}");
        item.transform.SetPositionAndRotation(bestPosition, bestRotation);
    }

    private static void ConfigureInteraction(GameObject item)
    {
        foreach (var t in item.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
            t.gameObject.layer = 0;
        }
        // Prefab colliders often include the vessel interior; use a stable grab volume for unused apparatus.
        foreach (var old in item.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(old);
        var worldRotation = item.transform.rotation;
        item.transform.rotation = Quaternion.identity;
        var localBounds = BoundsOf(item);
        var collider = item.AddComponent<BoxCollider>();
        collider.center = item.transform.InverseTransformPoint(localBounds.center);
        collider.size = new Vector3(localBounds.size.x / item.transform.lossyScale.x,
            localBounds.size.y / item.transform.lossyScale.y, localBounds.size.z / item.transform.lossyScale.z);
        item.transform.rotation = worldRotation;
        var body = GetOrAdd<Rigidbody>(item);
        body.useGravity = false; body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var grab = GetOrAdd<XRGrabInteractable>(item);
        grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false; grab.retainTransformParent = true;
        grab.useDynamicAttach = true;
        grab.colliders.Clear(); grab.colliders.Add(collider);
        if (item.GetComponent<ExperimentGrabReturn>() == null) item.AddComponent<ExperimentGrabReturn>();
    }

    private static T GetOrAdd<T>(GameObject item) where T : Component
    {
        var component = item.GetComponent<T>();
        return component != null ? component : item.AddComponent<T>();
    }

    [MenuItem("Tools/Stream Manager/Two Level Layout/Low (19 Objects)")]
    public static void Low() => Select(ExperimentLayoutSelection.Low);
    [MenuItem("Tools/Stream Manager/Two Level Layout/High (31 Objects)")]
    public static void High() => Select(ExperimentLayoutSelection.High);

    private static void Select(ExperimentLayoutSelection selection)
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open the large-table scene first.");
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        if (!paired.TryApply(selection, "editor_menu", null, out string error)) throw new InvalidOperationException(error);
        if (!Application.isPlaying)
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    [MenuItem("Tools/Stream Manager/Two Level Layout/Verify And Render")]
    public static void Verify()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Validate(scene, true);
    }

    private static void Validate(Scene scene, bool render)
    {
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        var registry = All<SceneObjectRegistry>(scene).Single();
        var originalSelection = paired.Selection;
        var originalObjects = All<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy && !s.StableId.StartsWith("layout_a_")).ToArray();
        var report = new List<string>();
        Require(originalObjects.Length == 13, "13 original task objects", report);
        var referencePoses = paired.Items.ToDictionary(item => item, item => (item.homePosition, item.homeRotation));
        foreach (var level in new[] { ExperimentLayoutSelection.Low, ExperimentLayoutSelection.High })
        {
            Require(paired.TryApply(level, "verification", null, out string error), error ?? "Apply " + level, report);
            int count = level == ExperimentLayoutSelection.Low ? 6 : 18;
            Require(paired.ActiveCount == count && registry.Count == count + 13, $"{level}: {count} extras, {count + 13} catalog objects", report);
            foreach (string station in Stations)
                Require(paired.Items.Count(item => item.station == station && item.gameObject.activeSelf) == count / 3, level + " balanced on " + station, report);
            foreach (var item in paired.Items)
            {
                var semantic = item.gameObject.GetComponent<SemanticObject>();
                Require(registry.TryGet(semantic.StableId, out _) == item.gameObject.activeSelf, level + " catalog membership " + semantic.StableId, report);
                Require(semantic.Selectable && semantic.Grabbable && item.gameObject.GetComponent<XRGrabInteractable>() != null &&
                    item.gameObject.GetComponent<ExperimentGrabReturn>() != null, "Selectable/grabbable/return " + semantic.StableId, report);
                Require(item.gameObject.transform.localPosition == referencePoses[item].homePosition &&
                    Quaternion.Angle(item.gameObject.transform.localRotation, referencePoses[item].homeRotation) < 0.001f, "Stable pose " + semantic.StableId, report);
                if (!item.gameObject.activeSelf) continue;
                var table = scene.GetRootGameObjects().Single(root => root.name == item.station).transform;
                var top = BoundsOf(CopperSulfateStationarySceneBuilder.FindTabletop(table).gameObject);
                var bounds = BoundsOf(item.gameObject);
                Require(bounds.min.x >= top.min.x && bounds.max.x <= top.max.x && bounds.min.z >= top.min.z && bounds.max.z <= top.max.z &&
                    Mathf.Abs(bounds.min.y - top.max.y - 0.002f) < 0.001f, "Supported on tabletop " + semantic.StableId, report);
                Require(!originalObjects.Any(target => OverlapXZ(bounds, BoundsOf(target.gameObject), 0.079f)), "Task clearance " + semantic.StableId, report);
                report.Add($"POSE {level} {semantic.StableId} station={item.station} position={item.gameObject.transform.position:F4} size={bounds.size:F4}");
            }
            var activeItems = paired.Items.Where(item => item.gameObject.activeSelf).ToArray();
            for (int a = 0; a < activeItems.Length; a++)
            for (int b = a + 1; b < activeItems.Length; b++)
                Require(!OverlapXZ(BoundsOf(activeItems[a].gameObject), BoundsOf(activeItems[b].gameObject), 0.044f), "Extras separated", report);
            if (render) Render(scene, level.ToString().ToLowerInvariant());
        }
        Require(!paired.TryApply(ExperimentLayoutSelection.Medium, "verification", null, out _), "Medium rejected", report);
        Require(!paired.TryApply(ExperimentLayoutSelection.Clear, "verification", null, out _), "Clear rejected", report);
        Require(!paired.TryApply(ExperimentLayoutSelection.Low, "verification", 7, out _), "Unbuilt seed rejected", report);
        paired.TryApply(originalSelection, "verification_restore", null, out _);
        Directory.CreateDirectory(Output);
        File.WriteAllLines(Output + "/verification.txt", report);
    }

    private static void Require(bool condition, string detail, List<string> report)
    {
        if (!condition) throw new InvalidOperationException(detail);
        report.Add("PASS: " + detail);
    }

    private static void Render(Scene scene, string level)
    {
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
        foreach (var view in new[] { ("front", 0f), ("left", -70f), ("right", 70f) })
            CopperSulfateLiquidVerification.Render(scene, Output + "/" + level + "-" + view.Item1 + ".png",
                seat.position + Vector3.up * 1.13f, seat.rotation * Quaternion.Euler(25f, view.Item2, 0f), false);
    }
}
