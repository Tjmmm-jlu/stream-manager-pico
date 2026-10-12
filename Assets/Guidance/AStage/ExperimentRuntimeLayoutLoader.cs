using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

/// <summary>
/// 运行时布局装载器（2026-10-09）。
/// 由编辑器装配工具（Tools > Stream Manager > Regional Density > Setup Runtime Layout Loader）填好
/// prefabPool / 任务引用 / 台面范围后，网页端可通过 guidance 通道发送 apply_layout_spec（JSON 规格）：
///   1) 校验：数量、ID 唯一、类型齐全、件间间距、任务净空、台面范围；
///   2) 在独立根节点下实例化 78 件（39 共享 + 39 仅高档），补齐碰撞、抓取、复位与语义组件；
///   3) 接管 ExperimentPairedLayoutController（隐藏烘焙布局），网页低/高切换对新布局继续可用；
///   4) clear_layout_spec 时销毁运行时布局并回退到烘焙布局。
/// 设计约束：布局求解与验证全部在编辑器离线完成；设备端只做"装载 + 校验 + 回滚"，不做随机求解。
/// </summary>
public sealed class ExperimentRuntimeLayoutLoader : MonoBehaviour
{
    [Header("引用（由装配工具填充）")]
    [SerializeField] private ExperimentPairedLayoutController pairedLayout;
    [SerializeField] private SceneObjectRegistry registry;
    [SerializeField] private ExperimentLogger logger;
    [SerializeField] private List<SemanticObject> taskObjects = new List<SemanticObject>();

    [Header("台面范围（装配工具填充；用于装载校验）")]
    [SerializeField] private List<string> tableStations = new List<string>();
    [SerializeField] private List<Vector3> tableCenters = new List<Vector3>();
    [SerializeField] private List<Vector3> tableSizes = new List<Vector3>();

    [Header("预制体池（装配工具填充）")]
    [SerializeField] private List<RuntimeLayoutPrefabEntry> prefabPool = new List<RuntimeLayoutPrefabEntry>();

    [Header("运行参数")]
    [SerializeField] private string rootName = "Runtime Variant";

    public bool HasRuntimeLayout => _runtimeItems.Count > 0;
    public int ActiveIndex { get; private set; } = -1;
    public int ActiveSeed { get; private set; } = -1;
    public int PoolTypeCount => prefabPool.Count;

    private readonly List<ExperimentPairedLayoutController.Item> _runtimeItems =
        new List<ExperimentPairedLayoutController.Item>();
    private List<ExperimentPairedLayoutController.Item> _bakedItems;
    private GameObject _runtimeRoot;
    private int _expectedTotal = -1;
    private int _expectedShared = -1;

