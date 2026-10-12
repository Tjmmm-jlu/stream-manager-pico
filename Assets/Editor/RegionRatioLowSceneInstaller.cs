using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>为已完成的连续实验生成三区域 1:3 低档布局，保留任务检测器引用。</summary>
/// <remarks>2026-10-08 v2：干扰物改用抖动网格候选池（ExperimentJitteredDensityLayout），
/// 池耗尽时自动回退原随机算法；任务物体仍走原算法以保证操作几何不变。
/// 在双档场景（91 件已装）上再次运行本菜单会执行"整组重建"：清空两档 78 件干扰物后重新生成低档 39 件，
/// 完成后需再运行 Regional Density > Install High (91 Objects)，或直接使用该菜单下的 Rebuild Pair。</remarks>
[InitializeOnLoad]
public static class RegionRatioLowSceneInstaller
{
    public const string ScenePath = CopperSulfateLargeTableSceneBuilder.ScenePath;
    public const string Output = "Temp/RegionRatioLow";
    public const string Profile = "regional-low-prefabs-1to3-v2";
    private const string RootName = "Regional Low Apparatus";
    internal const string PrefabFolder = "Assets/3D Laboratory Environment with Appratus/Prefabs/";
    private static bool _busy;
    internal static readonly string[] Stations = { "PreparationStation", "MixingStation", "FilteringStation" };
    internal static readonly string[][] Tasks = {
        new[] { "copper_sulfate_crude", "forceps", "graduated_cylinder", "water_source" },
        new[] { "beaker", "glass_rod", "glass_funnel", "long_lighter" },
        new[] { "tripod_stand", "filtrate_beaker", "china_dish", "spirit_lamp", "lamp_cap" }
    };
    internal static readonly string[][] Types = {
        new[] { "graduated_cylinder", "graduated_cylinder", "forceps", "erlenmeyer_flask", "erlenmeyer_flask",
            "florence_flask", "florence_flask", "beaker", "beaker", "test_tube", "crucible", "crucible_tongs" },
        new[] { "beaker", "test_tube", "glass_funnel", "test_tube", "test_tube", "erlenmeyer_flask",
            "erlenmeyer_flask", "florence_flask", "florence_flask", "graduated_cylinder", "crucible_tongs", "crucible" },
        new[] { "beaker", "china_dish", "spirit_lamp", "crucible", "crucible", "test_tube", "test_tube",
            "erlenmeyer_flask", "erlenmeyer_flask", "florence_flask", "florence_flask", "crucible_tongs",
            "graduated_cylinder", "forceps", "glass_funnel" }
    };
    internal static readonly Dictionary<string, string> PrefabNames = new Dictionary<string, string> {
        { "beaker", "Beaker.prefab" }, { "graduated_cylinder", "Graduated_Cylinder.prefab" },
        { "forceps", "forceps.prefab" }, { "erlenmeyer_flask", "Erlenmeyer_flask.prefab" },
        { "florence_flask", "florence_flask.prefab" }, { "test_tube", "Glass_Lab_test_tube.prefab" },
        { "glass_funnel", "glass_funnel.prefab" }, { "china_dish", "china dish.prefab" },
        { "spirit_lamp", "Spirit_Lamp with water.prefab" }, { "crucible", "crucible_and_cover.prefab" },
        { "crucible_tongs", "Crucible_Tongs.prefab" }
    };

