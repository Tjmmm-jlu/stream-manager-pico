using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class ExperimentPairedLayoutController : MonoBehaviour
{
    [Serializable]
    public sealed class Item
    {
        public GameObject gameObject;
        public string station;
        public bool shared;
        public Vector3 homePosition;
        public Quaternion homeRotation;
        public Vector3 homeScale;
        public Transform homeParent;

        public Item(GameObject instance, string table, bool inBoth)
        {
            gameObject = instance; station = table; shared = inBoth;
            homeParent = instance.transform.parent;
            homePosition = instance.transform.localPosition;
            homeRotation = instance.transform.localRotation;
            homeScale = instance.transform.localScale;
        }

        public void Restore()
        {
            if (gameObject == null) return;
            var grab = gameObject.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            gameObject.transform.SetParent(homeParent, false);
            gameObject.transform.localPosition = homePosition;
            gameObject.transform.localRotation = homeRotation;
            gameObject.transform.localScale = homeScale;
            var body = gameObject.GetComponent<Rigidbody>();
            if (body == null) return;
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
            body.position = gameObject.transform.position;
            body.rotation = gameObject.transform.rotation;
            body.Sleep();
        }
    }

    [SerializeField] private PreparationSequenceController sequence;
    [SerializeField] private ExperimentStateController state;
    [SerializeField] private SceneObjectRegistry registry;
    [SerializeField] private ExperimentLogger logger;
    [SerializeField] private string profileId = "large-tables-A";
    [SerializeField] private int layoutSeed = 20260811;
    [SerializeField] private ExperimentLayoutSelection selection = ExperimentLayoutSelection.Low;
    [SerializeField] private List<Item> items = new List<Item>();
    [SerializeField] private int expectedTotal = 18;
    [SerializeField] private int expectedShared = 6;
    [SerializeField] private bool highAvailable = true;

    public string ProfileId => profileId;
    public int LayoutSeed => layoutSeed;
    public ExperimentLayoutSelection Selection => selection;
    public IReadOnlyList<Item> Items => items;
    public bool HighAvailable => highAvailable;
    public int ActiveCount => items.Count(item => item.gameObject != null && item.gameObject.activeInHierarchy);
    public bool IsLocked => sequence != null && sequence.IsRunning || state != null &&
        state.CurrentState != ExperimentTrialState.Idle && state.CurrentState != ExperimentTrialState.Completed &&
        state.CurrentState != ExperimentTrialState.Aborted;

    public void Configure(PreparationSequenceController steps, ExperimentStateController trial,
        SceneObjectRegistry catalog, ExperimentLogger log, IEnumerable<Item> placedItems)
    {
        sequence = steps; state = trial; registry = catalog; logger = log;
        items = placedItems.ToList();
        selection = ExperimentLayoutSelection.Low;
        ApplyObjects();
    }

    // 新版区域布局只先配置低档；旧场景继续使用原来的 18/6 两档配置。
    public void ConfigureLowProfile(string id, int seed, PreparationSequenceController steps,
        ExperimentStateController trial, SceneObjectRegistry catalog, ExperimentLogger log,
        IEnumerable<Item> placedItems)
    {
        var snapshot = placedItems.ToList();
        if (snapshot.Count == 0 || snapshot.Any(item => item.gameObject == null || !item.shared))
            throw new ArgumentException("A low-only layout requires a nonempty shared object list.");
        profileId = id; layoutSeed = seed;
        expectedTotal = expectedShared = snapshot.Count; highAvailable = false;
        Configure(steps, trial, catalog, log, snapshot);
    }

    public void ConfigureTwoLevelProfile(string id, int seed, PreparationSequenceController steps,
        ExperimentStateController trial, SceneObjectRegistry catalog, ExperimentLogger log,
        IEnumerable<Item> placedItems)
    {
        var snapshot = placedItems.ToList();
        int shared = snapshot.Count(item => item.shared);
        if (shared == 0 || shared == snapshot.Count || snapshot.Any(item => item.gameObject == null) ||
            snapshot.Select(item => item.gameObject).Distinct().Count() != snapshot.Count)
            throw new ArgumentException("A paired layout requires unique shared and high-only objects.");
        profileId = id; layoutSeed = seed;
        expectedTotal = snapshot.Count; expectedShared = shared; highAvailable = true;
        Configure(steps, trial, catalog, log, snapshot);
    }

    /// <summary>运行时装载器专用（2026-10-09）：整体替换物品清单（烘焙 ↔ 运行时布局），保持低/高选择语义。</summary>
    public void AdoptItems(IEnumerable<Item> newItems, string source)
    {
        var snapshot = newItems?.Where(item => item != null && item.gameObject != null).ToList();
        if (snapshot == null || snapshot.Count == 0)
            throw new ArgumentException("AdoptItems requires a nonempty item list.");
        int shared = snapshot.Count(item => item.shared);
        items = snapshot;
        expectedTotal = snapshot.Count;
        expectedShared = shared;
        highAvailable = shared < snapshot.Count;
        if (!highAvailable) selection = ExperimentLayoutSelection.Low;
        ApplyObjects();
        LogLayout("layout_adopted", source);
    }

    private void OnEnable()
    {
        if (sequence != null) sequence.Resetting += ResetObjects;
    }

    private void OnDisable()
    {
        if (sequence != null) sequence.Resetting -= ResetObjects;
    }

    private void Start()
    {
        ApplyObjects();
        LogLayout("layout_ready", "scene_start");
    }

    public bool TryApply(ExperimentLayoutSelection requested, string source, int? seed, out string error)
    {
        error = null;
        if (requested != ExperimentLayoutSelection.Low && requested != ExperimentLayoutSelection.High)
            error = "This scene supports low and high only.";
        else if (requested == ExperimentLayoutSelection.High && !highAvailable)
            error = "This regional profile currently provides the low layout only; high has not been configured.";
        else if (seed.HasValue && seed.Value != layoutSeed)
            error = "This fixed layout uses seed " + layoutSeed + ".";
        else if (IsLocked)
            error = "Reset or finish the current trial before changing the layout.";
        else if (FindObjectsOfType<XRGrabInteractable>().Any(grab => grab.isSelected))
            error = "Release held objects before changing the layout.";
        else if (items.Count != expectedTotal || items.Any(item => item.gameObject == null) ||
            items.Count(item => item.shared) != expectedShared)
            error = $"Layout is incomplete: expected {expectedTotal} objects, including {expectedShared} shared objects.";
        if (error != null) return false;
        selection = requested;
        ApplyObjects();
        LogLayout("layout_changed", source);
        return true;
    }

    private void ApplyObjects()
    {
        foreach (var item in items)
        {
            if (item.gameObject == null) continue;
            item.Restore();
            item.gameObject.SetActive(item.shared || selection == ExperimentLayoutSelection.High);
        }
        Physics.SyncTransforms();
        registry?.Refresh();
        if (Application.isPlaying) FindObjectOfType<ExperimentInteractionTracker>()?.RefreshTrackedObjects();
    }

    private void ResetObjects()
    {
        ApplyObjects();
        LogLayout("layout_reset", "sequence_reset");
    }

    private void LogLayout(string eventType, string source)
    {
        string detail = $"profile={profileId};density={selection.ToString().ToLowerInvariant()};" +
            $"seed={layoutSeed};generated={ActiveCount};registry={registry?.Count ?? 0};source={source};" +
            "objects=" + string.Join(",", items.Where(item => item.gameObject != null && item.gameObject.activeInHierarchy)
                .Select(item => item.gameObject.GetComponent<SemanticObject>().StableId));
        if (Application.isPlaying) logger?.LogEvent(eventType, detail);
        Debug.Log("[PairedLayout] " + detail, this);
    }
}
