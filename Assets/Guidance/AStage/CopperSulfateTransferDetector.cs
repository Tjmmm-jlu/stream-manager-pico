using System;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class CopperSulfateTransferDetector : MonoBehaviour
{
    public const string ActionId = "transfer_copper_sulfate";
    public enum TransferStage { WaitingForPickup, Carrying, Completed }

    [SerializeField] private PreparationSequenceController sequence;
    [SerializeField] private ExperimentLogger logger;
    [SerializeField] private ExperimentStateController stateController;
    [SerializeField] private SemanticObject forceps;
    [SerializeField] private SemanticObject reagent;
    [SerializeField] private SemanticObject beaker;
    [SerializeField] private Transform forcepsTip;
    [SerializeField] private Transform pickupPoint;
    [SerializeField] private Transform beakerMouth;
    [SerializeField] private Transform depositPoint;
    [SerializeField] private Transform crystals;
    [SerializeField, Min(0.005f)] private float pickupRadius = 0.04f;
    [SerializeField, Min(0.005f)] private float mouthRadius = 0.045f;
    [SerializeField, Min(0.1f)] private float pickupDwell = 0.45f;
    [SerializeField, Min(0.1f)] private float transferDwell = 0.6f;

    private XRGrabInteractable _forcepsGrab;
    private XRGrabInteractable _reagentGrab;
    private PoseSnapshot[] _poses;
    private PoseSnapshot _crystalPose;
    private MaterialSnapshot _reagentState;
    private MaterialSnapshot _beakerState;
    private Collider[] _crystalColliders;
    private bool[] _colliderEnabled;
    private bool _initialized;
    private bool _wasHeld;
    private float _dwell;
    private TransferStage _stage;
#if UNITY_EDITOR
    private bool _editorHeld;
#endif

    public event Action StateChanged;
    public TransferStage Stage => _stage;
    public float Progress => Mathf.Clamp01(_dwell /
        (_stage == TransferStage.Carrying ? transferDwell : pickupDwell));
    public Transform Tip => forcepsTip;
    public Transform Pickup => pickupPoint;
    public Transform Mouth => beakerMouth;
    public Transform CrystalVisual => crystals;
    public SemanticObject Forceps => forceps;
    public SemanticObject Beaker => beaker;
    public bool IsActiveStep => sequence != null && sequence.IsRunning &&
        sequence.CurrentStep?.completionAction == ActionId;

    public void Configure(PreparationSequenceController controller,
        SemanticObject tool, SemanticObject source, SemanticObject destination,
        Transform tip, Transform pickup, Transform mouth, Transform deposit,
        Transform crystalVisual, float openingRadius)
    {
        sequence = controller;
        logger = controller.GetComponent<ExperimentLogger>();
        stateController = controller.GetComponent<ExperimentStateController>();
        forceps = tool;
        reagent = source;
        beaker = destination;
        forcepsTip = tip;
        pickupPoint = pickup;
        beakerMouth = mouth;
        depositPoint = deposit;
        crystals = crystalVisual;
        mouthRadius = openingRadius;
    }

    private void Awake()
    {
        if (!Initialize())
            Debug.LogError("[CopperTransfer] Required scene references are missing.", this);
    }

    private void OnEnable()
    {
        if (sequence != null)
        {
            sequence.Resetting -= ResetTransfer;
            sequence.Resetting += ResetTransfer;
        }
    }

    private void OnDisable()
    {
        if (sequence != null)
            sequence.Resetting -= ResetTransfer;
        if (_initialized && _stage == TransferStage.Carrying)
            CancelPickup();
        _wasHeld = false;
        _dwell = 0f;
    }

    private bool Initialize()
    {
        if (_initialized)
            return true;
        if (sequence == null || forceps == null || reagent == null || beaker == null ||
            forcepsTip == null || pickupPoint == null || beakerMouth == null ||
            depositPoint == null || crystals == null)
            return false;

        _forcepsGrab = forceps.GetComponent<XRGrabInteractable>();
        _reagentGrab = reagent.GetComponent<XRGrabInteractable>();
        if (_forcepsGrab == null)
            return false;
        _poses = new[] { new PoseSnapshot(forceps.transform),
            new PoseSnapshot(reagent.transform), new PoseSnapshot(beaker.transform) };
        _crystalPose = new PoseSnapshot(crystals);
        _reagentState = new MaterialSnapshot(reagent);
        _beakerState = new MaterialSnapshot(beaker);
        _crystalColliders = crystals.GetComponentsInChildren<Collider>(true);
        _colliderEnabled = _crystalColliders.Select(item => item.enabled).ToArray();
        _initialized = true;
        OnEnable();
        return true;
    }

    private void Update()
    {
        bool held = _forcepsGrab != null && _forcepsGrab.isSelected;
#if UNITY_EDITOR
        held |= _editorHeld;
#endif
        Tick(Time.deltaTime, held);
    }

    private void Tick(float elapsed, bool held)
    {
        if (!Initialize())
            return;
        if (!IsActiveStep || !isActiveAndEnabled ||
            stateController != null &&
            (stateController.CurrentState == ExperimentTrialState.Aborted ||
             stateController.CurrentState == ExperimentTrialState.Completed))
        {
            if (_stage == TransferStage.Carrying)
                CancelPickup();
            _wasHeld = false;
            _dwell = 0f;
            return;
        }
        held &= forceps.gameObject.activeInHierarchy;
        if (held && !_wasHeld)
        {
            logger?.LogEvent("forceps_grab_started", null, forceps.StableId);
            stateController?.MarkOperatorActing();
        }
        _wasHeld = held;
        if (!held)
        {
            if (_stage == TransferStage.Carrying)
                CancelPickup();
            _dwell = 0f;
            return;
        }

        if (_stage == TransferStage.WaitingForPickup)
        {
            bool near = reagent.gameObject.activeInHierarchy &&
                (_reagentGrab == null || !_reagentGrab.isSelected) &&
                Vector3.Distance(forcepsTip.position, pickupPoint.position) <= pickupRadius;
            _dwell = near ? _dwell + Mathf.Max(0f, elapsed) : 0f;
            if (_dwell >= pickupDwell)
                PickUp();
        }
        else if (_stage == TransferStage.Carrying)
        {
            Vector3 offset = forcepsTip.position - beakerMouth.position;
            float height = Vector3.Dot(offset, beakerMouth.up);
            float radial = Vector3.ProjectOnPlane(offset, beakerMouth.up).magnitude;
            bool inside = beaker.gameObject.activeInHierarchy &&
                Vector3.Dot(beakerMouth.up, Vector3.up) >= 0.7f &&
                radial <= mouthRadius && height >= -0.015f && height <= 0.06f;
            _dwell = inside ? _dwell + Mathf.Max(0f, elapsed) : 0f;
            if (_dwell >= transferDwell)
                CompleteTransfer();
        }
    }

    private void PickUp()
    {
        _stage = TransferStage.Carrying;
        _dwell = 0f;
        foreach (Collider collider in _crystalColliders)
            collider.enabled = false;
        crystals.SetParent(forcepsTip, true);
        crystals.localPosition = Vector3.zero;
        crystals.rotation = forcepsTip.rotation;
        MaterialSnapshot.Apply(reagent, reagent.DisplayName, reagent.Contents,
            reagent.ContentColor, reagent.StateTags.Where(tag => tag != "ready")
                .Concat(new[] { "held_by_forceps" }), reagent.Aliases);
        logger?.LogEvent("copper_sulfate_grab_confirmed", null, reagent.StableId);
        StateChanged?.Invoke();
    }

    private void CancelPickup()
    {
        _crystalPose.Restore();
        RestoreColliders();
        _reagentState.Restore(reagent);
        _stage = TransferStage.WaitingForPickup;
        _dwell = 0f;
        logger?.LogEvent("copper_sulfate_grab_cancelled", "forceps_released", reagent.StableId);
        StateChanged?.Invoke();
    }

    private void CompleteTransfer()
    {
        _stage = TransferStage.Completed;
        _dwell = 0f;
        crystals.SetParent(depositPoint, true);
        crystals.localPosition = Vector3.zero;
        crystals.rotation = depositPoint.rotation;
        MaterialSnapshot.Apply(reagent, reagent.DisplayName, reagent.Contents,
            reagent.ContentColor, _reagentState.Tags.Where(tag => tag != "ready")
                .Concat(new[] { "transferred" }), reagent.Aliases);
        MaterialSnapshot.Apply(beaker, "\u70e7\u676f", "copper_sulfate", "blue",
            _beakerState.Tags.Where(tag => tag != "empty")
                .Concat(new[] { "contains_copper_sulfate" }),
            _beakerState.Aliases.Where(alias => alias != "\u7a7a\u70e7\u676f" &&
                !alias.StartsWith("empty", StringComparison.OrdinalIgnoreCase)));
        logger?.LogEvent("copper_sulfate_transfer_completed", "source=copper_sulfate_crude;destination=beaker", beaker.StableId);
        logger?.LogEvent("sequence_step_completed", ActionId, forceps.StableId);
        StateChanged?.Invoke();
        sequence.CompleteAction(ActionId);
    }

    public void ResetTransfer()
    {
        if (!Initialize())
            return;
        foreach (PoseSnapshot pose in _poses)
        {
            XRGrabInteractable grab = pose.Target.GetComponent<XRGrabInteractable>();
            if (grab != null && grab.isSelected && grab.interactionManager != null)
                grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);
        }
        _crystalPose.Restore();
        foreach (PoseSnapshot pose in _poses)
            pose.Restore();
        RestoreColliders();
        _reagentState.Restore(reagent);
        _beakerState.Restore(beaker);
        _stage = TransferStage.WaitingForPickup;
        _dwell = 0f;
        _wasHeld = false;
#if UNITY_EDITOR
        _editorHeld = false;
#endif
        logger?.LogEvent("copper_sulfate_transfer_reset", null, reagent.StableId);
        StateChanged?.Invoke();
    }

    private void RestoreColliders()
    {
        for (int i = 0; i < _crystalColliders.Length; i++)
            if (_crystalColliders[i] != null)
                _crystalColliders[i].enabled = _colliderEnabled[i];
    }

