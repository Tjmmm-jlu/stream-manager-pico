using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class PreparationSequenceController : MonoBehaviour
{
    [Serializable]
    public sealed class Step
    {
        public string id;
        [TextArea] public string instruction;
        public GameObject target;
    }

    [SerializeField] private GuidanceManager guidanceManager;
    [SerializeField] private List<Step> steps = new List<Step>();
    [SerializeField] private bool autoStartOnPlay;

    private int _currentStepIndex = -1;
    private bool _isRunning;

    public event Action StateChanged;

    public int CurrentStepIndex => _currentStepIndex;
    public int StepCount => steps.Count;
    public bool IsRunning => _isRunning;
    public bool IsComplete => !_isRunning && _currentStepIndex >= steps.Count;
    public Step CurrentStep =>
        _currentStepIndex >= 0 && _currentStepIndex < steps.Count
            ? steps[_currentStepIndex]
            : null;

    public GuidanceManager GuidanceManager
    {
        get => guidanceManager;
        set => guidanceManager = value;
    }

    private void Awake()
    {
        if (guidanceManager == null)
        {
            guidanceManager = GetComponent<GuidanceManager>();
        }
    }

    private void Start()
    {
        if (autoStartOnPlay)
        {
            StartSequence();
        }
    }

    public void Configure(GuidanceManager manager, IEnumerable<Step> configuredSteps)
    {
        guidanceManager = manager;
        steps = new List<Step>(configuredSteps);
        _currentStepIndex = -1;
        _isRunning = false;
    }

    public bool StartSequence()
    {
        if (steps.Count == 0 || guidanceManager == null)
        {
            return false;
        }

        _currentStepIndex = 0;
        _isRunning = true;
        ShowCurrentStep();
        return true;
    }

    public bool ShowStep(int index)
    {
        if (index < 0 || index >= steps.Count || guidanceManager == null)
        {
            return false;
        }

        _currentStepIndex = index;
        _isRunning = true;
        ShowCurrentStep();
        return true;
    }

    public bool NextStep()
    {
        if (!_isRunning)
        {
            return StartSequence();
        }

        if (_currentStepIndex + 1 >= steps.Count)
        {
            CompleteSequence();
            return true;
        }

        _currentStepIndex++;
        ShowCurrentStep();
        return true;
    }

    public bool PreviousStep()
    {
        if (steps.Count == 0 || guidanceManager == null)
        {
            return false;
        }

        _currentStepIndex = Mathf.Clamp(_currentStepIndex - 1, 0, steps.Count - 1);
        _isRunning = true;
        ShowCurrentStep();
        return true;
    }

    public void CompleteSequence()
    {
        guidanceManager?.ClearTarget();
        _currentStepIndex = steps.Count;
        _isRunning = false;
        StateChanged?.Invoke();
        Debug.Log(
            $"[Preparation] Sequence completed at " +
            $"{Time.realtimeSinceStartupAsDouble:F6}.");
    }

    public void ResetSequence()
    {
        guidanceManager?.ClearTarget();
        _currentStepIndex = -1;
        _isRunning = false;
        StateChanged?.Invoke();
    }

    private void ShowCurrentStep()
    {
        Step step = CurrentStep;
        if (step == null || step.target == null)
        {
            Debug.LogError(
                $"[Preparation] Step {_currentStepIndex} has no target.");
            return;
        }

        guidanceManager.ShowTarget(step.target);
        StateChanged?.Invoke();
        Debug.Log(
            $"[Preparation] step={_currentStepIndex + 1}/{steps.Count} " +
            $"id={step.id} target={step.target.name} " +
            $"instruction=\"{step.instruction}\" " +
            $"time={Time.realtimeSinceStartupAsDouble:F6}");
    }
}
