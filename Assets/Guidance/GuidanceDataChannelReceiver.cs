using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.RenderStreaming;
using Unity.WebRTC;
using UnityEngine;

public sealed class GuidanceDataChannelReceiver : DataChannelBase
{
    [Serializable]
    private sealed class GuidanceCommand
    {
        public string type;
        public string mode;
        public string density;
        public string trialId;
        public string condition;
        public string targetId;
        public string query;
        public string category;
        public string color;
        public string[] candidateIds;
        public int stepIndex;
        public int seed;
    }

    [Serializable]
    private sealed class CandidateVisualSummary
    {
        public string id;
        public string displayName;
        public bool hasBounds;
        public bool visible;
        public float xMin;
        public float yMin;
        public float xMax;
        public float yMax;
        public float depth;
    }

    [Serializable]
    private sealed class PlacementZoneVisualSummary
    {
        public string id;
        public string objectId;
        public string displayName;
        public bool hasBounds;
        public bool visible;
        public float xMin;
        public float yMin;
        public float xMax;
        public float yMax;
        public float depth;
    }

    [Serializable]
    private sealed class GuidanceResponse
    {
        public string type;
        public bool ok;
        public string mode;
        public string density;
        public string trialId;
        public string state;
        public string targetId;
        public string targetName;
        public string message;
        public int candidateCount;
        public SemanticObjectSummary[] candidates;
        public CandidateVisualSummary[] candidateVisuals;
        public PlacementZoneVisualSummary[] placementZoneVisuals;
        public int stepIndex;
        public int stepCount;
        public string instruction;
        public bool sequenceComplete;
        public int generatedCount;
        public int registryCount;
        public double unityTime;
        public string mixingStage;
        public float cylinderMl;
        public float beakerMl;
        public float sourceMl;
        public float spilledMl;
        public float mixingProgress;
        public float targetMl;
        public float toleranceMl;
        public bool canRetryLiquid;
        public string filtrationStage;
        public float filtrateMl;
        public float dishMl;
        public float filtrationInitialMl;
        public float filtrationMinimumMl;
        public float filtrationSpilledMl;
    }

    [Header("Existing guidance")]
    [SerializeField] private GuidanceManager guidanceManager;
    [SerializeField] private PreparationSequenceController preparationSequence;

    [Header("A-stage experiment foundation")]
    [SerializeField] private SceneObjectRegistry objectRegistry;
    [SerializeField] private ExperimentStateController stateController;
    [SerializeField] private ExperimentLogger experimentLogger;
    [SerializeField] private ExperimentDistractorLayoutGenerator layoutGenerator;
    [SerializeField] private ExperimentInteractionTracker interactionTracker;
    private CopperSulfateTransferDetector _copperTransfer;
    private CopperSulfateMixingDetector _copperMixing;
    private CopperSulfateFiltrationDetector _copperFiltration;
    private CopperSulfateFiltrationDetector.FiltrationStage _lastFiltrationStage;
    private CopperSulfateMixingDetector.MixingStage _lastMixingStage;
    [SerializeField] private PicoExperimentStartPose experimentStartPose;

    [Header("PC candidate visualization")]
    [SerializeField] private Camera renderStreamingCamera;
    [SerializeField, Min(0.05f)] private float candidateVisualInterval = 0.1f;

    private string _activeTrialId = string.Empty;
    private bool _readySent;
    private readonly List<SemanticObject> _previewCandidates =
        new List<SemanticObject>();
    private float _nextCandidateVisualTime;

    public GuidanceManager GuidanceManager
    {
        get => guidanceManager;
        set
        {
            Unsubscribe();
            guidanceManager = value;
            Subscribe();
        }
    }

    public PreparationSequenceController PreparationSequence
    {
        get => preparationSequence;
        set
        {
            Unsubscribe();
            preparationSequence = value;
            Subscribe();
        }
    }

    public SceneObjectRegistry ObjectRegistry
    {
        get => objectRegistry;
        set => objectRegistry = value;
    }

    public ExperimentStateController StateController
    {
        get => stateController;
        set => stateController = value;
    }

    public ExperimentLogger ExperimentLogger
    {
        get => experimentLogger;
        set => experimentLogger = value;
    }

    private void Reset()
    {
        local = false;
        label = "guidance";
    }