#if UNITY_EDITOR
    public void EditorSetHeld(bool held) { _editorHeld = held; }
    public void EditorTick(float elapsed, bool held) { Tick(elapsed, held); }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        if (forcepsTip != null) Gizmos.DrawWireSphere(forcepsTip.position, 0.008f);
        Gizmos.color = Color.yellow;
        if (pickupPoint != null) Gizmos.DrawWireSphere(pickupPoint.position, pickupRadius);
        Gizmos.color = Color.green;
        if (beakerMouth != null) Gizmos.DrawWireSphere(beakerMouth.position, mouthRadius);
    }
#endif

    private sealed class PoseSnapshot
    {
        public readonly Transform Target;
        private readonly Transform _parent;
        private readonly Vector3 _position;
        private readonly Quaternion _rotation;
        private readonly Vector3 _scale;
        public PoseSnapshot(Transform target)
        {
            Target = target;
            _parent = target.parent;
            _position = target.localPosition;
            _rotation = target.localRotation;
            _scale = target.localScale;
        }
        public void Restore()
        {
            Target.SetParent(_parent, false);
            Target.localPosition = _position;
            Target.localRotation = _rotation;
            Target.localScale = _scale;
            Rigidbody body = Target.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = Target.position;
                body.rotation = Target.rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
        }
    }

    private sealed class MaterialSnapshot
    {
        private readonly string _name;
        private readonly string _contents;
        private readonly string _color;
        public readonly string[] Tags;
        public readonly string[] Aliases;
        public MaterialSnapshot(SemanticObject item)
        {
            _name = item.DisplayName;
            _contents = item.Contents;
            _color = item.ContentColor;
            Tags = item.StateTags.ToArray();
            Aliases = item.Aliases.ToArray();
        }
        public void Restore(SemanticObject item)
        {
            Apply(item, _name, _contents, _color, Tags, Aliases);
        }
        public static void Apply(SemanticObject item, string name, string contents,
            string color, System.Collections.Generic.IEnumerable<string> tags,
            System.Collections.Generic.IEnumerable<string> aliases)
        {
            item.ConfigureDetailed(item.StableId, name, item.ObjectType,
                item.Category, item.Function, item.Color, item.Transparency,
                item.Shape, item.Materials, contents, color, tags, aliases,
                item.Selectable, item.Grabbable);
        }
    }
}
