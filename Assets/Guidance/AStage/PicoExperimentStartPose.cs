using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DefaultExecutionOrder(100)]
public sealed class PicoExperimentStartPose : MonoBehaviour
{
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private Transform preparationStart;
    [SerializeField] private ExperimentLogger experimentLogger;
    [SerializeField, Min(0.1f)] private float validTrackingDuration = 0.25f;
    [SerializeField, Min(1f)] private float recenterTimeout = 10f;

    public event Action StateChanged;
    public string Status { get; private set; } = "idle";
    public Transform PreparationStart => preparationStart;
    public XROrigin Origin => xrOrigin;
    private bool _pending;
    private float _validTime;
    private float _elapsed;
    private bool _startup;

    [Serializable]
    private sealed class CalibrationRecord
    {
        public string reason;
        public Vector3 originBefore;
        public Vector3 originAfter;
        public Vector3 cameraBefore;
        public Vector3 cameraAfter;
        public Vector3 startPosition;
        public float headHeight;
    }

    public void Configure(XROrigin origin, Transform start, ExperimentLogger logger)
    {
        xrOrigin = origin;
        preparationStart = start;
        experimentLogger = logger;
    }

    private void Start() => BeginRequest(true);

    [ContextMenu("Return To Preparation Start")]
    public void RequestRecenter() => BeginRequest(false);

    private void BeginRequest(bool startup)
    {
        _pending = false;
        _startup = startup;
        _validTime = 0f;
        _elapsed = 0f;
        if (!IsConfigured()) { SetStatus("unavailable"); return; }
        if (HandsBusy()) { SetStatus("hands_busy"); return; }
        _pending = true;
        SetStatus("waiting");
        experimentLogger?.LogEvent("xr_recenter_requested", startup ? "startup" : "operator");
    }

    private bool IsConfigured() => xrOrigin != null && xrOrigin.Origin != null &&
        xrOrigin.Camera != null && preparationStart != null &&
        !preparationStart.IsChildOf(xrOrigin.Origin.transform);

    private bool HandsBusy()
    {
        foreach (var interactor in xrOrigin.GetComponentsInChildren<XRBaseInteractor>(true))
            if (interactor.hasSelection) return true;
        return false;
    }

    private void LateUpdate()
    {
        if (!_pending) return;
        InputDevice head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
        bool tracked = head.isValid &&
            head.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked &&
            head.TryGetFeatureValue(CommonUsages.trackingState, out InputTrackingState state) &&
            (state & (InputTrackingState.Position | InputTrackingState.Rotation)) ==
            (InputTrackingState.Position | InputTrackingState.Rotation);
        ProcessTracking(Time.unscaledDeltaTime, tracked,
            xrOrigin != null && xrOrigin.CurrentTrackingOriginMode == TrackingOriginModeFlags.Floor);
    }

    private void ProcessTracking(float deltaTime, bool tracked, bool floorTracking)
    {
        if (!_pending) return;
        _elapsed += deltaTime;
        if (!IsConfigured()) { Finish("unavailable"); return; }
        if (HandsBusy()) { Finish("hands_busy"); return; }
        if (!_startup && _elapsed >= recenterTimeout) { Finish("tracking_unavailable"); return; }
        Transform camera = xrOrigin.Camera.transform;
        float height = xrOrigin.CameraInOriginSpaceHeight;
        Vector3 forward = Vector3.ProjectOnPlane(camera.forward, Vector3.up);
        // Wait for the tracked pose driver and floor origin, including after a tracking interruption.
        if (!tracked || !floorTracking || !Finite(camera.position) || !Finite(forward) ||
            height <= 0.1f || float.IsNaN(height) || float.IsInfinity(height) ||
            forward.sqrMagnitude < 0.01f)
        {
            _validTime = 0f;
            return;
        }
        _validTime += deltaTime;
        if (_validTime < validTrackingDuration) return;

        var record = new CalibrationRecord
        {
            reason = _startup ? "startup" : "operator",
            originBefore = xrOrigin.Origin.transform.position,
            cameraBefore = camera.position,
            startPosition = preparationStart.position,
            headHeight = height
        };
        Vector3 direction = Vector3.ProjectOnPlane(preparationStart.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.01f || !Finite(direction) || !Finite(preparationStart.position))
        {
            Finish("unavailable");
            return;
        }
        // Move only the rig. Floor tracking supplies the eye height; tracked camera/hand locals stay intact.
        xrOrigin.MatchOriginUpCameraForward(Vector3.up, direction.normalized);
        xrOrigin.MoveCameraToWorldLocation(preparationStart.position + Vector3.up * height);
        record.originAfter = xrOrigin.Origin.transform.position;
        record.cameraAfter = camera.position;
        experimentLogger?.LogEvent("xr_recenter_completed", JsonUtility.ToJson(record));
        Finish("ready");
    }

    private static bool Finite(Vector3 value) =>
        !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
        !float.IsNaN(value.y) && !float.IsInfinity(value.y) &&
        !float.IsNaN(value.z) && !float.IsInfinity(value.z);

    private void Finish(string status)
    {
        _pending = false;
        SetStatus(status);
        if (status != "ready") experimentLogger?.LogEvent("xr_recenter_failed", status);
    }

    private void SetStatus(string status)
    {
        Status = status;
        StateChanged?.Invoke();
    }

    private void OnDisable()
    {
        if (_pending) Finish("unavailable");
    }

#if UNITY_EDITOR
    public void EditorTick(float seconds, bool tracked, bool floorTracking) =>
        ProcessTracking(seconds, tracked, floorTracking);
#endif
}
