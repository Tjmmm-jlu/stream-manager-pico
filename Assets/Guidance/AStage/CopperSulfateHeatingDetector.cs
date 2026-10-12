using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class CopperSulfateHeatingDetector : MonoBehaviour
{
    public const string AssembleAction = "assemble_heating";
    public const string HeatAction = "concentrate_solution";
    public const string MountDishAction = "mount_heating_dish";
    public const string PlaceLampAction = "place_heating_lamp";
    public const string RemoveCapAction = "remove_lamp_cap";
    public const string IgniteAction = "ignite_lamp";
    public const string ConcentrateDwellAction = "concentrate_dwell";
    public const string ExtinguishAction = "extinguish_lamp";
    public const string CoolingDwellAction = "cooling_dwell";
    public const float CoolingSeconds = 5f;
    public enum HeatingStage { Waiting, Assembling, Ready, Heating, AwaitingExtinguish, Completed, Cooling }
    [SerializeField] private PreparationSequenceController sequence;
    [SerializeField] private CopperSulfateFiltrationDetector filtration;
    [SerializeField] private XRGrabInteractable lamp;
    [SerializeField] private XRGrabInteractable cap;
    [SerializeField] private XRGrabInteractable lighter;
    [SerializeField] private Transform dishMount;
    [SerializeField] private Transform lampMount;
    [SerializeField] private Transform capMount;
    [SerializeField] private Transform capParking;
    [SerializeField] private Transform wick;
    [SerializeField] private Transform ignitionTip;
    [SerializeField] private GameObject flame;
    [SerializeField] private ParticleSystem steam;
    [SerializeField] private GameObject crystallizedContents;
    [SerializeField, Min(1f)] private float heatingSeconds = 20f;
    [SerializeField, Range(0.1f, 0.9f)] private float remainingFraction = 0.5f;
    private Pose[] _homes;
    private string _lampSemantic;
    private string _dishSemantic;
    private float _dishDwell, _lampDwell, _capDwell, _ignitionDwell, _notify;
    private float _coolingElapsed;
    private bool _capMustLeave, _dishMustLeave, _lampMustLeave, _initialized;
    private Vector3 _flameScale;
    public event Action StateChanged;
    public HeatingStage Stage { get; private set; }
    public string Status { get; private set; } = "idle";
    public bool Lit { get; private set; }
    public bool Covered { get; private set; } = true;
    public bool DishMounted { get; private set; }
    public bool LampMounted { get; private set; }
    public float InitialMl { get; private set; }
    public float EvaporatedMl { get; private set; }
    public float TargetMl => InitialMl * remainingFraction;
    public float Progress => InitialMl > 0f ? Mathf.Clamp01(EvaporatedMl / (InitialMl - TargetMl)) : 0f;
    public float HeatingSeconds => heatingSeconds;
    public float CoolingRemainingSeconds => Stage == HeatingStage.Cooling ? Mathf.Max(0f, CoolingSeconds - _coolingElapsed) : 0f;
    public GameObject Crystals => crystallizedContents;
    public bool CanRetry => IsInAssembleSegment || IsInHeatSegment;

    // 元步骤模式下：组装段（装蒸发皿/放酒精灯/整体组装）与加热段（取帽→点火→浓缩→盖帽→冷却）各自细分。
    private bool IsInAssembleSegment => IsAction(AssembleAction) ||
        IsAction(MountDishAction) || IsAction(PlaceLampAction);

    private bool IsInHeatSegment => IsAction(HeatAction) ||
        IsAction(RemoveCapAction) || IsAction(IgniteAction) ||
        IsAction(ConcentrateDwellAction) || IsAction(ExtinguishAction) ||
        IsAction(CoolingDwellAction);
    public bool HasState => CanRetry || Stage == HeatingStage.Completed;
    public XRGrabInteractable Lamp => lamp;
    public XRGrabInteractable Cap => cap;
    public XRGrabInteractable Lighter => lighter;
    public Transform DishMount => dishMount;
    public Transform LampMount => lampMount;
    public Transform CapMount => capMount;
    public Transform CapParking => capParking;
    public Transform Wick => wick;
    public Transform IgnitionTip => ignitionTip;
    public GameObject Flame => flame;
    private LiquidContainer Dish => filtration.Dish;
    private XRGrabInteractable Stand => filtration.Stand;

    public void Configure(PreparationSequenceController steps, CopperSulfateFiltrationDetector filter,
        XRGrabInteractable burner, XRGrabInteractable lid, XRGrabInteractable igniter, Transform dishSeat,
        Transform lampSeat, Transform lidSeat, Transform parking, Transform wickPoint, Transform lighterTip,
        GameObject flameVisual, ParticleSystem steamVisual)
    {
        sequence = steps; filtration = filter; lamp = burner; cap = lid; lighter = igniter;
        dishMount = dishSeat; lampMount = lampSeat; capMount = lidSeat; capParking = parking;
        wick = wickPoint; ignitionTip = lighterTip; flame = flameVisual; steam = steamVisual;
    }
    private void Awake() => Initialize();
    private void OnEnable()
    {
        if (sequence == null) return;
        sequence.Resetting -= ResetExperiment; sequence.Resetting += ResetExperiment;
    }
    private void OnDisable()
    {
        if (sequence != null) sequence.Resetting -= ResetExperiment;
        SetLit(false);
    }
    private bool Initialize()
    {
        if (_initialized) return true;
        if (sequence == null || filtration == null || lamp == null || cap == null || lighter == null ||
            dishMount == null || lampMount == null || capMount == null || capParking == null || wick == null || ignitionTip == null) return false;
        _homes = new[] { new Pose(lamp.transform), new Pose(lighter.transform), new Pose(cap.transform), new Pose(Dish.transform) };
        _lampSemantic = JsonUtility.ToJson(lamp.GetComponent<SemanticObject>());
        _flameScale = flame.transform.localScale;
        _initialized = true; OnEnable(); SetLit(false); return true;
    }
    private bool IsAction(string action) => sequence != null && sequence.IsRunning && sequence.CurrentStep?.completionAction == action;
    private void Update() => Tick(Time.deltaTime);
    public void Tick(float elapsed)
    {
        if (!isActiveAndEnabled || !Initialize()) return;
        float dt = Mathf.Clamp(elapsed, 0f, 0.1f);
        if (Stage == HeatingStage.Cooling)
        {
            if (IsInHeatSegment) TickCooling(Mathf.Max(0f, elapsed));
            Notify(dt);
            return;
        }
        UpdateCap(dt);
        // The cap's dwell tick starts the countdown; only subsequent elapsed time counts toward five seconds.
        if (Stage == HeatingStage.Cooling) return;
        if (!CanRetry) { if (Lit) SetLit(false); return; }
        if (Stage == HeatingStage.Waiting)
        {
            if (filtration.Stage != CopperSulfateFiltrationDetector.FiltrationStage.Completed || Dish.VolumeMl <= 0f)
            { Status = "filtrate_not_ready"; Notify(dt); return; }
            InitialMl = Dish.VolumeMl; _dishSemantic = JsonUtility.ToJson(Dish.GetComponent<SemanticObject>());
            Stage = HeatingStage.Assembling;
        }
        UpdateMounts(dt);
        bool assembled = DishMounted && LampMounted && !filtration.Attached && !Stand.isSelected &&
            Vector3.Dot(Stand.transform.up, Vector3.up) >= 0.98f;
        if (IsInAssembleSegment)
        {
            Status = filtration.Attached ? "remove_filter_funnel" : !DishMounted ? "mount_heating_dish" : "place_heating_lamp";
            // 元步骤：目标状态已达成时完成对应子步骤（处理提前达成的情况）。
            if (IsAction(MountDishAction) && DishMounted)
                sequence.CompleteAction(MountDishAction);
            if (IsAction(PlaceLampAction) && LampMounted)
                sequence.CompleteAction(PlaceLampAction);
            if (assembled)
            { Stage = HeatingStage.Ready; Status = "remove_lamp_cap"; Log("heating_assembled"); sequence.CompleteAction(AssembleAction); }
        }
        else
        {
            if (!assembled) { SetLit(false); Status = "heating_assembly_incomplete"; }
            else if (!Lit)
            {
                Status = Covered ? "remove_lamp_cap" : "bring_lighter_to_wick";
                bool ignite = !Covered && LiquidPourInteractor.HeldByHand(lighter) &&
                    Vector3.Distance(ignitionTip.position, wick.position) <= 0.06f && Dish.VolumeMl > 0f;
                _ignitionDwell = ignite ? _ignitionDwell + dt : 0f;
                if (_ignitionDwell >= 0.3f && Progress < 1f)
                { SetLit(true); Stage = HeatingStage.Heating; SetTags(Dish.gameObject, "filtered", "hot", "heating"); Log("lamp_ignited"); sequence.CompleteAction(IgniteAction); }
            }
            if (Lit)
            {
                float amount = Mathf.Min(Mathf.Max(0f, Dish.VolumeMl - TargetMl), (InitialMl - TargetMl) / heatingSeconds * dt);
                EvaporatedMl += Dish.Remove(amount); Dish.RefreshVisual();
                Status = Progress >= 0.9999f ? "cover_lamp_to_finish" : "heating_solution";
                if (Progress >= 0.9999f && Stage != HeatingStage.AwaitingExtinguish)
                { Stage = HeatingStage.AwaitingExtinguish; SetTags(Dish.gameObject, "filtered", "hot", "concentrated"); sequence.CompleteAction(ConcentrateDwellAction); }
                flame.transform.localScale = _flameScale * (1f + 0.06f * Mathf.Sin(Time.unscaledTime * 17f));
                if (steam != null)
                {
                    steam.GetComponent<Renderer>().enabled = true;
                    if (Application.isPlaying && !steam.isPlaying) steam.Play();
                }
            }
        }
        Notify(dt);
    }
    private void UpdateCap(float dt)
    {
        if (Covered && cap.isSelected)
        {
            cap.transform.SetParent(_homes[2].Parent, true); Covered = false;
            SetTags(lamp.gameObject, Lit ? "lit" : "unlit", "uncapped"); SetTags(cap.gameObject, "removed");
            _capMustLeave = true; _capDwell = 0f; Log("lamp_cap_removed");
            sequence.CompleteAction(RemoveCapAction);
        }
        if (Covered) { Move(cap, capMount); return; }
        bool held = LiquidPourInteractor.HeldByHand(cap);
        float distance = Vector3.Distance(cap.transform.position, capMount.position);
        if (!held || distance > 0.075f) _capMustLeave = false;
        bool align = !_capMustLeave && held && distance <= 0.06f && Vector3.Dot(cap.transform.up, capMount.up) >= 0.85f;
        _capDwell = align ? _capDwell + dt : 0f;
        if (_capDwell < 0.3f) return;
        Cancel(cap); cap.transform.SetParent(lamp.transform, true); Move(cap, capMount); Covered = true; _capDwell = 0f;
        bool wasLit = Lit; SetLit(false); Log("lamp_capped");
        SetTags(cap.gameObject, "installed");
        if (wasLit && IsInHeatSegment && Progress >= 0.9999f)
        {
            Stage = HeatingStage.Cooling; Status = "cooling_solution"; _coolingElapsed = 0f;
            lamp.enabled = cap.enabled = Dish.Grab.enabled = false;
            SetTags(Dish.gameObject, "filtered", "hot", "concentrated", "cooling");
            Log("concentration_completed"); Log("cooling_started");
            sequence.CompleteAction(ExtinguishAction);
            StateChanged?.Invoke();
        }
        else if (wasLit)
        { Stage = HeatingStage.Ready; Status = "heating_paused"; SetTags(Dish.gameObject, "filtered", "hot", "partly_concentrated"); Log("heating_paused"); }
    }
    private void TickCooling(float elapsed)
    {
        _coolingElapsed = Mathf.Min(CoolingSeconds, _coolingElapsed + elapsed);
        if (_coolingElapsed < CoolingSeconds - 0.0001f) return;
        _coolingElapsed = CoolingSeconds;
        if (crystallizedContents != null) crystallizedContents.SetActive(true);
        Stage = HeatingStage.Completed; Status = "crystallization_completed";
        cap.enabled = true; SetLit(false);
        var semantic = Dish.GetComponent<SemanticObject>();
        if (semantic != null)
            semantic.ConfigureDetailed(semantic.StableId, semantic.DisplayName, semantic.ObjectType, semantic.Category,
                semantic.Function, semantic.Color, semantic.Transparency, semantic.Shape, semantic.Materials,
                "copper_sulfate_crystals_and_mother_liquor", "blue", new[] { "filtered", "concentrated", "cooled", "crystallized" },
                semantic.Aliases, semantic.Selectable, semantic.Grabbable);
        Log("crystallization_completed");
        sequence.CompleteAction(CoolingDwellAction);
        sequence.CompleteAction(HeatAction);
        StateChanged?.Invoke();
    }
    private void UpdateMounts(float dt)
    {
        if (DishMounted && Dish.Grab.isSelected) { DishMounted = false; _dishMustLeave = true; Dish.transform.SetParent(_homes[3].Parent, true); }
        if (LampMounted && lamp.isSelected) { LampMounted = false; _lampMustLeave = true; lamp.transform.SetParent(_homes[0].Parent, true); }
        if (Lit || Stand.isSelected || Vector3.Dot(Stand.transform.up, Vector3.up) < 0.98f) return;
        if (!DishMounted && !filtration.Attached)
            DishMounted = TryMount(Dish.Grab, dishMount, dt, ref _dishDwell, ref _dishMustLeave);
        if (!LampMounted) LampMounted = TryMount(lamp, lampMount, dt, ref _lampDwell, ref _lampMustLeave);
        Stand.enabled = !DishMounted && !LampMounted;
    }
    private bool TryMount(XRGrabInteractable item, Transform mount, float dt, ref float dwell, ref bool mustLeave)
    {
        bool held = LiquidPourInteractor.HeldByHand(item);
        float distance = Vector3.Distance(item.transform.position, mount.position);
        if (!held || distance > 0.09f) mustLeave = false;
        bool aligned = !mustLeave && held && distance <= 0.07f && Vector3.Dot(item.transform.up, mount.up) >= 0.94f;
        dwell = aligned ? dwell + dt : 0f;
        if (dwell < 0.25f) return false;
        Cancel(item); item.transform.SetParent(Stand.transform, true); Move(item, mount); dwell = 0f;
        bool isLamp = item == lamp;
        Log(isLamp ? "heating_lamp_mounted" : "heating_dish_mounted");
        sequence.CompleteAction(isLamp ? PlaceLampAction : MountDishAction);
        return true;
    }
    private void SetLit(bool value)
    {
        Lit = value; _ignitionDwell = 0f;
        if (flame != null) flame.SetActive(value);
        if (!value && steam != null)
        { steam.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); steam.GetComponent<Renderer>().enabled = false; }
        if (!_initialized) return;
        // 退出场景时器材可能已先销毁，关闭火焰不应访问失效的抓取组件。
        if (lamp != null)
        {
            lamp.enabled = !value;
            SetTags(lamp.gameObject, value ? "lit" : "unlit", Covered ? "capped" : "uncapped");
        }
        if (filtration != null)
        {
            if (Dish != null && Dish.Grab != null) Dish.Grab.enabled = !value;
            if (Stand != null) Stand.enabled = !value && !DishMounted && !LampMounted;
        }
    }
    public bool RetryCurrentStage()
    {
        if (!Initialize() || !CanRetry || InitialMl <= 0f) return false;
        _coolingElapsed = 0f; cap.enabled = true;
        if (crystallizedContents != null) crystallizedContents.SetActive(false);
        SetLit(false); EvaporatedMl = 0f; Dish.SetVolume(InitialMl); Dish.SetDissolvedFraction(1f);
        JsonUtility.FromJsonOverwrite(_dishSemantic, Dish.GetComponent<SemanticObject>());
        Cancel(cap); Covered = true; _capMustLeave = false; _capDwell = 0f; cap.transform.SetParent(lamp.transform, true); Move(cap, capMount);
        SetLit(false); SetTags(cap.gameObject, "installed");
        _homes[1].Restore();
        Stage = IsInAssembleSegment ? HeatingStage.Assembling : HeatingStage.Ready;
        Status = "remove_lamp_cap"; Log("heating_retried"); StateChanged?.Invoke(); return true;
    }
    public void ResetExperiment()
    {
        if (!Initialize()) return;
        _coolingElapsed = 0f;
        if (crystallizedContents != null) crystallizedContents.SetActive(false);
        SetLit(false); _homes[0].Restore(); _homes[1].Restore(); Cancel(cap);
        // Filtration owns the dish and stand reset; this stage restores only its own apparatus.
        DishMounted = LampMounted = false; Covered = true;
        cap.transform.SetParent(lamp.transform, true); Move(cap, capMount);
        lamp.enabled = cap.enabled = lighter.enabled = Dish.Grab.enabled = Stand.enabled = true;
        _capMustLeave = _dishMustLeave = _lampMustLeave = false;
        _dishDwell = _lampDwell = _capDwell = _ignitionDwell = 0f;
        InitialMl = EvaporatedMl = 0f; Stage = HeatingStage.Waiting; Status = "idle";
        JsonUtility.FromJsonOverwrite(_lampSemantic, lamp.GetComponent<SemanticObject>());
        SetTags(lamp.gameObject, "unlit", "capped"); SetTags(cap.gameObject, "installed");
        StateChanged?.Invoke();
    }
    private void Notify(float dt) { _notify += dt; if (_notify < 0.2f) return; _notify = 0f; StateChanged?.Invoke(); }
    private void Log(string name) => GetComponent<ExperimentLogger>()?.LogEvent(name,
        "initialMl=" + InitialMl.ToString("F2", CultureInfo.InvariantCulture) + ";evaporatedMl=" + EvaporatedMl.ToString("F2", CultureInfo.InvariantCulture), "spirit_lamp");
    private static void SetTags(GameObject item, params string[] tags)
    {
        var s = item.GetComponent<SemanticObject>(); if (s == null) return;
        s.ConfigureDetailed(s.StableId, s.DisplayName, s.ObjectType, s.Category, s.Function, s.Color, s.Transparency,
            s.Shape, s.Materials, s.Contents, s.ContentColor, tags, s.Aliases, s.Selectable, s.Grabbable);
    }
    private static void Cancel(XRGrabInteractable item)
    { if (item.isSelected && item.interactionManager != null) item.interactionManager.CancelInteractableSelection((IXRSelectInteractable)item); }
    private static void Move(XRGrabInteractable item, Transform target)
    {
        item.transform.SetPositionAndRotation(target.position, target.rotation);
        var body = item.GetComponent<Rigidbody>(); if (body == null) return;
        body.position = item.transform.position; body.rotation = item.transform.rotation;
    }
    private sealed class Pose
    {
        private readonly XRGrabInteractable _item;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        public Transform Parent { get; }
        public Pose(Transform target) { _item = target.GetComponent<XRGrabInteractable>(); Parent = target.parent; _position = target.position; _rotation = target.rotation; }
        public void Restore()
        {
            Cancel(_item); _item.transform.SetParent(Parent, true); _item.transform.SetPositionAndRotation(_position, _rotation);
            var body = _item.GetComponent<Rigidbody>(); body.position = _position; body.rotation = _rotation;
        }
    }
}