    private void Awake()
    {
        local = false;
        label = "guidance";
        guidanceManager = guidanceManager != null
            ? guidanceManager
            : GetComponent<GuidanceManager>();
        preparationSequence = preparationSequence != null
            ? preparationSequence
            : GetComponent<PreparationSequenceController>();
        objectRegistry = objectRegistry != null
            ? objectRegistry
            : GetComponent<SceneObjectRegistry>();
        stateController = stateController != null
            ? stateController
            : GetComponent<ExperimentStateController>();
        experimentLogger = experimentLogger != null
            ? experimentLogger
            : GetComponent<ExperimentLogger>();
        layoutGenerator = layoutGenerator != null
            ? layoutGenerator
            : FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        interactionTracker = interactionTracker != null
            ? interactionTracker
            : GetComponent<ExperimentInteractionTracker>();
        _copperTransfer = GetComponent<CopperSulfateTransferDetector>();
        _copperMixing = GetComponent<CopperSulfateMixingDetector>();
        _copperFiltration = GetComponent<CopperSulfateFiltrationDetector>();
        renderStreamingCamera = ResolveRenderStreamingCamera();
        objectRegistry?.Refresh();
    }

    private void Update()
    {
        if (!IsConnected || _previewCandidates.Count == 0 ||
            Time.unscaledTime < _nextCandidateVisualTime)
        {
            return;
        }

        _nextCandidateVisualTime =
            Time.unscaledTime + Mathf.Max(0.05f, candidateVisualInterval);
        SendCandidateVisuals(null);
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public override void SetChannel(string connectionId, RTCDataChannel channel)
    {
        _readySent = false;
        base.SetChannel(connectionId, channel);
        if (channel != null && IsConnected)
        {
            NotifyChannelReady(connectionId);
        }
    }

    protected override void OnOpen(string connectionId)
    {
        base.OnOpen(connectionId);
        NotifyChannelReady(connectionId);
    }

    protected override void OnClose(string connectionId)
    {
        base.OnClose(connectionId);
        _readySent = false;
        _previewCandidates.Clear();
        experimentLogger?.LogEvent("guidance_channel_closed", connectionId);
        Debug.Log($"[Guidance] DataChannel closed. connection={connectionId}");
    }

    protected override void OnMessage(byte[] bytes)
    {
        string json = Encoding.UTF8.GetString(bytes);
        GuidanceCommand command;
        try
        {
            command = JsonUtility.FromJson<GuidanceCommand>(json);
        }
        catch (Exception exception)
        {
            SendError(null, $"Malformed JSON: {exception.Message}");
            return;
        }

        if (command == null || string.IsNullOrWhiteSpace(command.type))
        {
            SendError(command, "Command type is missing.");
            return;
        }

        if (guidanceManager == null)
        {
            SendError(command, "GuidanceManager is missing.");
            return;
        }

        experimentLogger?.LogEvent("web_command_received", json);
        switch (command.type.Trim().ToLowerInvariant())
        {
            case "set_guidance_mode":
                ApplyMode(command, false);
                break;
            case "set_layout_density":
            case "set_scene_density":
                ApplyLayoutDensity(command);
                break;
            case "start_trial":
                StartTrial(command);
                break;
            case "query_objects":
                QueryObjects(command);
                break;
            case "get_object_catalog":
                SendObjectCatalog(command);
                break;
            case "preview_candidates":
                SetCandidatePreview(command);
                break;
            case "clear_candidate_preview":
                ClearCandidatePreview(command);
                break;
            case "select_target":
            case "confirm_target":
            case "show_target":
                SelectTarget(command);
                break;
            case "clear_guidance":
            case "clear_target":
                _previewCandidates.Clear();
                guidanceManager.ClearTarget();
                SendResponse(
                    "guidance_cleared", true, command,
                    null, null, "Guidance target cleared.");
                break;
            case "complete_trial":
                if (_copperTransfer != null && preparationSequence != null &&
                    preparationSequence.IsRunning &&
                    preparationSequence.HasPendingActions)
                {
                    SendSequenceError(command, "Complete the copper sulfate transfer before ending the trial.");
                    break;
                }
                _previewCandidates.Clear();
                stateController?.CompleteTrial();
                guidanceManager.ClearTarget();
                experimentLogger?.LogEvent("trial_completed");
                SendResponse(
                    "trial_completed", true, command,
                    null, null, "Trial completed.");
                break;
            case "abort_trial":
                if (_copperTransfer != null)
                    preparationSequence?.ResetSequence();
                _previewCandidates.Clear();
                stateController?.AbortTrial();
                guidanceManager.ClearTarget();
                experimentLogger?.LogEvent("trial_aborted");
                SendResponse(
                    "trial_aborted", true, command,
                    null, null, "Trial aborted.");
                _activeTrialId = string.Empty;
                break;
            case "ping":
                SendResponse(
                    "guidance_pong", true, command,
                    null, null, "Unity guidance channel is alive.");
                break;
            case "recenter_experiment":
                if (experimentStartPose == null)
                    SendResponse("recenter_state", false, command, null, null, "unavailable");
                else
                    experimentStartPose.RequestRecenter();
                break;
            case "get_recenter_state":
                OnRecenterStateChanged();
                break;
            case "start_sequence":
                StartSequence(command);
                break;
            case "retry_liquid_stage":
                if (_copperFiltration != null && _copperFiltration.CanRetry)
                {
                    if (!_copperFiltration.RetryCurrentStage()) SendSequenceError(command, "The filtration stage is not ready.");
                }
                else if (_copperMixing == null || !_copperMixing.RetryCurrentStage())
                    SendSequenceError(command, "The liquid stage is not active.");
                break;
            case "next_step":
            case "complete_step":
                ExecuteSequenceCommand(
                    preparationSequence != null && preparationSequence.NextStep(),
                    command,
                    "The current step requires its experiment action to be completed.");
                break;
            case "previous_step":
                ExecuteSequenceCommand(
                    preparationSequence != null && preparationSequence.PreviousStep(),
                    command,
                    "Could not move to the previous preparation step.");
                break;
            case "show_step":
                ExecuteSequenceCommand(
                    preparationSequence != null &&
                    preparationSequence.ShowStep(command.stepIndex),
                    command,
                    $"Invalid preparation step index: {command.stepIndex}");
                break;
            case "complete_sequence":
                if (preparationSequence == null)
                {
                    SendSequenceError(command, "PreparationSequenceController is missing.");
                }
                else
                {
                    ExecuteSequenceCommand(preparationSequence.CompleteSequence(),
                        command, "Complete all required experiment actions before ending the sequence.");
                }
                break;
            case "reset_sequence":
                if (preparationSequence == null)
                {
                    SendSequenceError(command, "PreparationSequenceController is missing.");
                }
                else
                {
                    _previewCandidates.Clear();
                    preparationSequence.ResetSequence();
                    stateController?.ResetTrial();
                    _activeTrialId = string.Empty;
                }
                break;
            default:
                SendError(command, $"Unsupported command type: {command.type}");
                break;
        }
    }

    private void NotifyChannelReady(string connectionId)
    {
        if (_readySent)
        {
            return;
        }

        _readySent = true;
        experimentLogger?.LogEvent("guidance_channel_opened", connectionId);
        SendResponse(
            "guidance_ready",
            guidanceManager != null && objectRegistry != null,
            null,
            null,
            null,
            objectRegistry != null
                ? $"Unity guidance controller is ready with {objectRegistry.Count} objects."
                : "SceneObjectRegistry is missing.");
        Debug.Log(
            $"[Guidance] DataChannel opened. connection={connectionId} label={Label}");
        OnRecenterStateChanged();
        OnMixingStateChanged();
        OnFiltrationStateChanged();
    }

    private void StartTrial(GuidanceCommand command)
    {
        _activeTrialId = string.IsNullOrWhiteSpace(command.trialId)
            ? $"trial-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}"
            : command.trialId.Trim();
        guidanceManager.ClearTarget();
        stateController?.StartTrial(_activeTrialId, command.condition);
        experimentLogger?.LogEvent("trial_started");

        if (string.IsNullOrWhiteSpace(command.mode))
        {
            command.mode = "highlight";
        }
        ApplyMode(command, true);
    }

    private void QueryObjects(GuidanceCommand command)
    {
        if (!EnsureRegistry(command))
        {
            return;
        }
        EnsureTrial(command);
        stateController?.BeginCandidateGeneration();
        List<SemanticObject> candidates = objectRegistry.FindCandidates(
            command.query,
            command.category,
            command.color,
            true);
        stateController?.SetCandidates(candidates);
        experimentLogger?.LogEvent(
            "candidate_query_completed",
            $"query={command.query};category={command.category};" +
            $"color={command.color};count={candidates.Count}");
        SendResponse(
            "object_candidates",
            true,
            command,
            null,
            null,
            $"Found {candidates.Count} candidate objects.",
            candidates.Select(SemanticObjectSummary.From).ToArray());
    }

    private void SendObjectCatalog(GuidanceCommand command)
    {
        if (!EnsureRegistry(command))
        {
            return;
        }
        objectRegistry.Refresh();
        SendResponse(
            "object_catalog",
            true,
            command,
            null,
            null,
            $"Scene catalog contains {objectRegistry.Count} objects.",
            objectRegistry.GetCatalog());
    }

    private void SetCandidatePreview(GuidanceCommand command)
    {
        if (!EnsureRegistry(command))
        {
            return;
        }

        _previewCandidates.Clear();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string candidateId in command.candidateIds ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(candidateId) ||
                !seenIds.Add(candidateId.Trim()) ||
                !objectRegistry.TryGet(candidateId, out SemanticObject candidate) ||
                candidate == null || !candidate.Selectable)
            {
                continue;
            }
            _previewCandidates.Add(candidate);
        }

