using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class ExperimentLogRecord
{
    public string utcTime;
    public double unityTime;
    public string sessionId;
    public string eventType;
    public string trialId;
    public string condition;
    public string state;
    public string targetId;
    public string[] candidateIds;
    public string message;
}

[DisallowMultipleComponent]
public sealed class ExperimentLogger : MonoBehaviour
{
    [SerializeField] private ExperimentStateController stateController;
    [SerializeField] private string logFolderName = "ExperimentLogs";
    [SerializeField] private bool echoToConsole = true;

    private string _sessionId;
    private string _logFilePath;

    public string SessionId => _sessionId;
    public string LogFilePath => _logFilePath;

    public ExperimentStateController StateController
    {
        get => stateController;
        set
        {
            Unsubscribe();
            stateController = value;
            Subscribe();
        }
    }

    private void Awake()
    {
        if (stateController == null)
        {
            stateController = GetComponent<ExperimentStateController>();
        }

        _sessionId = DateTime.UtcNow.ToString(
            "yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture);
        string folder = Path.Combine(Application.persistentDataPath, logFolderName);
        Directory.CreateDirectory(folder);
        _logFilePath = Path.Combine(folder, $"session-{_sessionId}.jsonl");
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void Start()
    {
        LogEvent("session_started", Application.version);
        Debug.Log($"[ExperimentLog] File: {_logFilePath}");
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    public void LogEvent(string eventType, string message = null)
    {
        if (string.IsNullOrWhiteSpace(_logFilePath))
        {
            return;
        }

        var record = new ExperimentLogRecord
        {
            utcTime = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            unityTime = Time.realtimeSinceStartupAsDouble,
            sessionId = _sessionId,
            eventType = eventType ?? string.Empty,
            trialId = stateController?.TrialId ?? string.Empty,
            condition = stateController?.Condition ?? string.Empty,
            state = stateController != null
                ? stateController.CurrentState.ToString()
                : ExperimentTrialState.Idle.ToString(),
            targetId = stateController?.TargetId ?? string.Empty,
            candidateIds = stateController?.CandidateIds?.ToArray() ??
                           Array.Empty<string>(),
            message = message ?? string.Empty
        };

        string json = JsonUtility.ToJson(record);
        try
        {
            File.AppendAllText(_logFilePath, json + Environment.NewLine);
        }
        catch (Exception exception)
        {
            Debug.LogError($"[ExperimentLog] Write failed: {exception.Message}");
            return;
        }

        if (echoToConsole)
        {
            Debug.Log($"[ExperimentLog] {json}");
        }
    }

    private void Subscribe()
    {
        if (stateController == null)
        {
            return;
        }
        stateController.StateChanged -= OnStateChanged;
        stateController.StateChanged += OnStateChanged;
    }

    private void Unsubscribe()
    {
        if (stateController != null)
        {
            stateController.StateChanged -= OnStateChanged;
        }
    }

    private void OnStateChanged(
        ExperimentTrialState previous,
        ExperimentTrialState current)
    {
        LogEvent("state_changed", $"{previous}->{current}");
    }
}
