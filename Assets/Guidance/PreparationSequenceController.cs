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
        public string completionAction;
    }

    [SerializeField] private GuidanceManager guidanceManager;
    [SerializeField] private List<Step> steps = new List<Step>();
    [SerializeField] private bool autoStartOnPlay;
    [SerializeField] private bool showTargetAutomatically;

    private int _currentStepIndex = -1;
    private bool _isRunning;
    private readonly HashSet<int> _completedActions = new HashSet<int>();

    public event Action StateChanged;
    public event Action Resetting;

    public int CurrentStepIndex => _currentStepIndex;
    public int StepCount => steps.Count;
    public IReadOnlyList<Step> Steps => steps;
    public bool IsRunning => _isRunning;
    public bool IsComplete => !_isRunning && _currentStepIndex >= steps.Count;
    public bool CurrentStepRequiresAction =>
        CurrentStep != null && !string.IsNullOrWhiteSpace(CurrentStep.completionAction);
    public bool CanAdvance => IsRunning &&
        (!CurrentStepRequiresAction || _completedActions.Contains(_currentStepIndex));
    public bool HasPendingActions => steps.Exists(step =>
        !string.IsNullOrWhiteSpace(step.completionAction) &&
        !_completedActions.Contains(steps.IndexOf(step)));
    public Step CurrentStep =>
        _currentStepIndex >= 0 && _currentStepIndex < steps.Count
            ? steps[_currentStepIndex]
            : null;

    public GuidanceManager GuidanceManager
    {
        get => guidanceManager;
        set => guidanceManager = value;
    }

    public bool ShowTargetAutomatically
    {
        get => showTargetAutomatically;
        set => showTargetAutomatically = value;
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

    public void Configure(
        GuidanceManager manager,
        IEnumerable<Step> configuredSteps,
        bool autoShowTarget = false)
    {
        guidanceManager = manager;
        steps = new List<Step>(configuredSteps);
        showTargetAutomatically = autoShowTarget;
        _currentStepIndex = -1;
        _isRunning = false;
        _completedActions.Clear();
    }

    public bool StartSequence()
    {
        if (steps.Count == 0 || guidanceManager == null)
        {
            return false;
        }

        ResetSequence();
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

        for (int previous = 0; previous < index; previous++)
        {
            if (!string.IsNullOrWhiteSpace(steps[previous].completionAction) &&
                !_completedActions.Contains(previous))
                return false;
        }
        // Replaying an action would otherwise disagree with the material state.
        if (!string.IsNullOrWhiteSpace(steps[index].completionAction) &&
            _completedActions.Contains(index))
            return false;

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

        if (!CanAdvance)
            return false;

        if (_currentStepIndex + 1 >= steps.Count)
        {
            return CompleteSequence();
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

        return ShowStep(Mathf.Clamp(_currentStepIndex - 1, 0, steps.Count - 1));
    }

    public bool CompleteAction(string action)
    {
        if (!IsRunning || !CurrentStepRequiresAction ||
            !string.Equals(CurrentStep.completionAction, action, StringComparison.Ordinal) ||
            !_completedActions.Add(_currentStepIndex))
            return false;
        return NextStep();
    }

    public bool CompleteSequence()
    {
        if (HasPendingActions)
            return false;
        guidanceManager?.ClearTarget();
        _currentStepIndex = steps.Count;
        _isRunning = false;
        StateChanged?.Invoke();
        Debug.Log(
            $"[Preparation] Sequence completed at " +
            $"{Time.realtimeSinceStartupAsDouble:F6}.");
        return true;
    }

    public void ResetSequence()
    {
        _isRunning = false;
        Resetting?.Invoke();
        guidanceManager?.ClearTarget();
        _currentStepIndex = -1;
        _completedActions.Clear();
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

        if (showTargetAutomatically)
        {
            guidanceManager.ShowTarget(step.target);
        }
        else
        {
            // The step defines the correct answer, but the expert must confirm
            // a target before the operator receives any visual cue.
            guidanceManager.ClearTarget();
        }
        StateChanged?.Invoke();
        Debug.Log(
            $"[Preparation] step={_currentStepIndex + 1}/{steps.Count} " +
            $"id={step.id} target={step.target.name} " +
            $"instruction=\"{step.instruction}\" " +
            $"time={Time.realtimeSinceStartupAsDouble:F6}");
    }
}
