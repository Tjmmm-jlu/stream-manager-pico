using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 运行时布局库构建工具（2026-10-09）。
/// 为"每场实验随机一套新布局"生成种子库：离线求解 + 验证 + 导出 JSON 规格，
/// 设备端由 ExperimentRuntimeLayoutLoader 装载（新增布局无需重打 APK）。
///
/// 请求文件：Temp/RuntimeLayout.request（内容即命令，处理完成后自动删除）
///   命令：
///     setup                             —— 装配场景中的 Runtime Layout Loader（引用/预制体池/台面范围）并保存场景
///     library &lt;start&gt; &lt;count&gt; &lt;baseSeed&gt; —— 从 start 号开始生成 count 套布局规格，导出到 viewer 与项目副本
/// 输出：
///   项目副本  Temp/RuntimeLayoutLibrary/layout_XXX.json + index.json + verification.txt
///   网页目录  D:\webrtcs\WebApp\client\public\layouts\（由网页服务器静态提供 /layouts/*）
/// </summary>
[InitializeOnLoad]
public static class RuntimeLayoutLibraryBuilder
{
    public const string ScenePath = CopperSulfateLargeTableSceneBuilder.ScenePath;
    public const string Output = "Temp/RuntimeLayoutLibrary";
    public const string ViewerLayoutFolder = @"D:\webrtcs\WebApp\client\public\layouts";
    private const string RequestPath = "Temp/RuntimeLayout.request";
    private const string LoaderObjectName = "Runtime Layout Loader";
    private const int BaseSeed = 20260915;
    private const int MaxAttempts = 24;
    private static bool _busy;
    private const string PlaytestMarker = "Temp/RuntimeLayoutPlaytest.txt";
    private const string PlaytestResultPath = "Temp/RuntimeLayoutPlaytestResult.txt";

    static RuntimeLayoutLibraryBuilder()
    {
        EditorApplication.update += ProcessRequest;
        EditorApplication.update += ProcessPlaytest;
    }

    private static void ProcessRequest()
    {
        if (_busy) return;
        if (File.Exists(RequestPath))
        {
            string probe = null;
            try { probe = File.ReadAllText(RequestPath).Trim(); } catch (Exception) { }
            if (probe == "stopplay")
            {
                File.Delete(RequestPath);
                EditorApplication.isPlaying = false;
                return;
            }
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(RequestPath)) return;
        _busy = true;
        Directory.CreateDirectory(Output);
        try
        {
            string command = File.ReadAllText(RequestPath).Trim();
            File.Delete(RequestPath);
            string[] parts = command.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            switch (parts.Length > 0 ? parts[0] : string.Empty)
            {
                case "setup":
                    Setup();
                    break;
                case "library":
                    Library(int.Parse(parts[1]), int.Parse(parts[2]), int.Parse(parts[3]));
                    break;
                case "playtest":
                    Playtest(int.Parse(parts[1]));
                    break;
                case "audit":
                    Audit(int.Parse(parts[1]), int.Parse(parts[2]));
                    break;
                default:
                    throw new InvalidOperationException("Unknown runtime layout command: " + command);
            }
        }
        catch (Exception exception)
        {
            File.WriteAllText(Output + "/result.txt", "FAIL: " + exception);
            Debug.LogException(exception);
        }
        finally { _busy = false; }
    }

    /// <summary>一次性装配：向场景加入/更新 "Runtime Layout Loader" 对象并保存。</summary>
    public static void Setup()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var active = SceneManager.GetActiveScene();
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var paired = All<ExperimentPairedLayoutController>(scene).Single();

        string backup = "MigrationBackups/BeforeRuntimeLoaderSetup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(scene.path, backup + "/SavedScene.unity");
        if (scene.isDirty) Require(EditorSceneManager.SaveScene(scene, backup + "/UnsavedScene.unity", true), "Cannot preserve unsaved scene.");

        GameObject loaderObject = scene.GetRootGameObjects().FirstOrDefault(r => r.name == LoaderObjectName);
        if (loaderObject == null)
        {
            loaderObject = new GameObject(LoaderObjectName);
            SceneManager.MoveGameObjectToScene(loaderObject, scene);
        }
        var loader = loaderObject.GetComponent<ExperimentRuntimeLayoutLoader>() ??
            loaderObject.AddComponent<ExperimentRuntimeLayoutLoader>();

