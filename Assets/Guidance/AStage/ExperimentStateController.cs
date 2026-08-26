using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ExperimentTrialState
{
    Idle,
    WaitingForInstruction,
    GeneratingCandidates,
    AwaitingConfirmation,
    TargetConfirmed,
    GuidanceShown,
    OperatorActing,
    Completed,
    Aborted
}

[DisallowMultipleComponent]
public sealed class ExperimentStateController : MonoBehaviour
{
    [SerializeField] private ExperimentTrialState currentState =
        ExperimentTrialState.Idle;
    [SerializeField] private string trialId = string.Empty;
    [SerializeField] private string condition = string.Empty;
    [SerializeField] private string targetId = string.Empty;

    private readonly List<string> _candidateIds = new List<string>();

    public event Action<ExperimentTrialState, ExperimentTrialState> StateChanged;

    public ExperimentTrialState CurrentState => currentState;
    public string TrialId => trialId;
    public string Condition => condition;
    public string TargetId => targetId;
    public IReadOnlyList<string> CandidateIds => _candidateIds;

    public void StartTrial(string requestedTrialId, string requestedCondition)
    {
        trialId = string.IsNullOrWhiteSpace(requestedTrialId)
            ? $"trial-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}"
            : requestedTrialId.Trim();
        condition = string.IsNullOrWhiteSpace(requestedCondition)
            ? "unspecified"
            : requestedCondition.Trim();
        targetId = string.Empty;
        _candidateIds.Clear();
        SetState(ExperimentTrialState.WaitingForInstruction, true);
    }

    public void BeginCandidateGeneration()
    {
        SetState(ExperimentTrialState.GeneratingCandidates);
    }

    public void SetCandidates(IEnumerable<SemanticObject> candidates)
    {
        _candidateIds.Clear();
        if (candidates != null)
        {
            _candidateIds.AddRange(
                candidates
                    .Where(item => item != null)
                    .Select(item => item.StableId));
        }
        SetState(ExperimentTrialState.AwaitingConfirmation);
    }

    public void ConfirmTarget(string confirmedTargetId)
    {
        targetId = confirmedTargetId?.Trim() ?? string.Empty;
        SetState(ExperimentTrialState.TargetConfirmed);
    }

    public void MarkGuidanceShown(string shownTargetId)
    {
        if (!string.IsNullOrWhiteSpace(shownTargetId))
        {
            targetId = shownTargetId.Trim();
        }
        SetState(ExperimentTrialState.GuidanceShown);
    }

    public void MarkOperatorActing()
    {
        SetState(ExperimentTrialState.OperatorActing);
    }

    public void CompleteTrial()
    {
        SetState(ExperimentTrialState.Completed);
    }

    public void AbortTrial()
    {
        SetState(ExperimentTrialState.Aborted);
    }

    public void ResetTrial()
    {
        trialId = string.Empty;
        condition = string.Empty;
        targetId = string.Empty;
        _candidateIds.Clear();
        SetState(ExperimentTrialState.Idle, true);
    }

    private void SetState(ExperimentTrialState nextState, bool force = false)
    {
        if (!force && currentState == nextState)
        {
            return;
        }

        ExperimentTrialState previous = currentState;
        currentState = nextState;
        StateChanged?.Invoke(previous, nextState);
        Debug.Log(
            $"[ExperimentState] trial={trialId} condition={condition} " +
            $"{previous}->{nextState} target={targetId}");
    }
}