    /// <summary>尝试应用一份布局规格；失败时场景保持不变并返回原因。</summary>
    public bool TryApplySpec(string json, int variantIndex, int variantSeed, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) { error = "布局规格为空。"; return false; }
        if (pairedLayout == null) { error = "装载器缺少 pairedLayout 引用（请先运行装配工具）。"; return false; }
        if (!Application.isPlaying) { error = "运行时布局只能在播放/设备运行时装载。"; return false; }
        if (prefabPool.Count == 0) { error = "预制体池为空（请先运行装配工具）。"; return false; }
        if (pairedLayout.IsLocked) { error = "任务流程进行中，禁止切换布局。"; return false; }
        if (FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None).Any(grab => grab.isSelected))
        { error = "手中仍有物体，请先放下再切换布局。"; return false; }

        RuntimeLayoutSpec spec;
        try { spec = JsonUtility.FromJson<RuntimeLayoutSpec>(json); }
        catch (Exception exception) { error = "规格 JSON 解析失败：" + exception.Message; return false; }
        if (spec == null || spec.items == null || spec.items.Count == 0) { error = "规格内容为空。"; return false; }

        CaptureBakedBaseline();
        if (spec.items.Count != _expectedTotal || spec.items.Count(item => item.shared) != _expectedShared)
        {
            error = $"规格数量不符：{spec.items.Count} 件（共享 {spec.items.Count(item => item.shared)}），" +
                $"期望 {_expectedTotal} 件（共享 {_expectedShared}）。";
            return false;
        }

        var pool = new Dictionary<string, RuntimeLayoutPrefabEntry>(StringComparer.Ordinal);
        foreach (RuntimeLayoutPrefabEntry entry in prefabPool)
        {
            if (entry != null && !string.IsNullOrWhiteSpace(entry.type) && entry.prefab != null)
                pool[entry.type] = entry;
        }
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RuntimeLayoutItemSpec item in spec.items)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.id) || string.IsNullOrWhiteSpace(item.type))
            { error = "规格包含非法条目。"; return false; }
            if (!seenIds.Add(item.id)) { error = "规格存在重复 ID：" + item.id; return false; }
            if (!pool.ContainsKey(item.type)) { error = "预制体池缺少类型：" + item.type; return false; }
        }

        DestroyRuntimeRoot();
        _runtimeItems.Clear();
        _runtimeRoot = new GameObject(rootName);
        _runtimeRoot.transform.SetParent(transform, false);
        try
        {
            foreach (RuntimeLayoutItemSpec item in spec.items)
            {
                RuntimeLayoutPrefabEntry entry = pool[item.type];
                GameObject instance = Instantiate(entry.prefab, _runtimeRoot.transform, false);
                instance.name = string.IsNullOrWhiteSpace(entry.semantic.displayName)
                    ? item.type
                    : entry.semantic.displayName;
                instance.transform.localScale = item.scale;
                instance.transform.SetPositionAndRotation(item.position, Quaternion.Euler(item.eulerAngles));
                foreach (Transform child in instance.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = 0;
                foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;

                var box = instance.AddComponent<BoxCollider>();
                box.center = item.colliderCenter;
                box.size = item.colliderSize;

                var body = instance.AddComponent<Rigidbody>();
                body.useGravity = false;
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                var grab = instance.AddComponent<XRGrabInteractable>();
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                grab.throwOnDetach = false;
                grab.retainTransformParent = true;
                grab.useDynamicAttach = true;
                grab.colliders.Clear();
                grab.colliders.Add(box);

                // ExperimentGrabReturn 在 Awake 中记录原位；必须放在位姿设置完成后添加。
                instance.AddComponent<ExperimentGrabReturn>();

                SemanticObjectSummary template = entry.semantic;
                var semantic = instance.AddComponent<SemanticObject>();
                semantic.ConfigureDetailed(item.id, template.displayName, template.objectType, template.category,
                    template.function, template.color, template.transparency, template.shape, template.materials,
                    template.contents, template.contentColor, template.stateTags, template.aliases,
                    template.selectable, template.grabbable);

                _runtimeItems.Add(new ExperimentPairedLayoutController.Item(instance, item.station, item.shared));
            }
        }
        catch (Exception exception)
        {
            DestroyRuntimeRoot();
            _runtimeItems.Clear();
            error = "实例化失败：" + exception.Message;
            return false;
        }

        Physics.SyncTransforms();
        if (!ValidateLayout(out error))
        {
            DestroyRuntimeRoot();
            _runtimeItems.Clear();
            return false;
        }

        foreach (ExperimentPairedLayoutController.Item item in _bakedItems)
            if (item.gameObject != null) item.gameObject.SetActive(false);
        pairedLayout.AdoptItems(_runtimeItems, "runtime_spec");
        registry?.Refresh();
        ActiveIndex = variantIndex;
        ActiveSeed = variantSeed;
        string detail = $"index={ActiveIndex};seed={ActiveSeed};items={_runtimeItems.Count};" +
            $"shared={_runtimeItems.Count(i => i.shared)};source=web";
        logger?.LogEvent("runtime_layout_applied", detail);
        Debug.Log("[RuntimeLayout] applied " + detail, this);
        return true;
    }

    /// <summary>销毁运行时布局并回退到烘焙布局。</summary>
    public bool ClearRuntimeLayout(out string error)
    {
        error = null;
        if (pairedLayout == null) { error = "装载器缺少 pairedLayout 引用。"; return false; }
        if (!Application.isPlaying) { error = "装载仅在运行时可用。"; return false; }
        if (!HasRuntimeLayout) { error = "当前没有运行时布局。"; return false; }
        if (pairedLayout.IsLocked) { error = "任务流程进行中，禁止切换布局。"; return false; }
        if (FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None).Any(grab => grab.isSelected))
        { error = "手中仍有物体，请先放下再恢复布局。"; return false; }

        DestroyRuntimeRoot();
        _runtimeItems.Clear();
        if (_bakedItems != null) pairedLayout.AdoptItems(_bakedItems, "runtime_clear");
        registry?.Refresh();
        logger?.LogEvent("runtime_layout_cleared", $"was index={ActiveIndex};seed={ActiveSeed}");
        Debug.Log($"[RuntimeLayout] cleared (was index={ActiveIndex};seed={ActiveSeed})", this);
        ActiveIndex = -1;
        ActiveSeed = -1;
        return true;
    }

    private void CaptureBakedBaseline()
    {
        if (_bakedItems != null) return;
        _bakedItems = pairedLayout.Items.Where(item => item != null && item.gameObject != null).ToList();
        _expectedTotal = _bakedItems.Count;
        _expectedShared = _bakedItems.Count(item => item.shared);
    }

    private void DestroyRuntimeRoot()
    {
        if (_runtimeRoot != null)
        {
            Destroy(_runtimeRoot);
            _runtimeRoot = null;
            return;
        }
        Transform existing = transform.Find(rootName);
        if (existing != null) Destroy(existing.gameObject);
    }

    /// <summary>装载后校验：件间间距、任务净空、台面范围。与编辑器安装器同一口径（0.034 / 0.079 容差）。</summary>
    private bool ValidateLayout(out string error)
    {
        error = null;
        var itemBounds = new List<Bounds>(_runtimeItems.Count);
        foreach (ExperimentPairedLayoutController.Item item in _runtimeItems)
            itemBounds.Add(CalculateBounds(item.gameObject));

        Bounds[] taskBounds = taskObjects
            .Where(task => task != null)
            .Select(task => CalculateBounds(task.gameObject))
            .ToArray();

        for (int i = 0; i < itemBounds.Count; i++)
        {
            for (int j = i + 1; j < itemBounds.Count; j++)
            {
                if (OverlapXZ(itemBounds[i], itemBounds[j], 0.034f))
                { error = "装载校验失败：件间间距不足（第 " + (i + 1) + " 与 " + (j + 1) + " 件）。"; return false; }
            }
            foreach (Bounds task in taskBounds)
            {
                if (OverlapXZ(itemBounds[i], task, 0.079f))
                { error = "装载校验失败：与任务器材净空不足（第 " + (i + 1) + " 件）。"; return false; }
            }
            if (!WithinAssignedTable(i, itemBounds[i]))
            { error = "装载校验失败：第 " + (i + 1) + " 件超出所属台面。"; return false; }
        }
        return true;
    }

    private bool WithinAssignedTable(int itemIndex, Bounds bound)
    {
        string station = _runtimeItems[itemIndex].station;
        for (int table = 0; table < tableStations.Count && table < tableCenters.Count && table < tableSizes.Count; table++)
        {
            if (!string.Equals(tableStations[table], station, StringComparison.Ordinal)) continue;
            Bounds area = new Bounds(tableCenters[table], tableSizes[table]);
            return bound.min.x >= area.min.x - 0.001f && bound.max.x <= area.max.x + 0.001f &&
                bound.min.z >= area.min.z - 0.001f && bound.max.z <= area.max.z + 0.001f;
        }
        return true; // 未登记该台面时跳过（不阻塞装载）
    }

    /// <summary>与编辑器安装器 MeshBounds(item, identity) 同一算法：逐 MeshFilter 取网格包围盒 8 角点的世界 AABB。
    /// 忽略禁用/未激活网格与粒子/线条渲染器，保证与离线放置和校验（0.034 / 0.079）完全同口径。
    /// 2026-10-09 修正：此前用 Renderer.bounds（含线条/粒子等），比编辑器度量更大，导致合格布局被误判回滚。</summary>
    private static Bounds CalculateBounds(GameObject item)
    {
        Bounds result = default;
        bool initialized = false;
        foreach (MeshFilter filter in item.GetComponentsInChildren<MeshFilter>())
        {
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (filter.sharedMesh == null || renderer == null || !renderer.enabled || !filter.gameObject.activeInHierarchy)
                continue;
            Bounds local = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = local.center + Vector3.Scale(local.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 point = filter.transform.TransformPoint(corner);
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

    // ---- 装配工具专用接口（编辑器调用；运行时不需要）----

    public void ConfigureSceneReferences(
        ExperimentPairedLayoutController paired,
        SceneObjectRegistry sceneRegistry,
        ExperimentLogger experimentLogger,
        List<SemanticObject> tasks,
        List<string> stations,
        List<Vector3> centers,
        List<Vector3> sizes)
    {
        pairedLayout = paired;
        registry = sceneRegistry;
        logger = experimentLogger;
        taskObjects = tasks ?? new List<SemanticObject>();
        tableStations = stations ?? new List<string>();
        tableCenters = centers ?? new List<Vector3>();
        tableSizes = sizes ?? new List<Vector3>();
    }

    public void ConfigurePrefabPool(List<RuntimeLayoutPrefabEntry> pool) =>
        prefabPool = pool ?? new List<RuntimeLayoutPrefabEntry>();
}
