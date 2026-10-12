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

// Extends the saved low layout without moving its task objects or distractors.
// 2026-10-08 v2：新增的 39 件同样走抖动网格引擎（同一设种），并在 target-neighborhoods.csv 中追加同类密度与遮挡计数。
[InitializeOnLoad]
public static class RegionRatioHighSceneInstaller
{
    public const string Profile = "regional-paired-prefabs-1to3-1to6-v3";
    public const string Output = "Temp/RegionRatioHigh";
    private const string RootName = "High Density Additions";
    private const string Package = "Assets/3D Laboratory Environment with Appratus/";
    private const int Seed = 20260915;
    private static bool _busy;
    private static string[] Stations => RegionRatioLowSceneInstaller.Stations;
    private static string[][] Tasks => RegionRatioLowSceneInstaller.Tasks;
    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
    private static Bounds BoundsOf(GameObject item) => RegionRatioLowSceneInstaller.BoundsOf(item);
    private static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }

    static RegionRatioHighSceneInstaller() => EditorApplication.update += ProcessRequest;
    private static void ProcessRequest()
    {
        const string request = "Temp/RegionRatioHigh.request";
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        _busy = true;
        Directory.CreateDirectory(Output);
        try
        {
            string command = File.ReadAllText(request).Trim(); File.Delete(request);
            if (command == "install") Install();
            else if (command == "verify") Verify();
            else if (command == "rebuild") InstallPairJoint();
            else if (command == "runtime") RegionRatioLowRuntimeVerification.Run();
            else if (command == "low") Select(ExperimentLayoutSelection.Low);
            else if (command == "high") Select(ExperimentLayoutSelection.High);
            else if (command == "recover-low")
            {
                Require(File.ReadAllText(RegionRatioLowSceneInstaller.ScenePath).Contains(RegionRatioLowSceneInstaller.Profile),
                    "Recovery requires the saved low scene.");
                var active = SceneManager.GetActiveScene();
                if (active.isDirty)
                {
                    string backup = "MigrationBackups/RegionalHighRecovery_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                    Directory.CreateDirectory(backup);
                    Require(EditorSceneManager.SaveScene(active, backup + "/UnsavedScene.unity", true), "Cannot preserve unsaved state.");
                }
                EditorSceneManager.OpenScene(RegionRatioLowSceneInstaller.ScenePath, OpenSceneMode.Single);
                Install();
            }
            else throw new InvalidOperationException("Unknown regional command: " + command);
        }
        catch (Exception ex) { File.WriteAllText(Output + "/result.txt", "FAIL: " + ex); Debug.LogException(ex); }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Regional Density/Install High (91 Objects)")]
    public static void Install()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == RegionRatioLowSceneInstaller.ScenePath, "Open the LargeTables scene first.");
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        Require(paired.ProfileId == RegionRatioLowSceneInstaller.Profile, "Install requires the existing 52-object low profile.");
        Require(paired.Items.Count == 39 && paired.Items.All(i => i.shared && i.gameObject != null && i.gameObject.activeInHierarchy),
            "Expected 39 active low distractors.");
        Require(!paired.IsLocked && !All<XRGrabInteractable>(scene).Any(g => g.isSelected), "Finish/reset the task and release objects first.");
        Directory.CreateDirectory(Output);
        string backup = "MigrationBackups/BeforeRegionalHighScene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(scene.path, backup + "/SavedScene.unity");
        if (scene.isDirty) Require(EditorSceneManager.SaveScene(scene, backup + "/UnsavedScene.unity", true), "Cannot preserve unsaved scene.");
        File.WriteAllText(Output + "/backup.txt", Path.GetFullPath(backup));

        var originals = All<Transform>(scene).ToDictionary(t => t,
            t => (parent: t.parent, position: t.localPosition, rotation: t.localRotation, scale: t.localScale));
        var taskIds = new HashSet<string>(Tasks.SelectMany(t => t));
        var taskObjects = All<SemanticObject>(scene).Where(s => taskIds.Contains(s.StableId)).ToArray();
        Require(taskObjects.Length == 13, "Expected 13 task objects.");
        var taskSemantics = taskObjects.ToDictionary(s => s, s => JsonUtility.ToJson(SemanticObjectSummary.From(s)));
        var lowItems = paired.Items.Select(i => new ExperimentPairedLayoutController.Item(i.gameObject, i.station, true)).ToList();
        var items = new List<ExperimentPairedLayoutController.Item>(lowItems);
        var tasks = taskObjects.Select(s => BoundsOf(s.gameObject)).ToList();
        var occupied = lowItems.Select(i => BoundsOf(i.gameObject)).ToList();
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
        var parent = scene.GetRootGameObjects().Single(r => r.name == "Regional Low Apparatus");
        Require(parent.transform.Find(RootName) == null, "High additions already exist.");
        string originalProfile = paired.ProfileId;
        int originalSeed = paired.LayoutSeed;
        var root = new GameObject(RootName); root.transform.SetParent(parent.transform, false);
        bool saved = false;
        try
        {
            // 多次重试（2026-10-08）：单条随机流可能在某张桌子上陷入"无解"；每次更换设种重新整组放置。
            // 第 1 次沿用原设置（attemptSeed=Seed），成功后的布局仍然确定可复现。
            const int MaxAttempts = 20;
            for (int attempt = 1; ; attempt++)
            {
                int attemptSeed = unchecked(Seed + attempt * 1000003);
                var random = new System.Random(attemptSeed);
                ExperimentJitteredDensityLayout.ResetAll(attemptSeed);
                items.Clear();
                items.AddRange(lowItems);
                occupied.Clear();
                occupied.AddRange(lowItems.Select(i => BoundsOf(i.gameObject)));
                try
                {
            for (int region = 0; region < Stations.Length; region++)
            {
                var group = new GameObject(Stations[region] + " High Extras"); group.transform.SetParent(root.transform, false);
                var counterparts = lowItems.Where(i => i.station == Stations[region]).ToArray();
                Require(counterparts.Length == Tasks[region].Length * 3, "Low region ratio mismatch.");
                // Larger objects go first; the low layout stays fixed throughout placement.
                foreach (int index in Enumerable.Range(0, counterparts.Length).OrderByDescending(i =>
                    { var b = BoundsOf(counterparts[i].gameObject); return b.size.x * b.size.z; }))
                {
                    var source = counterparts[index].gameObject;
                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
                    Require(path.StartsWith(Package + "Prefabs/", StringComparison.Ordinal), "Low distractor is not a package prefab.");
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
                    instance.transform.SetParent(group.transform, false);
                    instance.transform.localScale = source.transform.lossyScale;
                    instance.transform.rotation = source.transform.rotation;
                    RegionRatioLowSceneInstaller.Place(instance, RegionRatioLowSceneInstaller.Top(scene, region), tasks, occupied, seat, random, false);
                    RegionRatioLowSceneInstaller.ConfigureInteraction(instance);
                    var info = SemanticObjectSummary.From(source.GetComponent<SemanticObject>());
                    string id = $"regional_high_{region + 1}_{index + 1:00}_{info.objectType}";
                    instance.AddComponent<SemanticObject>().ConfigureDetailed(id, info.displayName, info.objectType, info.category,
                        info.function, info.color, info.transparency, info.shape, info.materials, info.contents, info.contentColor,
                        info.stateTags, info.aliases, true, true);
                    instance.name = info.displayName;
                    foreach (var component in instance.GetComponentsInChildren<Component>(true))
                        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    foreach (var child in instance.GetComponentsInChildren<Transform>(true))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
                    items.Add(new ExperimentPairedLayoutController.Item(instance, Stations[region], false));
                    occupied.Add(BoundsOf(instance));
                }
            }
                }
                catch (InvalidOperationException ex) when (attempt < MaxAttempts &&
                    ex.Message.StartsWith("No safe placement", StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[RegionRatioHigh] 第 {attempt} 次放置失败：{ex.Message} 清空本轮并重试。");
                    for (int child = root.transform.childCount - 1; child >= 0; child--)
                        UnityEngine.Object.DestroyImmediate(root.transform.GetChild(child).gameObject);
                    continue;
                }

                if (attempt > 1)
                {
                    Debug.Log($"[RegionRatioHigh] 第 {attempt} 次放置成功（attemptSeed={attemptSeed}）。");
                }

                break;
            }

            paired.ConfigureTwoLevelProfile(Profile, Seed, All<PreparationSequenceController>(scene).Single(),
                All<ExperimentStateController>(scene).Single(), All<SceneObjectRegistry>(scene).Single(), All<ExperimentLogger>(scene).Single(), items);
            VerifyBoth(scene);
            foreach (var entry in originals)
                Require(entry.Key != null && entry.Key.parent == entry.Value.parent &&
                    Vector3.Distance(entry.Key.localPosition, entry.Value.position) < 0.00001f &&
                    Quaternion.Angle(entry.Key.localRotation, entry.Value.rotation) < 0.001f &&
                    Vector3.Distance(entry.Key.localScale, entry.Value.scale) < 0.00001f, "Existing transform changed: " + entry.Key.name);
            Require(taskSemantics.All(p => JsonUtility.ToJson(SemanticObjectSummary.From(p.Key)) == p.Value), "Task semantics changed.");
            Require(paired.TryApply(ExperimentLayoutSelection.High, "editor_install", null, out string error), error);
            EditorSceneManager.MarkSceneDirty(scene);
            Require(EditorSceneManager.SaveScene(scene), "Failed to save high scene.");
            AssetDatabase.SaveAssets(); saved = true;
            File.AppendAllText(Output + "/verification.txt", "PASS: all original transforms and task semantics preserved.\n");
            RegionRatioLowSceneInstaller.Focus(scene);
            Selection.activeGameObject = root;
            File.WriteAllText(Output + "/result.txt", "PASS: paired layout saved. Low=52; High=91; shared extras=39; high-only extras=39.\nBackup=" + backup);
        }
        catch
        {
            if (!saved)
            {
                UnityEngine.Object.DestroyImmediate(root);
                paired.ConfigureLowProfile(originalProfile, originalSeed, All<PreparationSequenceController>(scene).Single(),
                    All<ExperimentStateController>(scene).Single(), All<SceneObjectRegistry>(scene).Single(),
                    All<ExperimentLogger>(scene).Single(), lowItems);
            }
            throw;
        }
    }

    [MenuItem("Tools/Stream Manager/Regional Density/Rebuild Pair (New Generator)")]
    public static void RebuildPair()
    {
        // 2026-10-08：改为低/高联合规划的一次性重建（解决高档在均匀低档摆位上无解的问题）。
        InstallPairJoint();
    }

    [MenuItem("Tools/Stream Manager/Regional Density/Low (52 Objects)")]
    public static void Low() => Select(ExperimentLayoutSelection.Low);
    [MenuItem("Tools/Stream Manager/Regional Density/High (91 Objects)")]
    public static void High() => Select(ExperimentLayoutSelection.High);
    private static void Select(ExperimentLayoutSelection requested)
    {
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == RegionRatioLowSceneInstaller.ScenePath, "Open LargeTables first.");
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        Require(paired.ProfileId == Profile, "Install the paired regional profile first.");
        Require(paired.TryApply(requested, "editor_menu", null, out string error), error);
        if (!Application.isPlaying)
        { EditorSceneManager.MarkSceneDirty(scene); Require(EditorSceneManager.SaveScene(scene), "Cannot save layout selection."); }
        RegionRatioLowSceneInstaller.Focus(scene);
    }

    [MenuItem("Tools/Stream Manager/Regional Density/Verify And Preview Both")]
    public static void Verify()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == RegionRatioLowSceneInstaller.ScenePath, "Open LargeTables first.");
        VerifyBoth(scene);
        RegionRatioLowSceneInstaller.Focus(scene);
        File.WriteAllText(Output + "/result.txt", "PASS: paired low/high verified and rendered.");
    }

    private static void VerifyBoth(Scene scene)
    {
        Directory.CreateDirectory(Output);
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        Require(paired.ProfileId == Profile && paired.HighAvailable && paired.Items.Count == 78 && paired.Items.Count(i => i.shared) == 39,
            "Expected 78 extras including 39 shared extras.");
        var originalSelection = paired.Selection;
        var report = new List<string>();
        var neighborhoods = new List<string> { "density,region,targetId,nearestDistractorCenterM,distractorsWithin035M,distractorsWithin050M,sameTypeWithin050M,occludingNeighbors" };
        try
        {
            foreach (var density in new[] { ExperimentLayoutSelection.Low, ExperimentLayoutSelection.High })
            {
                Require(paired.TryApply(density, "regional_validation", null, out string error), error);
                string label = density.ToString().ToLowerInvariant();
                string folder = Output + "/" + label; Directory.CreateDirectory(folder);
                Validate(scene, density, report, neighborhoods, folder);
                RegionRatioLowSceneInstaller.Render(scene, folder);
            }
        }
        finally { paired.TryApply(originalSelection, "restore_after_validation", null, out _); }
        File.WriteAllLines(Output + "/verification.txt", report);
        File.WriteAllLines(Output + "/target-neighborhoods.csv", neighborhoods);
    }

    private static void Validate(Scene scene, ExperimentLayoutSelection density, List<string> report, List<string> neighborhoods, string folder)
    {
        bool high = density == ExperimentLayoutSelection.High;
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        var active = All<SemanticObject>(scene).Where(s => s.gameObject.activeInHierarchy).ToArray();
        var registry = All<SceneObjectRegistry>(scene).Single(); registry.Refresh();
        Require(active.Length == (high ? 91 : 52) && registry.Count == active.Length && active.Select(s => s.StableId).Distinct().Count() == active.Length,
            "Unique registry count mismatch.");
        Require(paired.Items.All(i => i.gameObject.activeSelf == (i.shared || high)), "Incorrect low/high activation.");
        var taskIds = new HashSet<string>(Tasks.SelectMany(t => t));
        var taskBounds = active.Where(s => taskIds.Contains(s.StableId)).Select(s => BoundsOf(s.gameObject)).ToArray();
        var extras = paired.Items.Where(i => i.gameObject.activeInHierarchy).ToArray();
        var csv = new List<string> { "id,region,role,availability,type,sourcePrefab,x,y,z,width,height,depth" };
        for (int region = 0; region < 3; region++)
        {
            string station = Stations[region]; var top = RegionRatioLowSceneInstaller.Top(scene, region);
            var low = paired.Items.Where(i => i.station == station && i.shared).ToArray();
            var additions = paired.Items.Where(i => i.station == station && !i.shared).ToArray();
            Require(low.Length == Tasks[region].Length * 3 && additions.Length == low.Length, "Regional 1:3/1:6 count mismatch.");
            Func<ExperimentPairedLayoutController.Item, string> type = i => i.gameObject.GetComponent<SemanticObject>().ObjectType;
            Require(low.GroupBy(type).All(g => additions.Count(i => type(i) == g.Key) == g.Count()), "Category proportions changed.");
            var localExtras = extras.Where(i => i.station == station).ToArray();
            var members = active.Where(s => Tasks[region].Contains(s.StableId) || localExtras.Any(i => i.gameObject == s.gameObject)).ToArray();
            Require(members.Length == Tasks[region].Length * (high ? 7 : 4), "Region total mismatch.");
            report.Add($"PASS: {density} {station} tasks={Tasks[region].Length} extras={localExtras.Length} total={members.Length} area={F(top.size.x * top.size.z)} densityPerM2={F(members.Length / (top.size.x * top.size.z))}");
            foreach (var s in members)
            {
                var b = BoundsOf(s.gameObject); var item = localExtras.FirstOrDefault(i => i.gameObject == s.gameObject);
                Require(b.min.x >= top.min.x - 0.001f && b.max.x <= top.max.x + 0.001f && b.min.z >= top.min.z - 0.001f && b.max.z <= top.max.z + 0.001f,
                    "Outside table: " + s.StableId);
                string prefab = item == null ? "" : PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(s.gameObject);
                if (item != null)
                {
                    Require(prefab.StartsWith(Package + "Prefabs/", StringComparison.Ordinal), "Nonpackage prefab: " + s.StableId);
                    Require(s.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null).All(m => AssetDatabase.GetAssetPath(m.sharedMesh).StartsWith(Package, StringComparison.Ordinal)), "Nonpackage mesh.");
                    var grab = s.GetComponent<XRGrabInteractable>();
                    Require(s.Selectable && s.Grabbable && grab != null && s.GetComponent<ExperimentGrabReturn>() != null &&
                        grab.colliders.Any(c => c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy),
                        "Missing interaction components: " + s.StableId);
                    Require(Vector3.Distance(s.transform.localPosition, item.homePosition) < 0.00001f, "Home pose mismatch.");
                }
                csv.Add(string.Join(",", new[] { s.StableId, station, item == null ? "task" : "distractor", item == null || item.shared ? "both" : "high_only", s.ObjectType, prefab }
                    .Concat(new[] { b.center.x, b.center.y, b.center.z, b.size.x, b.size.y, b.size.z }.Select(F))));
                if (item == null)
                {
                    // v2 指标（2026-10-08）：在"最近距离/半径内数量"之上追加同类密度与座位视线遮挡计数。
                    var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;
                    var regionExtras = localExtras.Select(i => BoundsOf(i.gameObject)).ToArray();
                    var distances = regionExtras.Select(e => DistanceXZ(b.center, e.center)).ToArray();
                    int sameType = localExtras.Count(i => i.gameObject.GetComponent<SemanticObject>().ObjectType == s.ObjectType &&
                        DistanceXZ(b.center, BoundsOf(i.gameObject).center) <= 0.5f);
                    var eyes = new[] { seat.position + Vector3.up * 1.13f, seat.position + Vector3.up * 1.23f };
                    int occluders = regionExtras.Count(e => eyes.Any(eye =>
                    {
                        var d = b.center - eye;
                        return e.IntersectRay(new Ray(eye, d.normalized), out float hit) && hit < d.magnitude;
                    }));
                    neighborhoods.Add(string.Join(",", density.ToString().ToLowerInvariant(), station, s.StableId, F(distances.Min()),
                        distances.Count(d => d <= 0.35f), distances.Count(d => d <= 0.5f), sameType, occluders));
                }
            }
        }
        for (int i = 0; i < extras.Length; i++)
        {
            var b = BoundsOf(extras[i].gameObject);
            Require(!taskBounds.Any(t => RegionRatioLowSceneInstaller.Overlap(b, t, 0.079f)), "Task clearance failed.");
            for (int j = 0; j < i; j++) Require(!RegionRatioLowSceneInstaller.Overlap(b, BoundsOf(extras[j].gameObject), 0.034f), "Distractor clearance failed.");
        }
        Require(All<PreparationSequenceController>(scene).Single().Steps.Count == 7, "Task steps changed.");
        PicoControllerRayInstaller.Validate(scene);
        report.Add($"PASS: {density}: unique IDs, prefab provenance, category ratio, tabletop containment, shared spacing limits, grab/return, seven stages and controller rays.");
        File.WriteAllLines(folder + "/inventory.csv", csv);
    }
    /// <summary>
    /// 低/高联合规划重建（2026-10-08）：把每件低档物与其高档克隆作为一对，按尺寸降序穿插放置；
    /// 任一件无解则换设种整组重试，保证"低档 = 高档子集"且两档必然相容。适用于双档或仅低档的区域布局。
    /// </summary>
    public static void InstallPairJoint()
    {
        Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Exit Play Mode first.");
        var scene = SceneManager.GetActiveScene();
        Require(scene.path == RegionRatioLowSceneInstaller.ScenePath, "Open the LargeTables scene first.");
        var paired = All<ExperimentPairedLayoutController>(scene).Single();
        bool isPaired = paired.ProfileId == Profile;
        Require(isPaired || paired.ProfileId == RegionRatioLowSceneInstaller.Profile,
            "Rebuild requires the low or paired regional profile.");
        Require(!paired.IsLocked && !All<XRGrabInteractable>(scene).Any(g => g.isSelected),
            "Finish/reset the task and release objects first.");
        Directory.CreateDirectory(Output);
        string backup = "MigrationBackups/BeforeRegionalPairRebuild_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(scene.path, backup + "/SavedScene.unity");
        if (scene.isDirty) Require(EditorSceneManager.SaveScene(scene, backup + "/UnsavedScene.unity", true), "Cannot preserve unsaved scene.");
        File.WriteAllText(Output + "/backup.txt", Path.GetFullPath(backup));

        var taskIds = new HashSet<string>(Tasks.SelectMany(t => t));
        var taskObjects = All<SemanticObject>(scene).Where(s => taskIds.Contains(s.StableId)).ToArray();
        Require(taskObjects.Length == 13, "Expected 13 task objects.");
        var taskSemantics = taskObjects.ToDictionary(s => s, s => JsonUtility.ToJson(SemanticObjectSummary.From(s)));
        var source = taskObjects.ToDictionary(s => s.StableId);
        var definitions = All<ExperimentDistractorLayoutGenerator>(scene).Single().Definitions.ToDictionary(d => d.objectType);
        var tasks = taskObjects.Select(s => BoundsOf(s.gameObject)).ToList();
        var seat = All<PicoExperimentStartPose>(scene).Single().PreparationStart;

        // 只清理由旧布局拥有的干扰实例与根节点；磁盘场景副本已保存。
        foreach (var item in paired.Items) if (item.gameObject != null) UnityEngine.Object.DestroyImmediate(item.gameObject);
        foreach (var oldRoot in scene.GetRootGameObjects()
            .Where(r => r.name == "Paired Layout Apparatus" || r.name == "Regional Low Apparatus"))
            UnityEngine.Object.DestroyImmediate(oldRoot);
        var originals = All<Transform>(scene).ToDictionary(t => t,
            t => (parent: t.parent, position: t.localPosition, rotation: t.localRotation, scale: t.localScale));

        var root = new GameObject("Regional Low Apparatus");
        SceneManager.MoveGameObjectToScene(root, scene);
        bool saved = false;
        try
        {
            const int MaxAttempts = 24;
            for (int attempt = 1; ; attempt++)
            {
                int attemptSeed = unchecked(Seed + attempt * 1000003);
                var random = new System.Random(attemptSeed);
                ExperimentJitteredDensityLayout.ResetAll(attemptSeed);
                for (int child = root.transform.childCount - 1; child >= 0; child--)
                    UnityEngine.Object.DestroyImmediate(root.transform.GetChild(child).gameObject);
                var highRoot = new GameObject(RootName);
                highRoot.transform.SetParent(root.transform, false);
                var items = new List<ExperimentPairedLayoutController.Item>();
                var occupied = new List<Bounds>();
                try
                {
                    for (int region = 0; region < Stations.Length; region++)
                    {
                        var table = RegionRatioLowSceneInstaller.Top(scene, region);
                        var lowGroup = new GameObject(Stations[region] + " Extras");
                        lowGroup.transform.SetParent(root.transform, false);
                        var highGroup = new GameObject(Stations[region] + " High Extras");
                        highGroup.transform.SetParent(highRoot.transform, false);
                        int[] sizeOrder = Enumerable.Range(0, RegionRatioLowSceneInstaller.Types[region].Length)
                            .OrderByDescending(ix => ExpectedSize(source, RegionRatioLowSceneInstaller.Types[region][ix])).ToArray();
                        // 策略矩阵（2026-10-08）：1-8 抖动网格优先；9-12 旧算法整体；13+ 旧算法"先低后高"两段式（历史成功路径的近似，低档 17+ 用原列表序）。
                        bool forceLegacy = attempt > 8;
                        bool twoPhase = attempt > 12;
                        int[] lowOrder = attempt > 16 ? Enumerable.Range(0, sizeOrder.Length).ToArray() : sizeOrder;
                        void PlaceLowItem(int index)
                        {
                            string type = RegionRatioLowSceneInstaller.Types[region][index];
                            var lowItem = CreatePackageItem(scene, type, lowGroup.transform, source);
                            PlaceJointItem(lowItem, table, tasks, occupied, seat, random, forceLegacy);
                            RegionRatioLowSceneInstaller.ConfigureInteraction(lowItem);
                            ConfigureSemantics(lowItem, $"regional_low_{region + 1}_{index + 1:00}_{type}", type, source, definitions);
                            items.Add(new ExperimentPairedLayoutController.Item(lowItem, Stations[region], true));
                            occupied.Add(BoundsOf(lowItem));
                        }
                        void PlaceHighItem(int index)
                        {
                            string type = RegionRatioLowSceneInstaller.Types[region][index];
                            var highItem = CreatePackageItem(scene, type, highGroup.transform, source);
                            PlaceJointItem(highItem, table, tasks, occupied, seat, random, forceLegacy);
                            RegionRatioLowSceneInstaller.ConfigureInteraction(highItem);
                            ConfigureSemantics(highItem, $"regional_high_{region + 1}_{index + 1:00}_{type}", type, source, definitions);
                            items.Add(new ExperimentPairedLayoutController.Item(highItem, Stations[region], false));
                            occupied.Add(BoundsOf(highItem));
                        }
                        if (!twoPhase)
                        {
                            foreach (int index in sizeOrder) { PlaceLowItem(index); PlaceHighItem(index); }
                        }
                        else
                        {
                            foreach (int index in lowOrder) PlaceLowItem(index);
                            foreach (int index in sizeOrder) PlaceHighItem(index);
                        }
                    }
                }
                catch (InvalidOperationException ex) when (attempt < MaxAttempts &&
                    ex.Message.StartsWith("No safe placement", StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[RegionalPairRebuild] 第 {attempt} 次放置失败：{ex.Message} 清空本轮并重试。");
                    continue;
                }

                if (attempt > 1)
                {
                    Debug.Log($"[RegionalPairRebuild] 第 {attempt} 次放置成功（attemptSeed={attemptSeed}）。");
                }

                paired.ConfigureTwoLevelProfile(Profile, Seed, All<PreparationSequenceController>(scene).Single(),
                    All<ExperimentStateController>(scene).Single(), All<SceneObjectRegistry>(scene).Single(),
                    All<ExperimentLogger>(scene).Single(), items);
                VerifyBoth(scene);
                foreach (var entry in originals)
                    Require(entry.Key != null && entry.Key.parent == entry.Value.parent &&
                        Vector3.Distance(entry.Key.localPosition, entry.Value.position) < 0.00001f &&
                        Quaternion.Angle(entry.Key.localRotation, entry.Value.rotation) < 0.001f &&
                        Vector3.Distance(entry.Key.localScale, entry.Value.scale) < 0.00001f, "Existing transform changed: " + entry.Key.name);
                Require(taskSemantics.All(p => JsonUtility.ToJson(SemanticObjectSummary.From(p.Key)) == p.Value), "Task semantics changed.");
                Require(paired.TryApply(ExperimentLayoutSelection.Low, "pair_rebuild", null, out string error), error);
                EditorSceneManager.MarkSceneDirty(scene);
                Require(EditorSceneManager.SaveScene(scene), "Failed to save rebuilt pair.");
                AssetDatabase.SaveAssets(); saved = true;
                File.AppendAllText(Output + "/verification.txt", "PASS: joint pair rebuild preserved tasks and existing transforms.\n");
                RegionRatioLowSceneInstaller.Focus(scene);
                Selection.activeGameObject = root;
                File.WriteAllText(Output + "/result.txt",
                    "PASS: joint pair rebuild. Low=52; High=91; shared=39; high-only=39.\nBackup=" + backup);
                return;
            }
        }
        catch
        {
            if (!saved)
            {
                // 失败时内存场景可能已被清空/半成品；磁盘副本未动，直接重新打开恢复。
                EditorSceneManager.OpenScene(RegionRatioLowSceneInstaller.ScenePath, OpenSceneMode.Single);
            }
            throw;
        }
    }

    internal static void PlaceJointItem(GameObject item, Bounds table, List<Bounds> tasks, List<Bounds> occupied,
        Transform seat, System.Random rng, bool forceLegacy)
    {
        if (forceLegacy)
        {
            RegionRatioLowSceneInstaller.PlaceLegacyRandom(item, table, tasks, occupied, seat, rng, false);
            return;
        }

        RegionRatioLowSceneInstaller.Place(item, table, tasks, occupied, seat, rng, false);
    }

    internal static float ExpectedSize(Dictionary<string, SemanticObject> source, string type)
    {
        if (source.TryGetValue(type, out SemanticObject reference) && reference != null)
            return RegionRatioLowSceneInstaller.Largest(RegionRatioLowSceneInstaller.BoundsOf(reference.gameObject).size);
        return type == "crucible" ? 0.095f : type == "crucible_tongs" ? 0.19f : 0.15f;
    }

    internal static GameObject CreatePackageItem(Scene scene, string type, Transform group, Dictionary<string, SemanticObject> source)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RegionRatioLowSceneInstaller.PrefabFolder + RegionRatioLowSceneInstaller.PrefabNames[type]);
        if (prefab == null) throw new FileNotFoundException("Required package prefab: " + RegionRatioLowSceneInstaller.PrefabNames[type]);
        var item = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        if (item.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
            throw new InvalidOperationException("Unexpected scripts on package prefab: " + RegionRatioLowSceneInstaller.PrefabNames[type]);
        float size = source.TryGetValue(type, out SemanticObject sizeReference) && sizeReference != null
            ? RegionRatioLowSceneInstaller.Largest(RegionRatioLowSceneInstaller.BoundsOf(sizeReference.gameObject).size)
            : 0f;
        RegionRatioLowSceneInstaller.NormalizeSize(item, type, size);
        item.transform.SetParent(group.transform, true);
        return item;
    }

    internal static void ConfigureSemantics(GameObject item, string id, string type,
        Dictionary<string, SemanticObject> source, Dictionary<string, ExperimentDistractorDefinition> definitions)
    {
        var semantic = item.AddComponent<SemanticObject>();
        if (source.TryGetValue(type, out SemanticObject reference) && reference != null)
        {
            var info = SemanticObjectSummary.From(reference);
            semantic.ConfigureDetailed(id, info.displayName, info.objectType, info.category, info.function,
                info.color, info.transparency, info.shape, info.materials,
                type == "spirit_lamp" ? info.contents : "none",
                type == "spirit_lamp" ? info.contentColor : "none",
                type == "spirit_lamp" ? new[] { "unlit" } : new[] { "empty" },
                info.aliases, true, true);
        }
        else if (definitions.TryGetValue(type, out ExperimentDistractorDefinition def))
        {
            semantic.ConfigureDetailed(id, def.displayName, type, def.category, def.function, def.color, def.transparency,
                def.shape, def.materials, "none", "none", new[] { "empty" }, def.aliases, true, true);
        }
        else
        {
            throw new InvalidOperationException("Missing semantics: " + type);
        }

        item.name = semantic.DisplayName;
        foreach (var component in item.GetComponentsInChildren<Component>(true))
            if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
        foreach (var child in item.GetComponentsInChildren<Transform>(true))
            PrefabUtility.RecordPrefabInstancePropertyModifications(child.gameObject);
    }

    private static string F(float value) => value.ToString("F5", CultureInfo.InvariantCulture);
    private static float DistanceXZ(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
}
