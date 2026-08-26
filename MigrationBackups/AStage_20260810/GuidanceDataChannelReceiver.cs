using System;
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
        public string trialId;
        public int stepIndex;
    }

    [Serializable]
    private sealed class GuidanceResponse
    {
        public string type;
        public bool ok;
        public string mode;
        public string trialId;
        public string targetName;
        public string message;
        public int stepIndex;
        public int stepCount;
        public string instruction;
        public bool sequenceComplete;
        public double unityTime;
    }

    [SerializeField] private GuidanceManager guidanceManager;
    [SerializeField] private PreparationSequenceController preparationSequence;

    private string _activeTrialId = string.Empty;
    private bool _readySent;

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

    private void Reset()
    {
        local = false;
        label = "guidance";
    }

    private void Awake()
    {
        local = false;
        label = "guidance";
        if (guidanceManager == null)
        {
            guidanceManager = GetComponent<GuidanceManager>();
        }
        if (preparationSequence == null)
        {
            preparationSequence =
                GetComponent<PreparationSequenceController>();
        }
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public override void SetChannel(
        string connectionId,
        RTCDataChannel channel)
    {
        _readySent = false;
        base.SetChannel(connectionId, channel);

        // Render Streaming 3.1 can dispatch OnDataChannel after the browser
        // channel has already reached Open. DataChannelBase only subscribes
        // to the future OnOpen event, so handle the already-open case here.
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

    private void NotifyChannelReady(string connectionId)
    {
        if (_readySent)
        {
            return;
        }

        _readySent = true;
        SendResponse(
            "guidance_ready",
            true,
            GuidanceManager.GetModeName(
                guidanceManager != null
                    ? guidanceManager.ActiveMode
                    : GuidanceMode.None),
            null,
            null,
            guidanceManager != null
                ? "Unity guidance controller is ready."
                : "GuidanceManager is missing.");
        Debug.Log(
            $"[Guidance] DataChannel opened. connection={connectionId} label={Label}");
    }

    protected override void OnClose(string connectionId)
    {
        base.OnClose(connectionId);
        _readySent = false;
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
            SendResponse(
                "guidance_error", false, null, null, null,
                $"Malformed JSON: {exception.Message}");
            return;
        }

        if (command == null || string.IsNullOrWhiteSpace(command.type))
        {
            SendResponse(
                "guidance_error", false, null, null, null,
                "Command type is missing.");
            return;
        }

        if (guidanceManager == null)
        {
            SendResponse(
                "guidance_error", false, null, command.trialId, null,
                "GuidanceManager is missing.");
            return;
        }

        switch (command.type.Trim().ToLowerInvariant())
        {
            case "set_guidance_mode":
                ApplyMode(command, false);
                break;

            case "start_trial":
                _activeTrialId = command.trialId ?? string.Empty;
                guidanceManager.ClearTarget();
                ApplyMode(command, true);
                break;

            case "clear_guidance":
            case "abort_trial":
                guidanceManager.ClearTarget();
                SendResponse(
                    "guidance_cleared", true,
                    GuidanceManager.GetModeName(guidanceManager.ActiveMode),
                    command.trialId, null, "Guidance target cleared.");
                if (command.type.Equals(
                        "abort_trial", StringComparison.OrdinalIgnoreCase))
                {
                    _activeTrialId = string.Empty;
                }
                break;

            case "ping":
                SendResponse(
                    "guidance_pong", true,
                    GuidanceManager.GetModeName(guidanceManager.ActiveMode),
                    command.trialId, null, "Unity guidance channel is alive.");
                break;

            case "start_sequence":
                StartSequence(command);
                break;

            case "next_step":
            case "complete_step":
                ExecuteSequenceCommand(
                    preparationSequence != null &&
                    preparationSequence.NextStep(),
                    "Could not advance the preparation sequence.");
                break;

            case "previous_step":
                ExecuteSequenceCommand(
                    preparationSequence != null &&
                    preparationSequence.PreviousStep(),
                    "Could not move to the previous preparation step.");
                break;

            case "show_step":
                ExecuteSequenceCommand(
                    preparationSequence != null &&
                    preparationSequence.ShowStep(command.stepIndex),
                    $"Invalid preparation step index: {command.stepIndex}");
                break;

            case "complete_sequence":
                if (preparationSequence == null)
                {
                    SendSequenceError(
                        "PreparationSequenceController is missing.");
                }
                else
                {
                    preparationSequence.CompleteSequence();
                }
                break;

            case "reset_sequence":
                if (preparationSequence == null)
                {
                    SendSequenceError(
                        "PreparationSequenceController is missing.");
                }
                else
                {
                    preparationSequence.ResetSequence();
                }
                break;

            default:
                SendResponse(
                    "guidance_error", false, null, command.trialId, null,
                    $"Unsupported command type: {command.type}");
                break;
        }
    }

    private void ApplyMode(GuidanceCommand command, bool isTrialStart)
    {
        bool applied = guidanceManager.TrySetMode(command.mode);
        string appliedMode =
            GuidanceManager.GetModeName(guidanceManager.ActiveMode);
        SendResponse(
            isTrialStart ? "trial_ready" : "guidance_mode_applied",
            applied,
            appliedMode,
            command.trialId,
            null,
            applied
                ? $"Guidance mode set to {appliedMode}."
                : $"Unknown guidance mode: {command.mode}");
    }

    private void StartSequence(GuidanceCommand command)
    {
        if (preparationSequence == null)
        {
            SendSequenceError("PreparationSequenceController is missing.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(command.mode) &&
            !guidanceManager.TrySetMode(command.mode))
        {
            SendSequenceError($"Unknown guidance mode: {command.mode}");
            return;
        }

        _activeTrialId = command.trialId ?? string.Empty;
        if (!preparationSequence.StartSequence())
        {
            SendSequenceError(
                "The preparation sequence has no valid configured steps.");
        }
    }

    private void ExecuteSequenceCommand(bool succeeded, string error)
    {
        if (!succeeded)
        {
            SendSequenceError(
                preparationSequence == null
                    ? "PreparationSequenceController is missing."
                    : error);
        }
    }

    private void SendSequenceError(string message)
    {
        SendResponse(
            "sequence_error",
            false,
            guidanceManager != null
                ? GuidanceManager.GetModeName(guidanceManager.ActiveMode)
                : "none",
            _activeTrialId,
            null,
            message);
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
    }

    private void OnSequenceStateChanged()
    {
        PreparationSequenceController.Step step =
            preparationSequence != null
                ? preparationSequence.CurrentStep
                : null;
        bool isComplete =
            preparationSequence != null && preparationSequence.IsComplete;
        bool isRunning =
            preparationSequence != null && preparationSequence.IsRunning;
        SendResponse(
            "sequence_state",
            true,
            guidanceManager != null
                ? GuidanceManager.GetModeName(guidanceManager.ActiveMode)
                : "none",
            _activeTrialId,
            step?.target != null ? step.target.name : null,
            isComplete
                ? "Preparation sequence completed."
                : isRunning
                    ? "Preparation step is active."
                    : "Preparation sequence reset.",
            preparationSequence?.CurrentStepIndex ?? -1,
            preparationSequence?.StepCount ?? 0,
            step?.instruction,
            isComplete);
    }

    private void OnTargetShown(GameObject target, GuidanceMode mode)
    {
        SendResponse(
            "guidance_target_shown",
            true,
            GuidanceManager.GetModeName(mode),
            _activeTrialId,
            target != null ? target.name : null,
            "Unity rendered the selected guidance target.");
    }

    private void OnTargetCleared()
    {
        SendResponse(
            "guidance_target_cleared",
            true,
            guidanceManager != null
                ? GuidanceManager.GetModeName(guidanceManager.ActiveMode)
                : "none",
            _activeTrialId,
            null,
            "Unity cleared the guidance target.");
    }

    private void SendResponse(
        string type,
        bool ok,
        string mode,
        string trialId,
        string targetName,
        string message,
        int stepIndex = -1,
        int stepCount = 0,
        string instruction = null,
        bool sequenceComplete = false)
    {
        if (!IsConnected)
        {
            return;
        }

        var response = new GuidanceResponse
        {
            type = type,
            ok = ok,
            mode = mode,
            trialId = trialId,
            targetName = targetName,
            message = message,
            stepIndex = stepIndex,
            stepCount = stepCount,
            instruction = instruction,
            sequenceComplete = sequenceComplete,
            unityTime = Time.realtimeSinceStartupAsDouble
        };
        Send(JsonUtility.ToJson(response));
    }
}
