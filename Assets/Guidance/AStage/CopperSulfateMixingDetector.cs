using System;
using System.Globalization;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class CopperSulfateMixingDetector : MonoBehaviour
{
    public const string MeasureAction = "measure_water";
    public const string MixAction = "dissolve_copper_sulfate";
    public enum MixingStage { Waiting, Measuring, AddingWater, Stirring, Completed }
    [SerializeField] private PreparationSequenceController sequence;
    [SerializeField] private LiquidContainer source;
    [SerializeField] private LiquidContainer cylinder;
    [SerializeField] private LiquidContainer beaker;
    [SerializeField] private LiquidPourInteractor pour;
    [SerializeField] private XRGrabInteractable rod;
    [SerializeField] private Transform rodTip;
    [SerializeField] private Transform crystalVisual;
    [SerializeField] private GameObject residue;
    [SerializeField] private float targetMl = 100f;
    [SerializeField] private float toleranceMl = 10f;
    [SerializeField] private float settleSeconds = 0.5f;
    [SerializeField] private float stirringSeconds = 4f;
    private ExperimentLogger _logger;
    private ExperimentStateController _state;
    private Snapshot[] _initial;
    private Transform[] _crystals;
    private Vector3[] _crystalScales;
    private float _settle;
    private float _stirTime;
    private float _motionWindow;
    private Vector3 _previousTip;
    private Vector3 _windowTip;
    private bool _sampleValid;
    private bool _initialized;
    private float _notificationTime;
    private float _spillSinceLog;
    public event Action StateChanged;
    public MixingStage Stage { get; private set; }
    public string Status { get; private set; } = "idle";
    public float SpilledMl { get; private set; }
    public float Progress => Mathf.Clamp01(_stirTime / stirringSeconds);
    public float TargetMl => targetMl;
    public float ToleranceMl => toleranceMl;
    public LiquidContainer Source => source;
    public LiquidContainer Cylinder => cylinder;
    public LiquidContainer Beaker => beaker;
    public Transform RodTip => rodTip;
    public XRGrabInteractable Rod => rod;
    public GameObject Residue => residue;
    public bool CanRetry => IsAction(MeasureAction) || IsAction(MixAction);

    public void Configure(PreparationSequenceController controller, LiquidContainer water,
        LiquidContainer measuring, LiquidContainer receiving, LiquidPourInteractor pouring,
        XRGrabInteractable stirringRod, Transform tip, Transform crystals, GameObject impurities)
    {
        sequence = controller; source = water; cylinder = measuring; beaker = receiving;
        pour = pouring; rod = stirringRod; rodTip = tip; crystalVisual = crystals; residue = impurities;
    }

    private void Awake() => Initialize();
    private void OnEnable()
    {
        if (sequence == null) return;
        sequence.Resetting -= ResetExperiment;
        sequence.Resetting += ResetExperiment;
    }
    private void OnDisable()
    {
        if (sequence != null) sequence.Resetting -= ResetExperiment;
        if (pour != null) pour.Stop();
        _sampleValid = false;
    }
    private bool Initialize()
    {
        if (_initialized) return true;
        if (sequence == null || source == null || cylinder == null || beaker == null ||
            rod == null || rodTip == null || crystalVisual == null || pour == null) return false;
        _logger = GetComponent<ExperimentLogger>();
        _state = GetComponent<ExperimentStateController>();
        UpdateContents(source, "water", "colorless", new[] { "contains_water" });
        _initial = new[] { new Snapshot(source.gameObject), new Snapshot(cylinder.gameObject),
            new Snapshot(beaker.gameObject), new Snapshot(rod.gameObject) };
        _crystals = crystalVisual.Cast<Transform>().ToArray();
        _crystalScales = _crystals.Select(t => t.localScale).ToArray();
        _initialized = true;
        OnEnable();
        return true;
    }
    private bool IsAction(string action) => sequence != null && sequence.IsRunning &&
        sequence.CurrentStep?.completionAction == action && (_state == null ||
            _state.CurrentState != ExperimentTrialState.Aborted && _state.CurrentState != ExperimentTrialState.Completed);

    private void Update() => Tick(Time.deltaTime);
    public void Tick(float elapsed)
    {
        if (!Initialize() || !isActiveAndEnabled) return;
        float dt = Mathf.Clamp(elapsed, 0f, 0.1f);
        if (!IsAction(MeasureAction) && !IsAction(MixAction))
        {
            pour.Stop(); _sampleValid = false; _settle = 0f;
            if (Stage != MixingStage.Completed) Stage = MixingStage.Waiting;
            Status = "idle";
            return;
        }
        if (IsAction(MeasureAction)) Measure(dt); else Mix(dt);
        _notificationTime += dt;
        if (_notificationTime >= 0.2f) { _notificationTime = 0f; StateChanged?.Invoke(); }
    }

    private void Measure(float dt)
    {
        Stage = MixingStage.Measuring;
        bool hands = LiquidPourInteractor.OppositeHands(source.Grab, cylinder.Grab);
        bool cylinderHeld = LiquidPourInteractor.HeldByHand(cylinder.Grab);
        pour.Tick(source, cylinder, dt);
        RecordPour();
        // Reading the held cylinder must remain possible while the other hand swaps vessels.
        bool ready = cylinderHeld && cylinder.Upright && !pour.IsPouring && InRange(cylinder.VolumeMl);
        _settle = ready ? _settle + dt : 0f;
        Status = !cylinderHeld ? "hold_cylinder" : cylinder.VolumeMl > targetMl + toleranceMl ? "overfilled" :
            !cylinder.Upright ? "keep_upright" : ready ? "settling" : !hands ? "two_hands" : "measuring";
        if (_settle < settleSeconds) return;
        Log("water_measured", cylinder.VolumeMl, cylinder);
        UpdateContents(source, "water", "colorless", new[] { "contains_water" });
        UpdateContents(cylinder, "water", "colorless", new[] { "contains_water", "measured" });
        _settle = 0f;
        Stage = MixingStage.AddingWater;
        Status = LiquidPourInteractor.OppositeHands(cylinder.Grab, beaker.Grab) ? "adding_water" : "two_hands";
        sequence.CompleteAction(MeasureAction);
        StateChanged?.Invoke();
    }

    private void Mix(float dt)
    {
        if (Stage != MixingStage.Stirring)
        {
            Stage = MixingStage.AddingWater;
            bool hands = LiquidPourInteractor.OppositeHands(cylinder.Grab, beaker.Grab);
            pour.Tick(cylinder, beaker, dt);
            RecordPour();
            bool ready = hands && beaker.Upright && !pour.IsPouring && InRange(beaker.VolumeMl);
            _settle = ready ? _settle + dt : 0f;
            Status = !hands ? "two_hands" : beaker.VolumeMl > targetMl + toleranceMl ? "overfilled" : "adding_water";
            if (_settle < settleSeconds) return;
            Log("water_added", beaker.VolumeMl, beaker);
            UpdateContents(cylinder, cylinder.VolumeMl < 0.5f ? "none" : "water", "colorless",
                new[] { cylinder.VolumeMl < 0.5f ? "empty" : "contains_water" });
            UpdateContents(beaker, "copper_sulfate_water", "blue", new[] { "contains_copper_sulfate", "undissolved" });
            Stage = MixingStage.Stirring;
            Status = "stirring";
            _settle = 0f; _sampleValid = false;
            StateChanged?.Invoke();
            return;
        }
        pour.Stop();
        bool held = LiquidPourInteractor.OppositeHands(rod, beaker.Grab);
        bool immersed = held && beaker.Upright && beaker.ContainsLiquidPoint(rodTip.position);
        Status = !held ? "two_hands" : !immersed ? "immerse_rod" : "stirring";
        if (!immersed || !InRange(beaker.VolumeMl)) { _sampleValid = false; return; }
        Vector3 localTip = beaker.transform.InverseTransformPoint(rodTip.position);
        if (!_sampleValid)
        {
            _sampleValid = true; _windowTip = _previousTip = localTip; _motionWindow = 0f;
            return;
        }
        float movement = beaker.transform.TransformVector(localTip - _previousTip).magnitude;
        _previousTip = localTip;
        if (movement > 0.04f) { _sampleValid = false; return; }
        _motionWindow += dt;
        if (_motionWindow < 0.12f) return;
        Vector3 displacement = beaker.transform.TransformVector(localTip - _windowTip);
        Vector3 radial = Vector3.ProjectOnPlane(beaker.transform.TransformPoint(_windowTip) - beaker.Mouth, beaker.transform.up);
        Vector3 tangent = Vector3.Cross(beaker.transform.up, radial).normalized;
        float tangential = Mathf.Abs(Vector3.Dot(displacement, tangent));
        // Relative motion rejects moving both hands together; a 4 mm window rejects tracking tremor.
        if (radial.magnitude >= 0.008f && tangential >= 0.004f && tangential / _motionWindow < 0.7f)
        {
            _stirTime += _motionWindow;
            _state?.MarkOperatorActing();
            ApplyDissolution();
        }
        _windowTip = localTip; _motionWindow = 0f;
        if (_stirTime < stirringSeconds) return;
        Stage = MixingStage.Completed;
        Status = "dissolved";
        UpdateContents(beaker, "copper_sulfate_solution", "blue", new[] { "dissolved", "unfiltered", "contains_copper_sulfate" });
        Log("copper_sulfate_dissolved", beaker.VolumeMl, beaker);
        sequence.CompleteAction(MixAction);
        StateChanged?.Invoke();
    }

    private bool InRange(float volume) => Mathf.Abs(volume - targetMl) <= toleranceMl;
    private void RecordPour()
    {
        if (!pour.IsPouring) return;
        _state?.MarkOperatorActing();
        SpilledMl += pour.LastSpilledMl;
        _spillSinceLog += pour.LastSpilledMl;
        if (_spillSinceLog >= 1f) { Log("water_spilled", _spillSinceLog, null); _spillSinceLog = 0f; }
    }

    private void ApplyDissolution()
    {
        float fraction = Progress;
        for (int i = 0; i < _crystals.Length; i++)
            _crystals[i].localScale = _crystalScales[i] * Mathf.Pow(1f - fraction, 1f / 3f);
        beaker.SetDissolvedFraction(fraction);
        if (residue != null) residue.SetActive(fraction > 0.1f);
    }

    public bool RetryCurrentStage()
    {
        if (!Initialize() || !CanRetry) return false;
        pour.Stop();
        bool measuring = IsAction(MeasureAction);
        // Restore liquid apparatus only: the successful forceps transfer and its action gate stay intact.
        foreach (var snapshot in _initial) snapshot.RestorePose();
        source.ResetLiquid(); cylinder.ResetLiquid(); beaker.ResetLiquid();
        if (!measuring) { cylinder.SetVolume(targetMl); source.SetVolume(source.InitialVolumeMl - targetMl); }
        _stirTime = _settle = _motionWindow = 0f; _sampleValid = false;
        ApplyDissolution();
        _initial[0].RestoreSemantic(); _initial[1].RestoreSemantic();
        UpdateContents(beaker, "copper_sulfate", "blue", new[] { "contains_copper_sulfate" });
        if (!measuring) UpdateContents(cylinder, "water", "colorless", new[] { "contains_water", "measured" });
        Stage = measuring ? MixingStage.Measuring : MixingStage.AddingWater;
        Status = "two_hands";
        Log("liquid_stage_retried", 0f, null);
        StateChanged?.Invoke();
        return true;
    }

    public void ResetExperiment()
    {
        if (!Initialize()) return;
        pour.Stop();
        foreach (var snapshot in _initial) { snapshot.RestorePose(); snapshot.RestoreSemantic(); }
        source.ResetLiquid(); cylinder.ResetLiquid(); beaker.ResetLiquid();
        _stirTime = _settle = _motionWindow = _spillSinceLog = 0f;
        SpilledMl = 0f; _sampleValid = false;
        ApplyDissolution();
        Stage = MixingStage.Waiting; Status = "idle";
        StateChanged?.Invoke();
    }

    private void Log(string eventName, float amount, LiquidContainer container)
    {
        _logger?.LogEvent(eventName, "volumeMl=" + amount.ToString("F2", CultureInfo.InvariantCulture) +
            ";spilledMl=" + SpilledMl.ToString("F2", CultureInfo.InvariantCulture),
            container != null ? container.GetComponent<SemanticObject>()?.StableId : null);
    }

    private static void UpdateContents(LiquidContainer container, string contents, string color, string[] tags)
    {
        var semantic = container.GetComponent<SemanticObject>();
        if (semantic == null) return;
        semantic.ConfigureDetailed(semantic.StableId, semantic.StableId == "beaker" ? "\u70e7\u676f" : semantic.DisplayName,
            semantic.ObjectType, semantic.Category, semantic.Function, semantic.Color, semantic.Transparency,
            semantic.Shape, semantic.Materials, contents, color, tags,
            semantic.Aliases.Where(a => !a.Contains("\u7a7a") && !a.StartsWith("empty", StringComparison.OrdinalIgnoreCase)),
            semantic.Selectable, semantic.Grabbable);
    }

    private sealed class Snapshot
    {
        private readonly Transform _target;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly SemanticObject _semantic;
        private readonly string _json;
        public Snapshot(GameObject target)
        {
            _target = target.transform; _position = _target.position; _rotation = _target.rotation;
            _semantic = target.GetComponent<SemanticObject>();
            _json = _semantic != null ? JsonUtility.ToJson(_semantic) : null;
        }
        public void RestorePose()
        {
            var grab = _target.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
            _target.SetPositionAndRotation(_position, _rotation);
            var body = _target.GetComponent<Rigidbody>();
            if (body == null) return;
            body.position = _position; body.rotation = _rotation;
            if (!body.isKinematic) { body.velocity = Vector3.zero; body.angularVelocity = Vector3.zero; }
        }
        public void RestoreSemantic() { if (_semantic != null) JsonUtility.FromJsonOverwrite(_json, _semantic); }
    }
}