        var taskIds = new HashSet<string>(RegionRatioLowSceneInstaller.Tasks.SelectMany(t => t));
        var tasks = All<SemanticObject>(scene).Where(s => taskIds.Contains(s.StableId))
            .OrderBy(s => s.StableId, StringComparer.Ordinal).ToList();
        Require(tasks.Count == 13, "Expected 13 task objects.");

        var stations = new List<string>(RegionRatioLowSceneInstaller.Stations);
        var centers = new List<Vector3>();
        var sizes = new List<Vector3>();
        for (int region = 0; region < stations.Count; region++)
        {
            Bounds top = RegionRatioLowSceneInstaller.Top(scene, region);
            centers.Add(top.center);
            sizes.Add(top.size);
        }

        var references = All<SemanticObject>(scene)
            .Where(s => s.StableId.StartsWith("regional_", StringComparison.Ordinal))
            .GroupBy(s => s.ObjectType)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var pool = new List<RuntimeLayoutPrefabEntry>();
        foreach (string type in RegionRatioLowSceneInstaller.Types.SelectMany(t => t).Distinct())
        {
            Require(RegionRatioLowSceneInstaller.PrefabNames.TryGetValue(type, out string prefabName),
                "Missing prefab mapping: " + type);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                RegionRatioLowSceneInstaller.PrefabFolder + prefabName);
            Require(prefab != null, "Missing prefab asset: " + prefabName);
            var entry = new RuntimeLayoutPrefabEntry { type = type, prefab = prefab };
            if (references.TryGetValue(type, out SemanticObject reference))
            {
                entry.semantic = SemanticObjectSummary.From(reference);
                entry.semantic.id = string.Empty;
                entry.semantic.selectable = true;
                entry.semantic.grabbable = true;
            }
            else
            {
                entry.semantic = new SemanticObjectSummary
                {
                    displayName = prefab.name,
                    objectType = type,
                    selectable = true,
                    grabbable = true
                };
            }
            pool.Add(entry);
        }

        loader.ConfigureSceneReferences(paired, All<SceneObjectRegistry>(scene).Single(),
            All<ExperimentLogger>(scene).Single(), tasks, stations, centers, sizes);
        loader.ConfigurePrefabPool(pool);
        EditorUtility.SetDirty(loader);
        EditorSceneManager.MarkSceneDirty(scene);
        Require(EditorSceneManager.SaveScene(scene), "Failed to save scene after loader setup.");
        AssetDatabase.SaveAssets();
        File.WriteAllText(Output + "/setup.txt",
            "PASS: runtime layout loader configured. pool=" + pool.Count + "; tasks=" + tasks.Count +
            "\nBackup=" + backup);
    }

    /// <summary>批量生成布局规格（不保存场景；求解结果只写入 JSON 规格库）。</summary>
    public static void Library(int startIndex, int count, int baseSeed)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var active = SceneManager.GetActiveScene();
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        Require(paired.ProfileId == RegionRatioHighSceneInstaller.Profile,
            "Library generation requires the paired profile scene.");

        var taskIds = new HashSet<string>(RegionRatioLowSceneInstaller.Tasks.SelectMany(t => t));
        var taskObjects = All<SemanticObject>(scene).Where(s => taskIds.Contains(s.StableId)).ToArray();
        Require(taskObjects.Length == 13, "Expected 13 task objects.");
        var source = taskObjects.ToDictionary(s => s.StableId);
        var definitions = All<ExperimentDistractorLayoutGenerator>(scene).Single().Definitions
            .ToDictionary(d => d.objectType);

        var tasks = taskObjects.Select(s => RegionRatioLowSceneInstaller.BoundsOf(s.gameObject)).ToList();
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
        var originals = All<Transform>(scene).ToDictionary(t => t,
            t => (parent: t.parent, position: t.localPosition, rotation: t.localRotation, scale: t.localScale));

        Directory.CreateDirectory(Output);
        Directory.CreateDirectory(ViewerLayoutFolder);
        var manifest = LoadManifest();
        var report = new List<string>();
        var root = new GameObject("Library Build Temp");
        SceneManager.MoveGameObjectToScene(root, scene);
        try
        {
            for (int index = startIndex; index < startIndex + count; index++)
            {
                int seed = unchecked(baseSeed + index * 7919);
                if (TryBuildVariant(scene, root, seed, source, definitions, tasks, seat,
                    out RuntimeLayoutSpec spec, out string buildError))
                {
                    spec.index = index;
                    WriteSpec(spec, index);
                    UpsertManifest(manifest, spec, index);
                    report.Add($"PASS index={index} seed={seed} items={spec.itemCount} shared={spec.sharedCount}");
                    File.WriteAllText(Output + "/progress.txt",
                        $"done {index - startIndex + 1}/{count}; last index={index}; seed={seed}");
                }
                else
                {
                    report.Add($"SKIP index={index} seed={seed}: {buildError}");
                    File.WriteAllText(Output + "/progress.txt",
                        $"FAILED at index={index}; seed={seed}; {buildError}");
                }
            }

            foreach (var entry in originals)
                Require(entry.Key != null && entry.Key.parent == entry.Value.parent &&
                    Vector3.Distance(entry.Key.localPosition, entry.Value.position) < 0.00001f &&
                    Quaternion.Angle(entry.Key.localRotation, entry.Value.rotation) < 0.001f &&
                    Vector3.Distance(entry.Key.localScale, entry.Value.scale) < 0.00001f,
                    "Existing transform changed: " + entry.Key.name);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }

        WriteManifest(manifest);
        File.WriteAllLines(Output + "/verification.txt", report);
        File.WriteAllText(Output + "/result.txt",
            $"PASS: library batch {startIndex}..{startIndex + count - 1} done.\n" +
            string.Join("\n", report.Select(r => r.Split(' ')[0]).GroupBy(v => v).Select(g => $"{g.Key}={g.Count()}")));
    }

    /// <summary>单套变体求解（与 InstallPairJoint 同一策略矩阵）；失败返回 false 并输出最后一次尝试原因。</summary>
    private static bool TryBuildVariant(Scene scene, GameObject root, int seed,
        Dictionary<string, SemanticObject> source,
        Dictionary<string, ExperimentDistractorDefinition> definitions,
        List<Bounds> tasks, Transform seat,
        out RuntimeLayoutSpec spec, out string error)
    {
        spec = null;
        error = null;
        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            int attemptSeed = unchecked(seed + attempt * 1000003);
            var random = new System.Random(attemptSeed);
            ExperimentJitteredDensityLayout.ResetAll(attemptSeed);
            for (int child = root.transform.childCount - 1; child >= 0; child--)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(child).gameObject);
            var occupied = new List<Bounds>();
            var built = new List<(GameObject go, string type, bool shared, string station, string id)>();
            try
            {
                for (int region = 0; region < RegionRatioLowSceneInstaller.Stations.Length; region++)
                {
                    var table = RegionRatioLowSceneInstaller.Top(scene, region);
                    string station = RegionRatioLowSceneInstaller.Stations[region];
                    var lowGroup = new GameObject(station + " Extras");
                    lowGroup.transform.SetParent(root.transform, false);
                    var highGroup = new GameObject(station + " High Extras");
                    highGroup.transform.SetParent(root.transform, false);
                    int[] sizeOrder = Enumerable.Range(0, RegionRatioLowSceneInstaller.Types[region].Length)
                        .OrderByDescending(ix => RegionRatioHighSceneInstaller.ExpectedSize(
                            source, RegionRatioLowSceneInstaller.Types[region][ix]))
                        .ToArray();
                    bool forceLegacy = attempt > 8;
                    bool twoPhase = attempt > 12;

                    void PlaceOne(bool sharedRole, int index, Transform group)
                    {
                        string type = RegionRatioLowSceneInstaller.Types[region][index];
                        var item = RegionRatioHighSceneInstaller.CreatePackageItem(scene, type, group, source);
                        RegionRatioHighSceneInstaller.PlaceJointItem(item, table, tasks, occupied, seat, random, forceLegacy);
                        RegionRatioLowSceneInstaller.ConfigureInteraction(item);
                        string id = $"regional_{(sharedRole ? "low" : "high")}_{region + 1}_{index + 1:00}_{type}";
                        RegionRatioHighSceneInstaller.ConfigureSemantics(item, id, type, source, definitions);
                        built.Add((item, type, sharedRole, station, id));
                        occupied.Add(RegionRatioLowSceneInstaller.BoundsOf(item));
                    }

                    if (!twoPhase)
                    {
                        foreach (int index in sizeOrder) { PlaceOne(true, index, lowGroup.transform); PlaceOne(false, index, highGroup.transform); }
                    }
                    else
                    {
                        foreach (int index in sizeOrder) PlaceOne(true, index, lowGroup.transform);
                        foreach (int index in sizeOrder) PlaceOne(false, index, highGroup.transform);
                    }
                }
            }
            catch (InvalidOperationException exception)
                when (exception.Message.StartsWith("No safe placement", StringComparison.Ordinal))
            {
                error = $"attempt {attempt}: {exception.Message}";
                continue;
            }

            spec = new RuntimeLayoutSpec
            {
                profile = "runtime-joint-v1",
                seed = seed,
                itemCount = built.Count,
                sharedCount = built.Count(record => record.shared)
            };
            foreach (var record in built)
            {
                var box = record.go.GetComponent<BoxCollider>();
                spec.items.Add(new RuntimeLayoutItemSpec
                {
                    id = record.id,
                    type = record.type,
                    shared = record.shared,
                    station = record.station,
                    position = record.go.transform.position,
                    eulerAngles = record.go.transform.eulerAngles,
                    scale = record.go.transform.lossyScale,
                    colliderCenter = box != null ? box.center : Vector3.zero,
                    colliderSize = box != null ? box.size : Vector3.one
                });
            }
            return true;
        }
        return false;
    }

    [Serializable]
    private sealed class LibraryManifestEntry
    {
        public string file;
        public int index;
        public int seed;
        public int items;
        public int shared;
    }

    [Serializable]
    private sealed class LibraryManifest
    {
        public string profile = "runtime-joint-v1";
        public string generatedUtc;
        public List<LibraryManifestEntry> layouts = new List<LibraryManifestEntry>();
    }

    private static LibraryManifest LoadManifest()
    {
        string path = Path.Combine(ViewerLayoutFolder, "index.json");
        if (File.Exists(path))
        {
            try
            {
                var manifest = JsonUtility.FromJson<LibraryManifest>(File.ReadAllText(path));
                if (manifest != null) return manifest;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[RuntimeLibrary] 旧 index.json 解析失败，将重建：" + exception.Message);
            }
        }
        return new LibraryManifest();
    }

    private static void UpsertManifest(LibraryManifest manifest, RuntimeLayoutSpec spec, int index)
    {
        manifest.layouts.RemoveAll(entry => entry.index == index);
        manifest.layouts.Add(new LibraryManifestEntry
        {
            file = $"layout_{index:000}.json",
            index = index,
            seed = spec.seed,
            items = spec.itemCount,
            shared = spec.sharedCount
        });
        manifest.layouts.Sort((a, b) => a.index.CompareTo(b.index));
    }

    private static void WriteManifest(LibraryManifest manifest)
    {
        manifest.generatedUtc = DateTime.UtcNow.ToString("o");
        string json = JsonUtility.ToJson(manifest, true);
        File.WriteAllText(Path.Combine(ViewerLayoutFolder, "index.json"), json);
        File.WriteAllText(Path.Combine(Output, "index.json"), json);
    }

    private static void WriteSpec(RuntimeLayoutSpec spec, int index)
    {
        string json = JsonUtility.ToJson(spec, true);
        File.WriteAllText(Path.Combine(Output, $"layout_{index:000}.json"), json);
        File.WriteAllText(Path.Combine(ViewerLayoutFolder, $"layout_{index:000}.json"), json);
    }

    /// <summary>冒烟测试：写入标记后进入 Play 模式，自动装载指定规格并验证低/高切换与注册表计数。</summary>
    public static void Playtest(int index)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        string specPath = Path.Combine(Output, $"layout_{index:000}.json");
        Require(File.Exists(specPath), "Spec file not found: " + specPath);
        File.WriteAllText(PlaytestMarker, "index=" + index + "\nspec=" + specPath);
        if (File.Exists(PlaytestResultPath)) File.Delete(PlaytestResultPath);
        EditorApplication.isPlaying = true;
    }

    private static void ProcessPlaytest()
    {
        if (!Application.isPlaying) return;
        if (!File.Exists(PlaytestMarker)) return;
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (Time.frameCount < 30) return;
        string specPath = null;
        try
        {
            specPath = File.ReadAllLines(PlaytestMarker)
                .FirstOrDefault(line => line.StartsWith("spec=", StringComparison.Ordinal))?.Substring(5);
        }
        catch (Exception) { }
        bool loaderFound = false;
        try
        {
            var loader = UnityEngine.Object.FindObjectOfType<ExperimentRuntimeLayoutLoader>();
            if (loader == null)
            {
                File.WriteAllText(PlaytestResultPath, "FAIL: ExperimentRuntimeLayoutLoader not found in scene.");
                return;
            }
            loaderFound = true;
            if (specPath == null || !File.Exists(specPath))
            {
                File.WriteAllText(PlaytestResultPath, "FAIL: spec file missing: " + specPath);
                return;
            }
            bool ok = loader.TryApplySpec(File.ReadAllText(specPath), -1, 0, out string error);
            var registry = SceneObjectRegistry.Instance;
            var paired = UnityEngine.Object.FindObjectOfType<ExperimentPairedLayoutController>();
            Func<int> activeCount = () => paired.Items.Count(i => i.gameObject != null && i.gameObject.activeInHierarchy);
            bool highOk = paired.TryApply(ExperimentLayoutSelection.High, "playtest", null, out string highError);
            registry?.Refresh();
            int registryHigh = registry?.Count ?? -1;
            int activeHigh = activeCount();
            bool lowOk = paired.TryApply(ExperimentLayoutSelection.Low, "playtest", null, out string lowError);
            registry?.Refresh();
            int registryLow = registry?.Count ?? -1;
            int activeLow = activeCount();
            File.WriteAllText(PlaytestResultPath,
                (ok ? "PASS" : "FAIL: " + error) +
                $"\napplyOk={ok}; hasRuntime={loader.HasRuntimeLayout}; activeIndex={loader.ActiveIndex}; items={paired.Items.Count}" +
                $"\nhighOk={highOk}; highRegistry={registryHigh} (expect 91); highActiveItems={activeHigh} (expect 78); highError={highError}" +
                $"\nlowOk={lowOk}; lowRegistry={registryLow} (expect 52); lowActiveItems={activeLow} (expect 39); lowError={lowError}");
        }
        catch (Exception exception)
        {
            File.WriteAllText(PlaytestResultPath, "EXCEPTION: " + exception);
        }
        finally
        {
            try { File.Delete(PlaytestMarker); } catch (Exception) { }
            if (loaderFound) Debug.Log("[RuntimeLibrary] playtest finished; see Temp/RuntimeLayoutPlaytestResult.txt");
        }
    }

    /// <summary>审计：按装载器同口径（MeshFilter 网格 8 角点世界 AABB）逐份校验布局规格的净空与台面范围。</summary>
    public static void Audit(int startIndex, int count)
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var active = SceneManager.GetActiveScene();
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var taskIds = new HashSet<string>(RegionRatioLowSceneInstaller.Tasks.SelectMany(t => t));
        var taskBounds = All<SemanticObject>(scene).Where(s => taskIds.Contains(s.StableId))
            .Select(s => MeshLikeBounds(s.gameObject)).ToArray();
        var stations = RegionRatioLowSceneInstaller.Stations;
        var tables = Enumerable.Range(0, stations.Length)
            .Select(i => RegionRatioLowSceneInstaller.Top(scene, i)).ToArray();
        var prefabs = new Dictionary<string, GameObject>(StringComparer.Ordinal);
        foreach (string type in RegionRatioLowSceneInstaller.Types.SelectMany(t => t).Distinct())
            prefabs[type] = AssetDatabase.LoadAssetAtPath<GameObject>(
                RegionRatioLowSceneInstaller.PrefabFolder + RegionRatioLowSceneInstaller.PrefabNames[type]);

        var root = new GameObject("Audit Build Temp");
        SceneManager.MoveGameObjectToScene(root, scene);
        var report = new List<string>();
        try
        {
            for (int index = startIndex; index < startIndex + count; index++)
            {
                string path = Path.Combine(Output, $"layout_{index:000}.json");
                if (!File.Exists(path)) { report.Add($"MISSING index={index}"); continue; }
                var spec = JsonUtility.FromJson<RuntimeLayoutSpec>(File.ReadAllText(path));
                for (int child = root.transform.childCount - 1; child >= 0; child--)
                    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(child).gameObject);
                try
                {
                    var objects = new List<(GameObject go, int station)>();
                    foreach (RuntimeLayoutItemSpec item in spec.items)
                    {
                        Require(prefabs.TryGetValue(item.type, out GameObject prefab) && prefab != null,
                            "Missing prefab: " + item.type);
                        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                        go.transform.SetParent(root.transform, true);
                        go.transform.localScale = item.scale;
                        go.transform.SetPositionAndRotation(item.position, Quaternion.Euler(item.eulerAngles));
                        objects.Add((go, Array.IndexOf(stations, item.station)));
                    }
                    var bounds = objects.Select(o => MeshLikeBounds(o.go)).ToList();
                    float minTask = float.PositiveInfinity;
                    float minItem = float.PositiveInfinity;
                    string problem = null;
                    for (int i = 0; i < bounds.Count && problem == null; i++)
                    {
                        for (int t = 0; t < taskBounds.Length; t++)
                        {
                            minTask = Mathf.Min(minTask, XZGap(bounds[i], taskBounds[t]));
                            if (OverlapXZ(bounds[i], taskBounds[t], 0.079f)) { problem = $"task clearance @item {i + 1}"; break; }
                        }
                        for (int j = 0; problem == null && j < i; j++)
                        {
                            minItem = Mathf.Min(minItem, XZGap(bounds[i], bounds[j]));
                            if (OverlapXZ(bounds[i], bounds[j], 0.034f)) problem = $"item clearance @{i + 1} vs {j + 1}";
                        }
                        if (problem == null && objects[i].station >= 0)
                        {
                            Bounds area = tables[objects[i].station];
                            if (bounds[i].min.x < area.min.x - 0.001f || bounds[i].max.x > area.max.x + 0.001f ||
                                bounds[i].min.z < area.min.z - 0.001f || bounds[i].max.z > area.max.z + 0.001f)
                                problem = $"table overflow @item {i + 1}";
                        }
                    }
                    report.Add(problem == null
                        ? $"PASS index={index} minTaskGap={minTask:F4} minItemGap={minItem:F4}"
                        : $"FAIL index={index} {problem}; minTaskGap={minTask:F4} minItemGap={minItem:F4}");
                }
                catch (Exception exception) { report.Add($"ERROR index={index}: {exception.Message}"); }
            }
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
        File.WriteAllLines(Output + "/audit.txt", report);
        File.WriteAllText(Output + "/result.txt",
            $"PASS: audit {startIndex}..{startIndex + count - 1}\n" +
            string.Join("\n", report.Select(r => r.Split(' ')[0]).GroupBy(v => v)
                .Select(g => $"{g.Key}={g.Count()}")));
    }

    private static Bounds MeshLikeBounds(GameObject item)
    {
        Bounds result = default;
        bool initialized = false;
        foreach (MeshFilter mesh in item.GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer renderer = mesh.GetComponent<MeshRenderer>();
            if (mesh.sharedMesh == null || renderer == null || !renderer.enabled || !mesh.gameObject.activeInHierarchy) continue;
            Bounds local = mesh.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = mesh.transform.TransformPoint(corner);
                if (!initialized) { result = new Bounds(point, Vector3.zero); initialized = true; }
                else result.Encapsulate(point);
            }
        }
        if (!initialized) return new Bounds(item.transform.position, Vector3.zero);
        return result;
    }

    private static bool OverlapXZ(Bounds a, Bounds b, float gap) =>
        a.min.x < b.max.x + gap && a.max.x > b.min.x - gap &&
        a.min.z < b.max.z + gap && a.max.z > b.min.z - gap;

    private static float XZGap(Bounds a, Bounds b)
    {
        float dx = Mathf.Max(0f, Mathf.Max(b.min.x - a.max.x, a.min.x - b.max.x));
        float dz = Mathf.Max(0f, Mathf.Max(b.min.z - a.max.z, a.min.z - b.max.z));
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static T[] All<T>(Scene scene) where T : Component =>
        scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
}