        _nextCandidateVisualTime = 0f;
        experimentLogger?.LogEvent(
            "candidate_preview_set",
            string.Join(",", _previewCandidates.Select(item => item.StableId)));
        SendCandidateVisuals(command);
    }

    private void ClearCandidatePreview(GuidanceCommand command)
    {
        _previewCandidates.Clear();
        experimentLogger?.LogEvent("candidate_preview_cleared");
        SendCandidateVisuals(command);
    }

    private void SendCandidateVisuals(GuidanceCommand command)
    {
        renderStreamingCamera = ResolveRenderStreamingCamera();
        CandidateVisualSummary[] visuals = BuildCandidateVisuals();
        PlacementZoneVisualSummary[] placementZoneVisuals =
            BuildPlacementZoneVisuals(_previewCandidates);
        bool hasCamera = renderStreamingCamera != null;
        SendResponse(
            "candidate_visuals",
            hasCamera,
            command,
            null,
            null,
            hasCamera
                ? $"Projected {visuals.Length} candidates into the PC stream."
                : "Render streaming camera is missing.",
            candidateVisuals: visuals,
            placementZoneVisuals: placementZoneVisuals);
    }

    private CandidateVisualSummary[] BuildCandidateVisuals()
    {
        if (renderStreamingCamera == null)
        {
            return Array.Empty<CandidateVisualSummary>();
        }

        Plane[] frustumPlanes =
            GeometryUtility.CalculateFrustumPlanes(renderStreamingCamera);
        return _previewCandidates
            .Where(candidate => candidate != null)
            .Select(candidate => ProjectCandidate(candidate, frustumPlanes))
            .ToArray();
    }

    private CandidateVisualSummary ProjectCandidate(
        SemanticObject candidate,
        Plane[] frustumPlanes)
    {
        var visual = new CandidateVisualSummary
        {
            id = candidate.StableId,
            displayName = candidate.DisplayName
        };
        if (!candidate.gameObject.activeInHierarchy ||
            !TryGetWorldBounds(candidate, out Bounds bounds))
        {
            return visual;
        }

        visual.hasBounds = true;
        if (!TryProjectBounds(
                bounds,
                frustumPlanes,
                out float xMin,
                out float yMin,
                out float xMax,
                out float yMax,
                out float depth))
        {
            return visual;
        }

        visual.depth = depth;
        visual.visible = true;
        visual.xMin = Mathf.Clamp01(xMin);
        visual.yMin = Mathf.Clamp01(yMin);
        visual.xMax = Mathf.Clamp01(xMax);
        visual.yMax = Mathf.Clamp01(yMax);
        return visual;
    }

    private PlacementZoneVisualSummary[] BuildPlacementZoneVisuals(
        IEnumerable<SemanticObject> candidates)
    {
        if (renderStreamingCamera == null || candidates == null)
        {
            return Array.Empty<PlacementZoneVisualSummary>();
        }

        Plane[] frustumPlanes =
            GeometryUtility.CalculateFrustumPlanes(renderStreamingCamera);
        List<SemanticObject> candidateList = candidates
            .Where(candidate => candidate != null)
            .ToList();
        ExperimentPlacementZone[] zones =
            FindObjectsOfType<ExperimentPlacementZone>(true);
        var visuals = new List<PlacementZoneVisualSummary>();
        foreach (SemanticObject candidate in candidateList)
        {
            ExperimentPlacementZone zone = zones.FirstOrDefault(item =>
                item != null && item.Accepts(candidate.StableId));
            if (zone != null)
            {
                visuals.Add(
                    ProjectPlacementZone(candidate, zone, frustumPlanes));
            }
        }
        return visuals.ToArray();
    }

    private PlacementZoneVisualSummary[] BuildPlacementZoneVisualsForStep()
    {
        SemanticObject target = preparationSequence?.CurrentStep?.target == null
            ? null
            : preparationSequence.CurrentStep.target
                .GetComponentInParent<SemanticObject>();
        return target == null
            ? Array.Empty<PlacementZoneVisualSummary>()
            : BuildPlacementZoneVisuals(new[] { target });
    }

    private PlacementZoneVisualSummary ProjectPlacementZone(
        SemanticObject candidate,
        ExperimentPlacementZone zone,
        Plane[] frustumPlanes)
    {
        var visual = new PlacementZoneVisualSummary
        {
            id = string.IsNullOrWhiteSpace(zone.ZoneId)
                ? $"placement-{candidate?.StableId}"
                : zone.ZoneId,
            objectId = candidate?.StableId,
            displayName = candidate?.DisplayName
        };
        if (zone == null || !zone.gameObject.activeInHierarchy ||
            !TryGetPlacementZoneBounds(zone, out Bounds bounds))
        {
            return visual;
        }

        visual.hasBounds = true;
        if (!TryProjectBounds(
                bounds,
                frustumPlanes,
                out float xMin,
                out float yMin,
                out float xMax,
                out float yMax,
                out float depth))
        {
            return visual;
        }

        visual.depth = depth;
        visual.visible = true;
        visual.xMin = Mathf.Clamp01(xMin);
        visual.yMin = Mathf.Clamp01(yMin);
        visual.xMax = Mathf.Clamp01(xMax);
        visual.yMax = Mathf.Clamp01(yMax);
        return visual;
    }

    private bool TryProjectBounds(
        Bounds bounds,
        Plane[] frustumPlanes,
        out float xMin,
        out float yMin,
        out float xMax,
        out float yMax,
        out float depth)
    {
        Vector3 center = renderStreamingCamera.WorldToViewportPoint(bounds.center);
        depth = center.z;

        Vector3 min = bounds.min;
        Vector3 max = bounds.max;
        Vector3[] corners =
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };

        xMin = float.PositiveInfinity;
        yMin = float.PositiveInfinity;
        xMax = float.NegativeInfinity;
        yMax = float.NegativeInfinity;
        int frontCornerCount = 0;
        foreach (Vector3 corner in corners)
        {
            Vector3 viewportPoint =
                renderStreamingCamera.WorldToViewportPoint(corner);
            if (viewportPoint.z <= renderStreamingCamera.nearClipPlane)
            {
                continue;
            }

            frontCornerCount++;
            xMin = Mathf.Min(xMin, viewportPoint.x);
            yMin = Mathf.Min(yMin, viewportPoint.y);
            xMax = Mathf.Max(xMax, viewportPoint.x);
            yMax = Mathf.Max(yMax, viewportPoint.y);
        }

        bool intersectsViewport = frontCornerCount > 0 &&
                                  xMax >= 0f && xMin <= 1f &&
                                  yMax >= 0f && yMin <= 1f;
        return intersectsViewport &&
               GeometryUtility.TestPlanesAABB(frustumPlanes, bounds);
    }

    private static bool TryGetPlacementZoneBounds(
        ExperimentPlacementZone zone,
        out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Collider collider in
                 zone.GetComponentsInChildren<Collider>(false))
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }
            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        return found;
    }

    private static bool TryGetWorldBounds(
        SemanticObject candidate,
        out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in
                 candidate.GetComponentsInChildren<Renderer>(false))
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }
            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        if (found)
        {
            return true;
        }

        foreach (Collider collider in
                 candidate.GetComponentsInChildren<Collider>(false))
        {
            if (collider == null || !collider.enabled)
            {
                continue;
            }
            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        return found;
    }

    private Camera ResolveRenderStreamingCamera()
    {
        if (renderStreamingCamera != null)
        {
            return renderStreamingCamera;
        }

        GameObject streamCameraObject = GameObject.Find("streamcamera");
        if (streamCameraObject != null &&
            streamCameraObject.TryGetComponent(out Camera streamCamera))
        {
            return streamCamera;
        }

        return FindObjectsOfType<Camera>(true)
                   .FirstOrDefault(camera =>
                       camera != null &&
                       camera.name.IndexOf(
                           "stream", StringComparison.OrdinalIgnoreCase) >= 0)
               ?? Camera.main;
    }

    private void SelectTarget(GuidanceCommand command)
    {
        if (!EnsureRegistry(command))
        {
            return;
        }
        if (string.IsNullOrWhiteSpace(command.targetId))
        {
            SendError(command, "targetId is required.");
            return;
        }
        if (!objectRegistry.TryGet(command.targetId, out SemanticObject target))
        {
            SendError(command, $"Unknown targetId: {command.targetId}");
            return;
        }
        if (!target.Selectable)
        {
            SendError(command, $"Target is not selectable: {command.targetId}");
            return;
        }

        EnsureTrial(command);
        string requestedMode = string.IsNullOrWhiteSpace(command.mode)
            ? "highlight"
            : command.mode;
        if (!guidanceManager.TrySetMode(requestedMode))
        {
            SendError(command, $"Unknown guidance mode: {requestedMode}");
            return;
        }

        stateController?.ConfirmTarget(target.StableId);
        experimentLogger?.LogEvent("target_confirmed", target.StableId);
        guidanceManager.ShowTarget(target.gameObject);
    }

    private void ApplyMode(GuidanceCommand command, bool isTrialStart)
    {
        bool applied = guidanceManager.TrySetMode(command.mode);
        string appliedMode = GuidanceManager.GetModeName(guidanceManager.ActiveMode);
        SendResponse(
            isTrialStart ? "trial_ready" : "guidance_mode_applied",
            applied,
            command,
            null,
            null,
            applied
                ? $"Guidance mode set to {appliedMode}."
                : $"Unknown guidance mode: {command.mode}");
    }

    private void ApplyLayoutDensity(GuidanceCommand command)
    {
        if (layoutGenerator == null)
        {
            layoutGenerator = FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        }

        if (layoutGenerator == null)
        {
            SendError(command, "Experiment layout generator is missing.");
            return;
        }

        if (!TryParseLayoutSelection(command.density, out ExperimentLayoutSelection selection))
        {
            SendError(
                command,
                $"Unknown layout density: {command.density}. " +
                "Expected low, medium, high, or clear.");
            return;
        }

        int seed = command.seed == 0
            ? layoutGenerator.RandomSeed
            : command.seed;
        layoutGenerator.ApplySelection(selection, "pc_browser", seed);
        objectRegistry?.Refresh();

        int generatedCount = layoutGenerator.GeneratedCount;
        int registryCount = objectRegistry?.Count ?? 0;
        string density = selection.ToString().ToLowerInvariant();
        experimentLogger?.LogEvent(
            "layout_density_applied",
            $"density={density};generated={generatedCount};registry={registryCount};seed={seed}");
        SendResponse(
            "layout_density_applied",
            true,
            command,
            null,
            null,
            $"Layout density set to {density}. Generated {generatedCount} objects.",
            layoutDensity: density,
            generatedCount: generatedCount,
            registryCount: registryCount);
    }

    private static bool TryParseLayoutSelection(
        string value,
        out ExperimentLayoutSelection selection)
    {
        switch ((value ?? string.Empty).Trim().ToLowerInvariant())
        {
            case "low":
                selection = ExperimentLayoutSelection.Low;
                return true;
            case "medium":
                selection = ExperimentLayoutSelection.Medium;
                return true;
            case "high":
                selection = ExperimentLayoutSelection.High;
                return true;
            case "clear":
                selection = ExperimentLayoutSelection.Clear;
                return true;
            default:
                selection = ExperimentLayoutSelection.Clear;
                return false;
        }
    }

    private void StartSequence(GuidanceCommand command)
    {
        if (preparationSequence == null)
        {
            SendSequenceError(command, "PreparationSequenceController is missing.");
            return;
        }

        EnsureTrial(command);
        if (!string.IsNullOrWhiteSpace(command.mode) &&
            !guidanceManager.TrySetMode(command.mode))
        {
            SendSequenceError(command, $"Unknown guidance mode: {command.mode}");
            return;
        }

        if (!preparationSequence.StartSequence())
        {
            SendSequenceError(
                command, "The preparation sequence has no valid configured steps.");
        }
    }

    private void ExecuteSequenceCommand(
        bool succeeded,
        GuidanceCommand command,
        string error)
    {
        if (!succeeded)
        {
            SendSequenceError(
                command,
                preparationSequence == null
                    ? "PreparationSequenceController is missing."
                    : error);
        }
    }

    private void EnsureTrial(GuidanceCommand command)
    {
        if (stateController != null &&
            stateController.CurrentState != ExperimentTrialState.Idle &&
            stateController.CurrentState != ExperimentTrialState.Completed &&
            stateController.CurrentState != ExperimentTrialState.Aborted)
        {
            return;
        }

        _activeTrialId = string.IsNullOrWhiteSpace(command?.trialId)
            ? $"trial-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}"
            : command.trialId.Trim();
        stateController?.StartTrial(_activeTrialId, command?.condition);
        experimentLogger?.LogEvent("trial_started_implicitly");
    }

    private bool EnsureRegistry(GuidanceCommand command)
    {
        if (objectRegistry != null)
        {
            return true;
        }

        objectRegistry = SceneObjectRegistry.Instance ??
                         FindObjectOfType<SceneObjectRegistry>(true);
        if (objectRegistry != null)
        {
            objectRegistry.Refresh();
            return true;
        }

        SendError(command, "SceneObjectRegistry is missing.");
        return false;
    }

    private void Subscribe()
    {
        if (guidanceManager != null)
        {
            guidanceManager.TargetShown -= OnTargetShown;
            guidanceManager.TargetCleared -= OnTargetCleared;
            guidanceManager.TargetShown += OnTargetShown;
            guidanceManager.TargetCleared += OnTargetCleared;
        }
        if (preparationSequence != null)
        {
            preparationSequence.StateChanged -= OnSequenceStateChanged;
            preparationSequence.StateChanged += OnSequenceStateChanged;
        }
        if (interactionTracker != null)
        {
            interactionTracker.PlacementEvaluated -= OnPlacementEvaluated;
            interactionTracker.PlacementEvaluated += OnPlacementEvaluated;
        }
        if (_copperTransfer != null)
        {
            _copperTransfer.StateChanged -= OnTransferStateChanged;
            _copperTransfer.StateChanged += OnTransferStateChanged;
        }
        if (_copperMixing != null)
        {
            _copperMixing.StateChanged -= OnMixingStateChanged;
            _copperMixing.StateChanged += OnMixingStateChanged;
        }
        if (_copperFiltration != null)
        {
            _copperFiltration.StateChanged -= OnFiltrationStateChanged;
            _copperFiltration.StateChanged += OnFiltrationStateChanged;
        }
        if (experimentStartPose != null)
        {
            experimentStartPose.StateChanged -= OnRecenterStateChanged;
            experimentStartPose.StateChanged += OnRecenterStateChanged;
        }
    }

    private void Unsubscribe()
    {
        if (guidanceManager != null)
        {
            guidanceManager.TargetShown -= OnTargetShown;
            guidanceManager.TargetCleared -= OnTargetCleared;
        }
        if (preparationSequence != null)
        {
            preparationSequence.StateChanged -= OnSequenceStateChanged;
        }
        if (interactionTracker != null)
        {
            interactionTracker.PlacementEvaluated -= OnPlacementEvaluated;
        }
        if (_copperTransfer != null)
            _copperTransfer.StateChanged -= OnTransferStateChanged;
        if (_copperMixing != null)
            _copperMixing.StateChanged -= OnMixingStateChanged;
        if (_copperFiltration != null)
            _copperFiltration.StateChanged -= OnFiltrationStateChanged;
        if (experimentStartPose != null)
            experimentStartPose.StateChanged -= OnRecenterStateChanged;
    }

    private void OnRecenterStateChanged()
    {
        string status = experimentStartPose != null ? experimentStartPose.Status : "unavailable";
        SendResponse("recenter_state", status == "waiting" || status == "ready" || status == "idle",
            null, null, null, status);
    }

    private void OnTransferStateChanged()
    {
        _previewCandidates.Clear();
        SendResponse("transfer_state", true, null, "forceps", null,
            _copperTransfer.Stage.ToString(), null,
            preparationSequence?.CurrentStepIndex ?? -1,
            preparationSequence?.StepCount ?? 0);
        if (objectRegistry != null)
        {
            objectRegistry.Refresh();
            SendResponse("object_catalog", true, null, null, null,
                "Experiment material state updated.", objectRegistry.GetCatalog());
        }
    }

    private void OnMixingStateChanged()
    {
        if (_copperMixing == null) return;
        if (_copperFiltration != null && _copperFiltration.CanRetry) return;
        SendResponse("liquid_state", true, null, null, null, _copperMixing.Status);
        if (_lastMixingStage == _copperMixing.Stage) return;
        _lastMixingStage = _copperMixing.Stage;
        objectRegistry?.Refresh();
        if (objectRegistry != null)
            SendResponse("object_catalog", true, null, null, null, "Liquid state updated.", objectRegistry.GetCatalog());
    }

    private void OnFiltrationStateChanged()
    {
        if (_copperFiltration == null || _copperFiltration.Stage == CopperSulfateFiltrationDetector.FiltrationStage.Waiting) return;
        SendResponse("filtration_state", true, null, null, null, _copperFiltration.Status);
        if (_lastFiltrationStage == _copperFiltration.Stage) return;
        _lastFiltrationStage = _copperFiltration.Stage;
        objectRegistry?.Refresh();
        if (objectRegistry != null)
            SendResponse("object_catalog", true, null, null, null, "Filtration state updated.", objectRegistry.GetCatalog());
    }

    private void OnSequenceStateChanged()
    {
        PreparationSequenceController.Step step = preparationSequence?.CurrentStep;
        bool isComplete = preparationSequence != null && preparationSequence.IsComplete;
        bool isRunning = preparationSequence != null && preparationSequence.IsRunning;
        _previewCandidates.Clear();
        if (isComplete)
            stateController?.CompleteTrial();
        else if (isRunning)
            stateController?.BeginSequenceStep();
        experimentLogger?.LogEvent(
            isComplete ? "sequence_completed" : "sequence_state_changed",
            step?.id);
        SendResponse(
            "sequence_state",
            true,
            null,
            step?.target != null ? GetTargetId(step.target) : null,
            step?.target != null ? step.target.name : null,
            isComplete
                ? "Preparation sequence completed."
                : isRunning
                    ? "Preparation step is active; target cue awaits expert confirmation."
                    : "Preparation sequence reset.",
            null,
            preparationSequence?.CurrentStepIndex ?? -1,
            preparationSequence?.StepCount ?? 0,
            step?.instruction,
            isComplete,
            placementZoneVisuals: BuildPlacementZoneVisualsForStep());
    }

    private void OnPlacementEvaluated(
        ExperimentInteractionTracker.PlacementEvaluation evaluation)
    {
        if (evaluation == null)
        {
            return;
        }

        SendResponse(
            "placement_evaluated",
            evaluation.isCorrect,
            null,
            evaluation.releasedObjectId,
            null,
            evaluation.message,
            null,
            evaluation.stepIndex,
            evaluation.stepCount);
    }

    private void OnTargetShown(GameObject target, GuidanceMode mode)
    {
        string targetId = GetTargetId(target);
        stateController?.MarkGuidanceShown(targetId);
        experimentLogger?.LogEvent("guidance_target_shown", targetId);
        SendResponse(
            "guidance_target_shown",
            true,
            null,
            targetId,
            target != null ? target.name : null,
            "Unity rendered the confirmed guidance target.");
    }

    private void OnTargetCleared()
    {
        experimentLogger?.LogEvent("guidance_target_cleared");
        SendResponse(
            "guidance_target_cleared",
            true,
            null,
            null,
            null,
            "Unity cleared the guidance target.");
    }

    private void SendSequenceError(GuidanceCommand command, string message)
    {
        SendResponse("sequence_error", false, command, null, null, message);
    }

    private void SendError(GuidanceCommand command, string message)
    {
        experimentLogger?.LogEvent("command_error", message);
        SendResponse("guidance_error", false, command, null, null, message);
    }

    private void SendResponse(
        string type,
        bool ok,
        GuidanceCommand command,
        string targetId,
        string targetName,
        string message,
        SemanticObjectSummary[] candidates = null,
        int stepIndex = -1,
        int stepCount = 0,
        string instruction = null,
        bool sequenceComplete = false,
        CandidateVisualSummary[] candidateVisuals = null,
        PlacementZoneVisualSummary[] placementZoneVisuals = null,
        string layoutDensity = null,
        int generatedCount = -1,
        int registryCount = -1)
    {
        if (!IsConnected)
        {
            return;
        }

        string trialId = !string.IsNullOrWhiteSpace(command?.trialId)
            ? command.trialId
            : !string.IsNullOrWhiteSpace(stateController?.TrialId)
                ? stateController.TrialId
                : _activeTrialId;
        var response = new GuidanceResponse
        {
            type = type,
            ok = ok,
            mode = guidanceManager != null
                ? GuidanceManager.GetModeName(guidanceManager.ActiveMode)
                : "none",
            density = layoutDensity,
            trialId = trialId,
            state = stateController != null
                ? stateController.CurrentState.ToString()
                : ExperimentTrialState.Idle.ToString(),
            targetId = targetId,
            targetName = targetName,
            message = message,
            candidateCount = candidates?.Length ?? candidateVisuals?.Length ?? 0,
            candidates = candidates ?? Array.Empty<SemanticObjectSummary>(),
            candidateVisuals = candidateVisuals ??
                               Array.Empty<CandidateVisualSummary>(),
            placementZoneVisuals = placementZoneVisuals ??
                                   Array.Empty<PlacementZoneVisualSummary>(),
            stepIndex = stepIndex,
            stepCount = stepCount,
            instruction = instruction,
            sequenceComplete = sequenceComplete,
            generatedCount = generatedCount,
            registryCount = registryCount,
            unityTime = Time.realtimeSinceStartupAsDouble,
            mixingStage = _copperMixing != null ? _copperMixing.Stage.ToString() : null,
            cylinderMl = _copperMixing != null ? _copperMixing.Cylinder.VolumeMl : 0f,
            beakerMl = _copperMixing != null ? _copperMixing.Beaker.VolumeMl : 0f,
            sourceMl = _copperMixing != null ? _copperMixing.Source.VolumeMl : 0f,
            spilledMl = _copperMixing != null ? _copperMixing.SpilledMl : 0f,
            mixingProgress = _copperMixing != null ? _copperMixing.Progress : 0f,
            targetMl = _copperMixing != null ? _copperMixing.TargetMl : 0f,
            toleranceMl = _copperMixing != null ? _copperMixing.ToleranceMl : 0f,
            canRetryLiquid = _copperMixing != null && _copperMixing.CanRetry || _copperFiltration != null && _copperFiltration.CanRetry,
            filtrationStage = _copperFiltration != null ? _copperFiltration.Stage.ToString() : null,
            filtrateMl = _copperFiltration != null ? _copperFiltration.Receiver.VolumeMl : 0f,
            dishMl = _copperFiltration != null ? _copperFiltration.Dish.VolumeMl : 0f,
            filtrationInitialMl = _copperFiltration != null ? _copperFiltration.InitialMl : 0f,
            filtrationMinimumMl = _copperFiltration != null ? _copperFiltration.MinimumMl : 0f,
            filtrationSpilledMl = _copperFiltration != null ? _copperFiltration.AttemptSpilledMl : 0f
        };
        Send(JsonUtility.ToJson(response));
    }

    private static string GetTargetId(GameObject target)
    {
        return target != null
            ? target.GetComponentInParent<SemanticObject>()?.StableId
            : null;
    }
}