    static RegionRatioLowSceneInstaller() => EditorApplication.update += ProcessRequest;
    private static void ProcessRequest()
    {
        const string request = "Temp/RegionRatioLow.request";
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        _busy = true;
        Directory.CreateDirectory(Output);
        try
        {
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (command == "install") Install();
            else if (command == "verify") Verify();
            else if (command == "runtime") RegionRatioLowRuntimeVerification.Run();
            else if (command == "inspect")
            {
                var scene = SceneManager.GetActiveScene();
                File.WriteAllLines(Output + "/bounds.txt", Enumerable.Range(0, 3).Select(i => Stations[i] + " " + Top(scene, i))
                    .Concat(All<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy).Select(s => s.StableId + " " + BoundsOf(s.gameObject))));
                Render(scene);
            }
            else throw new InvalidOperationException("Unknown regional command: " + command);
        }
        catch (Exception ex) { File.WriteAllText(Output + "/result.txt", "FAIL: " + ex); Debug.LogException(ex); }
        finally { _busy = false; }
    }
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    internal static Bounds BoundsOf(GameObject item) => CopperSulfateLargeTableSceneBuilder.MeshBounds(item, Quaternion.identity);
    internal static Bounds Top(Scene scene, int index) => BoundsOf(CopperSulfateStationarySceneBuilder.FindTabletop(
        scene.GetRootGameObjects().Single(root => root.name == Stations[index]).transform).gameObject);

    [MenuItem("Tools/Stream Manager/Regional Low/Install 1 to 3 Layout (52 Objects)")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play Mode first.");
        Directory.CreateDirectory(Output);
        var backup = "MigrationBackups/BeforeRegionRatioScene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, backup + "/SavedScene.unity");
        var active = SceneManager.GetActiveScene();
        foreach (var scene in Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt))
            if (scene.isDirty && !EditorSceneManager.SaveScene(scene, backup + "/Unsaved_" + scene.handle + ".unity", true))
                throw new IOException("Cannot back up the unsaved scene.");
        var current = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (current.GetRootGameObjects().Any(r => r.name == RootName) &&
            All<ExperimentPairedLayoutController>(current).Single().ProfileId == Profile && !File.ReadAllText(ScenePath).Contains(Profile))
            current = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var paired = All<ExperimentPairedLayoutController>(current).Single();
        // 双档场景（52+91 已装）允许"整组重建"：销毁两档全部干扰实例后，按低档流程用新算法重新生成 39 件；
        // 完成后需再运行 Regional Density > Install High 重新追加高档。
        bool rebuildingPaired = paired.ProfileId == RegionRatioHighSceneInstaller.Profile;
        if (rebuildingPaired)
        {
            Debug.Log("[RegionRatioLow] 检测到双档布局：执行整组重建（先清空两档干扰物，再重新生成 39 件低档）。");
        }
        else if (paired.ProfileId == Profile)
        {
            throw new InvalidOperationException("Already installed; use Verify instead.");
        }

        bool upgradingRegional = rebuildingPaired || paired.ProfileId.StartsWith("regional-low", StringComparison.Ordinal);
        var generator = All<ExperimentDistractorLayoutGenerator>(current).Single();
        var source = All<SemanticObject>(current).Where(s => s.gameObject.activeInHierarchy && Tasks.SelectMany(t => t).Contains(s.StableId))
            .ToDictionary(s => s.StableId);
        if (source.Count != 13) throw new InvalidOperationException("Expected 13 enabled task objects.");
        var semantics = source.ToDictionary(p => p.Key, p => JsonUtility.ToJson(SemanticObjectSummary.From(p.Value)));
        var originalSizes = source.ToDictionary(p => p.Key, p => p.Value.transform.lossyScale);
        var originalPoses = source.ToDictionary(p => p.Key, p => (p.Value.transform.position, p.Value.transform.rotation));
        // 只清理由旧布局拥有的干扰实例；旧场景磁盘副本已经保存。
        foreach (var item in paired.Items) if (item.gameObject != null) UnityEngine.Object.DestroyImmediate(item.gameObject);
        foreach (var root in current.GetRootGameObjects().Where(r => r.name == "Paired Layout Apparatus" || r.name == RootName))
            UnityEngine.Object.DestroyImmediate(root);
        var container = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(container, current);
        var random = new System.Random(20260915);
        ExperimentJitteredDensityLayout.ResetAll(20260915); // 抖动网格 v2：与本实验设种一致，重建候选池
        // 如需"操作区禁放"（高风险动作区），在放置前登记世界 XZ 矩形（x, z, width, depth），例如：
        // ExperimentJitteredDensityLayout.SetActionZones(Top(current, 2), new Rect(x, z, width, depth));
        var seat = All<PicoExperimentStartPose>(current).Single().PreparationStart;
        var protectedBounds = source.Where(p => upgradingRegional || p.Key != "china_dish" && p.Key != "spirit_lamp" && p.Key != "lamp_cap" && p.Key != "filtrate_beaker")
            .Select(p => BoundsOf(p.Value.gameObject)).ToList();
        if (!upgradingRegional)
        {
        Place(source["filtrate_beaker"].gameObject, Top(current, 2), protectedBounds, new List<Bounds>(), seat, random, true);
        protectedBounds.Add(BoundsOf(source["filtrate_beaker"].gameObject));
        Place(source["china_dish"].gameObject, Top(current, 2), protectedBounds, new List<Bounds>(), seat, random, true);
        protectedBounds.Add(BoundsOf(source["china_dish"].gameObject));
        var heating = All<CopperSulfateHeatingDetector>(current).Single();
        var oldLampPosition = heating.Lamp.transform.position;
        Place(source["spirit_lamp"].gameObject, Top(current, 2), protectedBounds, new List<Bounds>(), seat, random, true);
        if (heating.CapParking != null && !heating.CapParking.IsChildOf(heating.Lamp.transform))
            heating.CapParking.position += heating.Lamp.transform.position - oldLampPosition;
        heating.Cap.transform.SetPositionAndRotation(heating.CapMount.position, heating.CapMount.rotation);
        protectedBounds.Add(BoundsOf(heating.Lamp.gameObject));
        protectedBounds.Add(BoundsOf(heating.Cap.gameObject));
        }
        var definitions = generator.Definitions.ToDictionary(d => d.objectType);
        var items = new List<ExperimentPairedLayoutController.Item>();
        var occupied = new List<Bounds>();
        for (int region = 0; region < 3; region++)
        {
            var group = new GameObject(Stations[region] + " Extras"); group.transform.SetParent(container.transform, false);
            for (int index = 0; index < Types[region].Length; index++)
            {
                string type = Types[region][index];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabFolder + PrefabNames[type]);
                if (prefab == null) throw new FileNotFoundException("Required package prefab: " + PrefabNames[type]);
                var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, current);
                // 保留原资源包的模型、材质与 Prefab 连接；仅增加选择、复位和语义组件。
                if (item.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                    throw new InvalidOperationException("Unexpected scripts on package prefab: " + PrefabNames[type]);
                float size = source.TryGetValue(type, out var sizeReference) ?
                    Largest(BoundsOf(sizeReference.gameObject).size) : 0f;
                NormalizeSize(item, type, size);
                item.transform.SetParent(group.transform, true);
                Place(item, Top(current, region), protectedBounds, occupied, seat, random, false);
                ConfigureInteraction(item);
                string id = $"regional_low_{region + 1}_{index + 1:00}_{type}";
                var semantic = item.AddComponent<SemanticObject>();
                if (source.TryGetValue(type, out var reference))
                {
                    var info = SemanticObjectSummary.From(reference);
                    semantic.ConfigureDetailed(id, info.displayName, info.objectType, info.category, info.function,
                        info.color, info.transparency, info.shape, info.materials,
                        type == "spirit_lamp" ? info.contents : "none", type == "spirit_lamp" ? info.contentColor : "none",
                        type == "spirit_lamp" ? new[] { "unlit" } : new[] { "empty" }, info.aliases, true, true);
                }
                else if (definitions.TryGetValue(type, out var def))
                    semantic.ConfigureDetailed(id, def.displayName, type, def.category, def.function, def.color, def.transparency,
                        def.shape, def.materials, "none", "none", new[] { "empty" }, def.aliases, true, true);
                else throw new InvalidOperationException("Missing semantics: " + type);
                item.name = semantic.DisplayName;
                foreach (var component in item.GetComponentsInChildren<Component>(true))
                    if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                foreach (var child in item.GetComponentsInChildren<Transform>(true)) PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                occupied.Add(BoundsOf(item));
                items.Add(new ExperimentPairedLayoutController.Item(item, Stations[region], true));
            }
        }
        var registry = All<SceneObjectRegistry>(current).Single();
        var settings = new SerializedObject(registry); settings.FindProperty("includeInactiveObjects").boolValue = false;
        settings.ApplyModifiedPropertiesWithoutUndo();
        paired.ConfigureLowProfile(Profile, 20260915, All<PreparationSequenceController>(current).Single(),
            All<ExperimentStateController>(current).Single(), registry, All<ExperimentLogger>(current).Single(), items);
        generator.SetPairedLayout(paired);
        foreach (var pair in source)
        {
            if (JsonUtility.ToJson(SemanticObjectSummary.From(pair.Value)) != semantics[pair.Key] ||
                Vector3.Distance(pair.Value.transform.lossyScale, originalSizes[pair.Key]) > 0.0001f)
                throw new InvalidOperationException("Task semantics or scale changed: " + pair.Key);
            if ((upgradingRegional || pair.Key != "china_dish" && pair.Key != "spirit_lamp" && pair.Key != "lamp_cap" && pair.Key != "filtrate_beaker") &&
                (Vector3.Distance(pair.Value.transform.position, originalPoses[pair.Key].position) > 0.0001f ||
                Quaternion.Angle(pair.Value.transform.rotation, originalPoses[pair.Key].rotation) > 0.001f))
                throw new InvalidOperationException("Unexpected task movement: " + pair.Key);
        }
        Validate(current);
        EditorSceneManager.MarkSceneDirty(current);
        if (!EditorSceneManager.SaveScene(current)) throw new IOException("Cannot save regional scene.");
        AssetDatabase.SaveAssets();
        Render(current);
        Focus(current);
        File.WriteAllText(Output + "/result.txt", "PASS: saved low 1:3 scene. Tasks=13; distractors=39; total=52.\nBackup=" + backup);
    }

    internal static float Largest(Vector3 size) => Mathf.Max(size.x, size.y, size.z);
    internal static void NormalizeSize(GameObject instance, string type, float referenceSize)
    {
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var b = BoundsOf(instance);
        float size = referenceSize > 0f ? referenceSize :
            type == "crucible" ? 0.095f : type == "crucible_tongs" ? 0.19f : 0.15f;
        instance.transform.localScale *= size / Largest(b.size);
        if ((type == "test_tube" || type == "crucible_tongs" || type == "forceps") &&
            b.size.y > Mathf.Max(b.size.x, b.size.z))
            instance.transform.rotation = Quaternion.Euler(90, 0, 0);
    }
    internal static bool Overlap(Bounds a, Bounds b, float gap) =>
        a.min.x < b.max.x + gap && a.max.x > b.min.x - gap && a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;
    // 双密度生成 v2（2026-10-08）：干扰物优先从"抖动网格候选池"均匀取位；
    // 任务物体（task=true）与池耗尽情况保持旧算法，保证操作几何与既有成功布局不变。
    internal static void Place(GameObject item, Bounds table, List<Bounds> tasks, List<Bounds> occupied,
        Transform seat, System.Random rng, bool task)
    {
        if (task)
        {
            PlaceLegacyRandom(item, table, tasks, occupied, seat, rng, task);
            return;
        }

        Quaternion originalRotation = item.transform.rotation;
        if (TryPlaceJittered(item, table, tasks, occupied, seat, rng))
        {
            return;
        }

        item.transform.rotation = originalRotation; // 还原朝向，让兜底算法从初始状态开始
        Debug.LogWarning(
            $"[JitteredDensity] {item.name} 在候选池内未找到合法位置（剩余候选 {ExperimentJitteredDensityLayout.Remaining(table)}），回退旧随机算法。",
            item);
        PlaceLegacyRandom(item, table, tasks, occupied, seat, rng, task);
    }

    /// <summary>抖动网格放置：候选点依次通过"桌内边距 / 操作区禁放 / 任务正面净空 / 旧两级间距 / 座位视线遮挡"全部约束后落位。</summary>
    private static bool TryPlaceJittered(GameObject item, Bounds table, List<Bounds> tasks, List<Bounds> occupied,
        Transform seat, System.Random rng)
    {
        Quaternion baseRotation = item.transform.rotation;
        var eyes = new[] { seat.position + Vector3.up * 1.13f, seat.position + Vector3.up * 1.23f };
        int scanned = 0, edgeRejects = 0, zoneRejects = 0, frontRejects = 0, overlapRejects = 0, sightRejects = 0;
        for (; scanned < ExperimentJitteredDensityLayout.MaxCandidatesPerItem; scanned++)
        {
            if (!ExperimentJitteredDensityLayout.TryGetCandidate(table, rng, out Vector3 candidate, out int candidateIndex))
            {
                break;
            }

            item.transform.rotation = Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0) * baseRotation;
            item.transform.position = Vector3.zero;
            var b = BoundsOf(item);
            item.transform.position = new Vector3(candidate.x - b.center.x, table.max.y + 0.002f - b.min.y, candidate.z - b.center.z);
            b = BoundsOf(item);
            if (b.min.x < table.min.x + 0.039f || b.max.x > table.max.x - 0.039f ||
                b.min.z < table.min.z + 0.039f || b.max.z > table.max.z - 0.039f) { edgeRejects++; continue; }
            if (ExperimentJitteredDensityLayout.HitsZone(table, b)) { zoneRejects++; continue; }
            if (ExperimentJitteredDensityLayout.BreachesTaskFront(b, tasks)) { frontRejects++; continue; }
            if (tasks.Any(t => Overlap(b, t, 0.08f)) || occupied.Any(o => Overlap(b, o, 0.035f))) { overlapRejects++; continue; }
            bool blocksSight = eyes.Any(eye => tasks.Any(t =>
            {
                var d = t.center - eye;
                return b.IntersectRay(new Ray(eye, d.normalized), out float hit) && hit < d.magnitude;
            }));
            if (blocksSight) { sightRejects++; continue; }
            ExperimentJitteredDensityLayout.MarkUsed(table, candidateIndex);
            if (item.TryGetComponent<Rigidbody>(out var gridBody))
            {
                gridBody.position = item.transform.position;
                gridBody.rotation = item.transform.rotation;
            }

            return true;
        }

        Debug.LogWarning(
            $"[JitteredDensity] {item.name} 网格路径无可用位：扫过={scanned} 边距拒={edgeRejects} 禁区拒={zoneRejects} " +
            $"正面拒={frontRejects} 重叠拒={overlapRejects} 视线拒={sightRejects} 池剩余={ExperimentJitteredDensityLayout.Remaining(table)}",
            item);
        return false;
    }

    internal static void PlaceLegacyRandom(GameObject item, Bounds table, List<Bounds> tasks, List<Bounds> occupied,
        Transform seat, System.Random rng, bool task)
    {
        var rotation = item.transform.rotation;
        var eyes = new[] { seat.position + Vector3.up * 1.13f, seat.position + Vector3.up * 1.23f };
        Vector3 best = default; Quaternion bestRotation = default; float bestScore = float.PositiveInfinity;
        var localTasks = tasks.Where(b => Overlap(b, table, 0)).ToArray();
        int edgeRejects = 0, overlapRejects = 0, sightRejects = 0;
        for (int trial = 0; trial < 5000; trial++)
        {
            item.transform.rotation = task ? rotation : Quaternion.Euler(0, (float)rng.NextDouble() * 360, 0) * rotation;
            item.transform.position = Vector3.zero;
            var b = BoundsOf(item);
            float x = Mathf.Lerp(table.min.x + 0.04f + b.extents.x, table.max.x - 0.04f - b.extents.x, (float)rng.NextDouble());
            float z = Mathf.Lerp(table.min.z + 0.04f + b.extents.z, table.max.z - 0.04f - b.extents.z, (float)rng.NextDouble());
            item.transform.position = new Vector3(x - b.center.x, table.max.y + 0.002f - b.min.y, z - b.center.z);
            b = BoundsOf(item);
            if (b.min.x < table.min.x + 0.039f || b.max.x > table.max.x - 0.039f || b.min.z < table.min.z + 0.039f || b.max.z > table.max.z - 0.039f) { edgeRejects++; continue; }
            if (tasks.Any(t => Overlap(b, t, 0.08f)) || occupied.Any(o => Overlap(b, o, 0.035f))) { overlapRejects++; continue; }
            if (eyes.Any(eye => tasks.Any(t => { var d = t.center - eye;
                return b.IntersectRay(new Ray(eye, d.normalized), out float hit) && hit < d.magnitude; }))) { sightRejects++; continue; }
            float reach = Vector2.Distance(new Vector2(x, z), new Vector2(seat.position.x, seat.position.z));
            float nearest = localTasks.Length == 0 ? 0.4f : localTasks.Min(t => Vector2.Distance(new Vector2(x, z), new Vector2(t.center.x, t.center.z)));
            float spread = occupied.Count == 0 ? 0.4f : occupied.Min(o => Vector2.Distance(new Vector2(x, z), new Vector2(o.center.x, o.center.z)));
            float score = task ? reach : Mathf.Abs(nearest - 0.36f) * 0.45f - Mathf.Min(spread, 0.6f) * 0.6f + Mathf.Max(0, reach - 1.4f) * 0.5f;
            score += (float)rng.NextDouble() * 0.03f;
            if (score >= bestScore) continue;
            bestScore = score; best = item.transform.position; bestRotation = item.transform.rotation;
        }
        if (float.IsPositiveInfinity(bestScore)) throw new InvalidOperationException("No safe placement for " + item.name + "; table=" + table + "; placed=" + occupied.Count +
            $"; rejects: edge={edgeRejects} overlap={overlapRejects} sight={sightRejects}; localTasks={localTasks.Length}; itemSize=" + BoundsOf(item).size);
        item.transform.SetPositionAndRotation(best, bestRotation);
        if (item.TryGetComponent<Rigidbody>(out var rb)) { rb.position = best; rb.rotation = bestRotation; }
    }
    internal static void ConfigureInteraction(GameObject item)
    {
        foreach (var child in item.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(child.gameObject, 0);
            child.gameObject.layer = 0;
        }
        foreach (var collider in item.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        var rotation = item.transform.rotation; item.transform.rotation = Quaternion.identity;
        var bounds = BoundsOf(item); var scale = item.transform.lossyScale;
        var box = item.AddComponent<BoxCollider>(); box.center = item.transform.InverseTransformPoint(bounds.center);
        box.size = new Vector3(bounds.size.x / scale.x, bounds.size.y / scale.y, bounds.size.z / scale.z);
        item.transform.rotation = rotation;
        var body = item.AddComponent<Rigidbody>(); body.useGravity = false; body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate; body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        var grab = item.AddComponent<XRGrabInteractable>(); grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false; grab.retainTransformParent = true; grab.useDynamicAttach = true;
        grab.colliders.Clear(); grab.colliders.Add(box); item.AddComponent<ExperimentGrabReturn>();
    }
    [MenuItem("Tools/Stream Manager/Regional Low/Verify And Preview")]
    public static void Verify()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new InvalidOperationException("Open LargeTables first.");
        if (All<ExperimentPairedLayoutController>(scene).Single().ProfileId == RegionRatioHighSceneInstaller.Profile)
        { RegionRatioHighSceneInstaller.Verify(); return; }
        Validate(scene); Render(scene); Focus(scene); File.WriteAllText(Output + "/result.txt", "PASS: regional low verified and rendered.");
    }
    private static void Validate(Scene scene)
    {
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        var registry = All<SceneObjectRegistry>(scene).Single(); registry.Refresh();
        var active = All<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy).ToArray();
        var report = new List<string>();
        Action<bool, string> check = (ok, message) => { if (!ok) throw new InvalidOperationException(message); report.Add("PASS: " + message); };
        check(active.Length == 52 && registry.Count == 52 && active.Select(s => s.StableId).Distinct().Count() == 52, "52 enabled unique catalog objects");
        report.Add("ENGINE: jittered-grid v2; pools=" + ExperimentJitteredDensityLayout.DescribePools());
        check(paired.ProfileId == Profile && paired.Items.Count == 39 && paired.Items.All(i => i.shared), "Low profile has 39 shared extras");
        check(paired.TryApply(ExperimentLayoutSelection.Low, "validation", null, out _), "Low profile can be selected");
        check(!paired.TryApply(ExperimentLayoutSelection.High, "validation", null, out _) && paired.ActiveCount == 39, "Unconfigured high is rejected without mutation");
        var csv = new List<string> { "id,region,role,type,sourcePrefab,x,y,z,width,height,depth" };
        for (int region = 0; region < 3; region++)
        {
            var top = Top(scene, region);
            check(paired.Items.Count(i => i.station == Stations[region]) == Tasks[region].Length * 3, Stations[region] + " ratio 1:3");
            report.Add($"REGION {Stations[region]} area={top.size.x * top.size.z:F4} tasks={Tasks[region].Length} extras={Types[region].Length}");
            var list = active.Where(s => Tasks[region].Contains(s.StableId) || paired.Items.Any(i => i.station == Stations[region] && i.gameObject == s.gameObject)).ToArray();
            foreach (var s in list)
            {
                var b = BoundsOf(s.gameObject); bool isTask = Tasks[region].Contains(s.StableId);
                check(b.min.x >= top.min.x - 0.001f && b.max.x <= top.max.x + 0.001f && b.min.z >= top.min.z - 0.001f && b.max.z <= top.max.z + 0.001f, "On assigned table: " + s.StableId);
                string prefabPath = isTask ? "" : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.gameObject);
                if (!isTask)
                {
                    check(s.Selectable && s.Grabbable && s.GetComponent<XRGrabInteractable>() != null && s.GetComponent<ExperimentGrabReturn>() != null && s.GetComponent<Collider>() != null, "Selectable grab/return: " + s.StableId);
                    check(prefabPath == PrefabFolder + PrefabNames[s.ObjectType], "Package prefab source: " + s.StableId);
                    check(s.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null)
                        .All(m => AssetDatabase.GetAssetPath(m.sharedMesh).StartsWith("Assets/3D Laboratory Environment with Appratus/", StringComparison.Ordinal)),
                        "Package meshes only: " + s.StableId);
                }
                csv.Add(string.Join(",", new[] { s.StableId, Stations[region], isTask ? "task" : "distractor", s.ObjectType, prefabPath }.Concat(
                    new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z }.Select(v => v.ToString("F5", CultureInfo.InvariantCulture)))));
            }
        }
        var tasks = active.Where(s => Tasks.SelectMany(t => t).Contains(s.StableId)).ToArray();
        for (int i = 0; i < paired.Items.Count; i++)
        {
            var b = BoundsOf(paired.Items[i].gameObject);
            check(!tasks.Any(t => Overlap(b, BoundsOf(t.gameObject), 0.079f)), "Task clearance: " + paired.Items[i].gameObject.GetComponent<SemanticObject>().StableId);
            for (int j = 0; j < i; j++) check(!Overlap(b, BoundsOf(paired.Items[j].gameObject), 0.034f), "Extra clearance " + i + "/" + j);
        }
        check(All<PreparationSequenceController>(scene).Single().Steps.Count == 7, "Seven task stages retained");
        PicoControllerRayInstaller.Validate(scene); report.Add("PASS: left/right controller rays retained");
        Directory.CreateDirectory(Output); File.WriteAllLines(Output + "/verification.txt", report); File.WriteAllLines(Output + "/inventory.csv", csv);
    }
    internal static void Render(Scene scene, string output = Output)
    {
        Directory.CreateDirectory(output);
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
        foreach (var view in new[] { ("front", 0f), ("left", -70f), ("right", 70f) })
            CopperSulfateLiquidVerification.Render(scene, output + "/" + view.Item1 + ".png",
                seat.position + Vector3.up * 1.13f, seat.rotation * Quaternion.Euler(30f, view.Item2, 0), false);
        for (int i = 0; i < 3; i++)
        {
            var top = Top(scene, i);
            CopperSulfateLiquidVerification.Render(scene, output + "/region-" + (i + 1) + ".png",
                top.center + Vector3.up * 1.9f, Quaternion.Euler(90, 0, 0), false);
        }
    }
    internal static void Focus(Scene scene)
    {
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
        var view = SceneView.lastActiveSceneView;
        if (view != null) { view.LookAt(seat.position + seat.forward * 0.4f + Vector3.up * 0.75f, seat.rotation * Quaternion.Euler(55, 0, 0), 2.8f); view.Repaint(); }
        Selection.activeGameObject = scene.GetRootGameObjects().Single(r => r.name == RootName);
    }
}
