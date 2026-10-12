using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class CopperSulfateFiltrationDetector : MonoBehaviour
{
    public const string FilterAction = "filter_copper_sulfate";
    public const string TransferAction = "transfer_filtrate";
    public const string MountFunnelAction = "mount_funnel";
    public const string PlaceReceiverAction = "place_receiver";
    public const string CollectReceiverAction = "collect_receiver";
    public const string RemoveFunnelAction = "remove_funnel";
    public enum FiltrationStage { Waiting, Assembling, Filtering, CollectingFiltrate, Transferring, Completed }
    [SerializeField] private PreparationSequenceController sequence;
    [SerializeField] private CopperSulfateMixingDetector mixing;
    [SerializeField] private LiquidContainer receiver;
    [SerializeField] private LiquidContainer dish;
    [SerializeField] private XRGrabInteractable funnel;
    [SerializeField] private Transform seat;
    [SerializeField] private Transform inlet;
    [SerializeField] private float inletRadius;
    [SerializeField] private Transform trappedResidue;
    [SerializeField] private LineRenderer outletStream;
    [SerializeField] private LiquidPourInteractor pour;
    [SerializeField] private XRGrabInteractable stand;
    [SerializeField] private Transform funnelMount;
    [SerializeField] private Transform receiverMount;
    [SerializeField] private Transform filterOutlet;
    [SerializeField] private Transform guideTarget;
    [SerializeField] private GameObject receiverPlacementGuide;
    private ExperimentStateController _state;
    private ExperimentLogger _logger;
    private Pose[] _poses;
    private string[] _initialSemantics;
    private string _initialFunnelSemantic;
    private Vector3 _sourceResidueScale;
    private Vector3 _trappedResidueScale;
    private float _filterInitialMl;
    private float _transferInitialMl;
    private float _settle;
    private float _notifyTime;
    private float _spillSinceLog;
    private bool _attached;
    private bool _receiverMounted;
    private float _funnelSettle;
    private float _receiverSettle;
    private bool _receiverMustLeaveMount;
    private bool _initialized;
    public event Action StateChanged;
    public FiltrationStage Stage { get; private set; }
    public string Status { get; private set; } = "idle";
    public LiquidContainer Receiver => receiver;
    public LiquidContainer Dish => dish;
    public XRGrabInteractable Funnel => funnel;
    public Transform Seat => seat;
    public Transform Inlet => inlet;
    public Transform TrappedResidue => trappedResidue;
    public bool Attached => _attached;
    public bool ReceiverMounted => _receiverMounted;
    public XRGrabInteractable Stand => stand;
    public Transform FunnelMount => funnelMount;
    public Transform ReceiverMount => receiverMount;
    public Transform FilterOutlet => filterOutlet;
    public Transform GuideTarget => guideTarget;
    public bool StandConfigured => stand != null && funnelMount != null && receiverMount != null && filterOutlet != null && guideTarget != null;
    public float SpilledMl { get; private set; }
    public float AttemptSpilledMl { get; private set; }
    public float InitialMl => Stage == FiltrationStage.Transferring || Stage == FiltrationStage.Completed ? _transferInitialMl : _filterInitialMl;
    public float MinimumMl => InitialMl * 0.9f;
    public float Progress => InitialMl > 0f ? Mathf.Clamp01((Stage == FiltrationStage.Transferring ||
        Stage == FiltrationStage.Completed ? dish.VolumeMl : receiver.VolumeMl) / InitialMl) : 0f;
    public bool CanRetry => IsAction(FilterAction) || IsAction(TransferAction);

    public void Configure(PreparationSequenceController controller, CopperSulfateMixingDetector solution,
        LiquidContainer receiving, LiquidContainer evaporating, XRGrabInteractable filter,
        Transform mountingPoint, Transform opening, float openingRadius, Transform residue,
        LineRenderer outlet, LiquidPourInteractor pouring)
    {
        sequence = controller; mixing = solution; receiver = receiving; dish = evaporating; funnel = filter;
        seat = mountingPoint; inlet = opening; inletRadius = openingRadius; trappedResidue = residue;
        outletStream = outlet; pour = pouring;
    }

    public void ConfigureStand(XRGrabInteractable support, Transform holder, Transform receivingPosition,
        Transform nozzle, Transform rodTarget, GameObject placementGuide)
    {
        stand = support; funnelMount = holder; receiverMount = receivingPosition;
        filterOutlet = nozzle; guideTarget = rodTarget; receiverPlacementGuide = placementGuide;
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
        StopStreams();
    }
    private bool Initialize()
    {
        if (_initialized) return true;
        if (sequence == null || mixing == null || receiver == null || dish == null || funnel == null ||
            seat == null || inlet == null || trappedResidue == null || pour == null) return false;
        _state = GetComponent<ExperimentStateController>(); _logger = GetComponent<ExperimentLogger>();
        _poses = new[] { new Pose(mixing.Beaker.transform), new Pose(receiver.transform), new Pose(dish.transform), new Pose(funnel.transform),
            stand != null ? new Pose(stand.transform) : null, new Pose(mixing.Rod.transform) };
        _initialSemantics = new[] { receiver, dish }.Select(c => JsonUtility.ToJson(c.GetComponent<SemanticObject>())).ToArray();
        _initialFunnelSemantic = JsonUtility.ToJson(funnel.GetComponent<SemanticObject>());
        _sourceResidueScale = mixing.Residue.transform.localScale; _trappedResidueScale = trappedResidue.localScale;
        _initialized = true; OnEnable(); return true;
    }
    private bool IsAction(string action) => sequence != null && sequence.IsRunning && sequence.CurrentStep?.completionAction == action &&
        (_state == null || _state.CurrentState != ExperimentTrialState.Aborted && _state.CurrentState != ExperimentTrialState.Completed);
    private void Update() => Tick(Time.deltaTime);
    public void Tick(float elapsed)
    {
        if (!Initialize() || !isActiveAndEnabled) return;
        UpdateDetachedApparatus();
        bool inFilterFamily = IsAction(MountFunnelAction) ||
            IsAction(PlaceReceiverAction) || IsAction(FilterAction) ||
            IsAction(CollectReceiverAction);
        bool ownsStep = inFilterFamily || IsAction(TransferAction) ||
            IsAction(RemoveFunnelAction);
        if (!ownsStep) { StopStreams(); return; }
        if (!StandConfigured) { StopStreams(); Status = "stand_not_configured"; return; }
        float dt = Mathf.Clamp(elapsed, 0f, 0.1f);
        if (inFilterFamily) Filter(dt);
        else if (IsAction(TransferAction)) Transfer(dt);
        else
        {
            // 元步骤 E1（取下漏斗）：由 UpdateDetachedApparatus 在取下时完成。
            Status = _attached ? "remove_filter_funnel" : "filter_funnel_removed";
            StopStreams();
        }
        _notifyTime += dt;
        if (_notifyTime >= 0.2f) { _notifyTime = 0f; StateChanged?.Invoke(); }
    }

    private void Filter(float dt)
    {
        if (Stage == FiltrationStage.Waiting)
        {
            if (mixing.Stage != CopperSulfateMixingDetector.MixingStage.Completed || mixing.Beaker.VolumeMl <= 0f)
            { Status = "solution_not_ready"; return; }
            _filterInitialMl = mixing.Beaker.VolumeMl;
            Stage = FiltrationStage.Assembling;
            Log("filtration_started");
        }
        StopStreams();
        // 元步骤：目标状态已提前达成时直接完成（避免等待不再发生的重复事件）。
        if (IsAction(MountFunnelAction) && _attached)
        { sequence.CompleteAction(MountFunnelAction); return; }
        if (IsAction(PlaceReceiverAction) && _receiverMounted)
        { sequence.CompleteAction(PlaceReceiverAction); return; }
        if (Stage == FiltrationStage.Assembling) { Assemble(dt); return; }
        if (Stage == FiltrationStage.CollectingFiltrate)
        {
            Status = "collect_receiver";
            if (!LiquidPourInteractor.HeldByHand(receiver.Grab) ||
                Vector3.Distance(receiver.transform.TransformPoint(receiver.Bottom), receiverMount.position) < 0.08f) return;
            _transferInitialMl = receiver.VolumeMl;
            stand.enabled = true;
            Stage = FiltrationStage.Transferring; Status = "transfer_two_hands";
            AttemptSpilledMl = 0f;
            Log("filtrate_collected");
            sequence.CompleteAction(CollectReceiverAction);
            sequence.CompleteAction(FilterAction);
            StateChanged?.Invoke(); return;
        }
        if (!_attached || !_receiverMounted) { Assemble(dt); return; }
        bool assembled = _attached && _receiverMounted && !stand.isSelected && receiver.Upright;
        pour.TickGuidedIntoOpening(mixing.Beaker, receiver, mixing.Rod, mixing.RodTip,
            inlet, funnel.transform, filterOutlet, assembled, dt);
        RecordPour();
        if (pour.LastReceivedMl > 0f)
        {
            receiver.SetDissolvedFraction(1f);
            UpdateContents(receiver, "copper_sulfate_solution", new[] { "dissolved", "filtered", "contains_copper_sulfate" });
            UpdateContents(funnel.GetComponent<SemanticObject>(), "insoluble_residue", new[] { "filter_paper", "contains_residue" });
            trappedResidue.gameObject.SetActive(true);
            trappedResidue.localScale = _trappedResidueScale * Mathf.Pow(receiver.VolumeMl / _filterInitialMl, 1f / 3f);
            if (outletStream != null)
            {
                outletStream.enabled = true; outletStream.SetPosition(0, filterOutlet.position);
                outletStream.SetPosition(1, new Vector3(filterOutlet.position.x, receiver.SurfaceWorldY, filterOutlet.position.z));
            }
        }
        mixing.Residue.transform.localScale = _sourceResidueScale * Mathf.Pow(mixing.Beaker.VolumeMl / _filterInitialMl, 1f / 3f);
        bool enough = receiver.VolumeMl + 0.01f >= MinimumMl;
        bool ready = enough && assembled && !pour.IsPouring && (mixing.Beaker.Upright || mixing.Beaker.VolumeMl <= 0.001f);
        _settle = ready ? _settle + dt : 0f;
        Status = mixing.Beaker.VolumeMl + receiver.VolumeMl + 0.01f < MinimumMl ? "filtrate_lost" :
            !assembled ? "install_apparatus" :
            !LiquidPourInteractor.OppositeHands(mixing.Beaker.Grab, mixing.Rod) ? "hold_beaker_and_rod" :
            !pour.LastGuideValid ? "align_glass_rod" : pour.LastSpilledMl > 0f ? "filter_missed" : "filtering";
        if (_settle < 0.5f) return;
        StopStreams(); _settle = 0f; Stage = FiltrationStage.CollectingFiltrate; Status = "collect_receiver";
        funnel.enabled = true; receiver.Grab.enabled = true;
        if (mixing.Beaker.VolumeMl < 0.01f) UpdateContents(mixing.Beaker, "none", new[] { "empty" });
        Log("filtration_completed");
        sequence.CompleteAction(FilterAction);
        StateChanged?.Invoke();
    }

    private void Assemble(float dt)
    {
        if (stand.isSelected || Vector3.Dot(stand.transform.up, Vector3.up) < 0.98f)
        { _funnelSettle = _receiverSettle = 0f; Status = "stand_not_stationary"; return; }
        if (!_attached)
        {
            bool aligned = LiquidPourInteractor.HeldByHand(funnel) && Vector3.Distance(seat.position, funnelMount.position) <= 0.06f &&
                Vector3.Dot(funnel.transform.up, funnelMount.up) >= 0.94f;
            _funnelSettle = aligned ? _funnelSettle + dt : 0f;
            if (_funnelSettle >= 0.25f)
            {
                Snap(funnel, funnel.transform.InverseTransformPoint(seat.position), funnelMount);
                _attached = true; stand.enabled = false; Log("funnel_mounted_on_stand"); StateChanged?.Invoke();
            }
        }
        if (!_receiverMounted)
        {
            bool held = LiquidPourInteractor.HeldByHand(receiver.Grab);
            float distance = Vector3.Distance(receiver.transform.TransformPoint(receiver.Bottom), receiverMount.position);
            if (!held || distance > 0.07f) _receiverMustLeaveMount = false;
            bool aligned = !_receiverMustLeaveMount && held && receiver.Upright && distance <= 0.05f;
            _receiverSettle = aligned ? _receiverSettle + dt : 0f;
            if (_receiverSettle >= 0.25f)
            {
                Snap(receiver.Grab, receiver.Bottom, receiverMount, false);
                _receiverMounted = true; stand.enabled = false;
                if (receiverPlacementGuide != null) receiverPlacementGuide.SetActive(false);
                Log("receiver_placed_on_stand"); StateChanged?.Invoke();
            }
        }
        Status = !_attached ? "mount_funnel_on_stand" : !_receiverMounted ? "place_receiver_under_funnel" : "hold_beaker_and_rod";
        if (!_attached || !_receiverMounted) return;
        _settle = _funnelSettle = _receiverSettle = 0f; Stage = FiltrationStage.Filtering;
        if (receiver.VolumeMl < 0.01f) UpdateContents(receiver, "none", new[] { "empty", "filter_assembled" });
        Log("filter_assembled"); StateChanged?.Invoke();
    }

    private void Snap(XRGrabInteractable grab, Vector3 localPoint, Transform mount, bool lockGrab = true)
    {
        CancelSelection(grab); grab.enabled = !lockGrab;
        grab.transform.SetParent(stand.transform, true); grab.transform.rotation = mount.rotation;
        grab.transform.position += mount.position - grab.transform.TransformPoint(localPoint);
        var body = grab.GetComponent<Rigidbody>();
        if (body != null) { body.position = grab.transform.position; body.rotation = grab.transform.rotation; }
    }

    private void UpdateDetachedApparatus()
    {
        if (_attached && funnel.isSelected)
        {
            funnel.transform.SetParent(_poses[3].Parent, true); _attached = false;
            sequence.CompleteAction(RemoveFunnelAction);
        }
        if (_receiverMounted && receiver.Grab.isSelected)
        {
            receiver.transform.SetParent(_poses[1].Parent, true); _receiverMounted = false;
            _receiverMustLeaveMount = true; _receiverSettle = _settle = 0f;
            StopStreams();
            if (receiverPlacementGuide != null) receiverPlacementGuide.SetActive(true);
        }
    }

    private void Transfer(float dt)
    {
        Stage = FiltrationStage.Transferring;
        if (_transferInitialMl <= 0f || _receiverMounted) { StopStreams(); Status = "collect_receiver"; return; }
        pour.Tick(receiver, dish, dt); RecordPour();
        if (pour.LastReceivedMl > 0f)
        {
            dish.SetDissolvedFraction(1f);
            UpdateContents(dish, "copper_sulfate_solution", new[] { "dissolved", "filtered", "contains_copper_sulfate" });
        }
        bool ready = dish.VolumeMl + 0.01f >= MinimumMl && LiquidPourInteractor.HeldByHand(dish.Grab) && dish.Upright && !pour.IsPouring;
        _settle = ready ? _settle + dt : 0f;
        Status = receiver.VolumeMl + dish.VolumeMl + 0.01f < MinimumMl ? "filtrate_lost" :
            !LiquidPourInteractor.OppositeHands(receiver.Grab, dish.Grab) ? "transfer_two_hands" :
            !dish.Upright ? "dish_upright" : pour.LastSpilledMl > 0f ? "transfer_missed" : "transferring";
        if (_settle < 0.5f) return;
        StopStreams(); Stage = FiltrationStage.Completed; Status = "filtrate_transferred";
        if (receiverPlacementGuide != null) receiverPlacementGuide.SetActive(false);
        UpdateContents(receiver, receiver.VolumeMl < 0.01f ? "none" : "copper_sulfate_solution",
            receiver.VolumeMl < 0.01f ? new[] { "empty" } : new[] { "filtered", "contains_copper_sulfate" });
        Log("filtrate_transferred"); sequence.CompleteAction(TransferAction); StateChanged?.Invoke();
    }

    private void RecordPour()
    {
        if (!pour.IsPouring) return;
        _state?.MarkOperatorActing(); SpilledMl += pour.LastSpilledMl; AttemptSpilledMl += pour.LastSpilledMl;
        _spillSinceLog += pour.LastSpilledMl;
        if (_spillSinceLog >= 1f) { Log("filtrate_spilled"); _spillSinceLog = 0f; }
    }
    private void StopStreams()
    {
        if (pour != null) pour.Stop();
        if (outletStream != null) outletStream.enabled = false;
    }
    public bool RetryCurrentStage()
    {
        if (!Initialize() || !CanRetry) return false;
        if (IsAction(FilterAction) && _filterInitialMl <= 0f) return false;
        StopStreams(); _settle = _spillSinceLog = 0f; AttemptSpilledMl = 0f;
        if (IsAction(FilterAction))
        {
            foreach (var pose in _poses) pose?.Restore();
            ResetAssembly();
            mixing.Beaker.SetVolume(_filterInitialMl); mixing.Beaker.SetDissolvedFraction(1f);
            UpdateContents(mixing.Beaker, "copper_sulfate_solution", new[] { "dissolved", "unfiltered", "contains_copper_sulfate" });
            mixing.Residue.transform.localScale = _sourceResidueScale; mixing.Residue.SetActive(true);
            trappedResidue.gameObject.SetActive(false); receiver.ResetLiquid(); dish.ResetLiquid();
            RestoreSemantics(); Stage = FiltrationStage.Assembling; Status = "mount_funnel_on_stand";
        }
        else
        {
            _poses[1].Restore(); _poses[2].Restore(); _receiverMounted = false; receiver.Grab.enabled = true;
            receiver.SetVolume(_transferInitialMl); receiver.SetDissolvedFraction(1f); dish.ResetLiquid();
            JsonUtility.FromJsonOverwrite(_initialSemantics[1], dish.GetComponent<SemanticObject>());
            UpdateContents(receiver, "copper_sulfate_solution", new[] { "dissolved", "filtered", "contains_copper_sulfate" });
            Stage = FiltrationStage.Transferring; Status = "transfer_two_hands";
        }
        Log("filtration_stage_retried"); StateChanged?.Invoke(); return true;
    }
    public void ResetExperiment()
    {
        if (!Initialize()) return;
        StopStreams();
        // Mixing owns the source beaker reset; filtration owns its mounted apparatus and residue scale.
        for (int i = 1; i < _poses.Length; i++) _poses[i]?.Restore();
        ResetAssembly(); receiver.ResetLiquid(); dish.ResetLiquid(); RestoreSemantics();
        mixing.Residue.transform.localScale = _sourceResidueScale;
        trappedResidue.gameObject.SetActive(false); trappedResidue.localScale = _trappedResidueScale;
        _filterInitialMl = _transferInitialMl = _settle = _spillSinceLog = 0f;
        SpilledMl = AttemptSpilledMl = 0f; Stage = FiltrationStage.Waiting; Status = "idle"; StateChanged?.Invoke();
    }
    private void ResetAssembly()
    {
        _attached = _receiverMounted = false; _funnelSettle = _receiverSettle = 0f;
        _receiverMustLeaveMount = false;
        funnel.enabled = true; receiver.Grab.enabled = true;
        if (stand != null) stand.enabled = true;
        if (receiverPlacementGuide != null) receiverPlacementGuide.SetActive(true);
    }
    private void RestoreSemantics()
    {
        JsonUtility.FromJsonOverwrite(_initialSemantics[0], receiver.GetComponent<SemanticObject>());
        JsonUtility.FromJsonOverwrite(_initialSemantics[1], dish.GetComponent<SemanticObject>());
        JsonUtility.FromJsonOverwrite(_initialFunnelSemantic, funnel.GetComponent<SemanticObject>());
    }
    private void Log(string eventName) => _logger?.LogEvent(eventName,
        "initialMl=" + InitialMl.ToString("F2", CultureInfo.InvariantCulture) +
        ";receivedMl=" + receiver.VolumeMl.ToString("F2", CultureInfo.InvariantCulture) +
        ";dishMl=" + dish.VolumeMl.ToString("F2", CultureInfo.InvariantCulture) +
        ";spilledMl=" + SpilledMl.ToString("F2", CultureInfo.InvariantCulture), "filtrate_beaker");
    private static void UpdateContents(LiquidContainer container, string contents, string[] tags)
        => UpdateContents(container.GetComponent<SemanticObject>(), contents, tags);
    private static void UpdateContents(SemanticObject semantic, string contents, string[] tags)
    {
        if (semantic == null) return;
        semantic.ConfigureDetailed(semantic.StableId, semantic.DisplayName, semantic.ObjectType, semantic.Category,
            semantic.Function, semantic.Color, semantic.Transparency, semantic.Shape, semantic.Materials,
            contents, contents == "none" ? "none" : contents == "insoluble_residue" ? "gray" : "blue",
            tags, semantic.Aliases, semantic.Selectable, semantic.Grabbable);
    }
    private static void CancelSelection(XRGrabInteractable grab)
    {
        if (grab != null && grab.isSelected && grab.interactionManager != null)
            grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
    }
    private sealed class Pose
    {
        private readonly Transform _target;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;
        public Transform Parent { get; }
        public Pose(Transform target)
        {
            _target = target; Parent = target.parent; _position = target.position; _rotation = target.rotation; _scale = target.localScale;
        }
        public void Restore()
        {
            CancelSelection(_target.GetComponent<XRGrabInteractable>());
            _target.SetParent(Parent, true); _target.SetPositionAndRotation(_position, _rotation); _target.localScale = _scale;
            var body = _target.GetComponent<Rigidbody>();
            if (body == null) return;
            body.position = _position; body.rotation = _rotation;
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
    }
}
