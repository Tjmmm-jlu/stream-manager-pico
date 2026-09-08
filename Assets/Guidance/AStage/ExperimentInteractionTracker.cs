using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class ExperimentInteractionTracker : MonoBehaviour
{
    [SerializeField] private ExperimentStateController stateController;
    [SerializeField] private ExperimentLogger experimentLogger;
    [SerializeField] private PreparationSequenceController preparationSequence;
    [SerializeField] private bool autoAdvanceSequence = true;

    private readonly List<XRGrabInteractable> _trackedObjects =
        new List<XRGrabInteractable>();

    public event Action<PlacementEvaluation> PlacementEvaluated;

    [Serializable]
    public sealed class PlacementEvaluation
    {
        public int stepIndex;
        public int stepCount;
        public string expectedObjectId;
        public string releasedObjectId;
        public string expectedZoneId;
        public string actualZoneId;
        public bool objectMatches;
        public bool insideExpectedZone;
        public bool isCorrect;
        public string message;
    }

    private void Awake()
    {
        if (stateController == null)
        {
            stateController = GetComponent<ExperimentStateController>();
        }
        if (experimentLogger == null)
        {
            experimentLogger = GetComponent<ExperimentLogger>();
        }
        if (preparationSequence == null)
        {
            preparationSequence = GetComponent<PreparationSequenceController>();
        }
    }

    private void OnEnable()
    {
        RefreshTrackedObjects();
    }

    private void OnDisable()
    {
        UnsubscribeAll();
    }

    public void RefreshTrackedObjects()
    {
        UnsubscribeAll();
        foreach (XRGrabInteractable interactable in
                 FindObjectsOfType<XRGrabInteractable>(true))
        {
            if (interactable.GetComponentInParent<SemanticObject>() == null)
            {
                continue;
            }

            interactable.selectEntered.AddListener(OnGrabStarted);
            interactable.selectExited.AddListener(OnGrabEnded);
            _trackedObjects.Add(interactable);
        }
    }

    private void UnsubscribeAll()
    {
        foreach (XRGrabInteractable interactable in _trackedObjects)
        {
            if (interactable == null)
            {
                continue;
            }
            interactable.selectEntered.RemoveListener(OnGrabStarted);
            interactable.selectExited.RemoveListener(OnGrabEnded);
        }
        _trackedObjects.Clear();
    }

    private void OnGrabStarted(SelectEnterEventArgs args)
    {
        SemanticObject semanticObject =
            args.interactableObject.transform.GetComponentInParent<SemanticObject>();
        if (semanticObject == null)
        {
            return;
        }

        string expectedObjectId = GetCurrentExpectedObjectId();
        bool isExpected = !string.IsNullOrWhiteSpace(expectedObjectId) &&
                          string.Equals(
                              expectedObjectId,
                              semanticObject.StableId,
                              StringComparison.OrdinalIgnoreCase);
        experimentLogger?.LogEvent(
            isExpected ? "target_grab_started" : "other_object_grab_started",
            semanticObject.StableId);
        if (isExpected)
        {
            stateController?.MarkOperatorActing();
        }
    }

    private void OnGrabEnded(SelectExitEventArgs args)
    {
        SemanticObject semanticObject =
            args.interactableObject.transform.GetComponentInParent<SemanticObject>();
        if (semanticObject != null)
        {
            experimentLogger?.LogEvent(
                "object_released", semanticObject.StableId);

            EvaluateReleasedObject(semanticObject);
        }
    }

    private void EvaluateReleasedObject(SemanticObject releasedObject)
    {
        if (preparationSequence == null ||
            !preparationSequence.IsRunning ||
            preparationSequence.CurrentStep == null)
        {
            return;
        }

        PreparationSequenceController.Step step =
            preparationSequence.CurrentStep;
        string expectedObjectId = GetStepTargetId(step);
        bool objectMatches = string.Equals(
            expectedObjectId,
            releasedObject.StableId,
            StringComparison.OrdinalIgnoreCase);

        var evaluation = new PlacementEvaluation
        {
            stepIndex = preparationSequence.CurrentStepIndex,
            stepCount = preparationSequence.StepCount,
            expectedObjectId = expectedObjectId ?? string.Empty,
            releasedObjectId = releasedObject.StableId,
            expectedZoneId = string.Empty,
            actualZoneId = string.Empty,
            objectMatches = objectMatches,
            // Kept true for compatibility with the existing data shape. The
            // placement zone is no longer part of the task decision.
            insideExpectedZone = true,
            isCorrect = objectMatches,
            message = BuildSelectionEvaluationMessage(
                objectMatches,
                expectedObjectId,
                releasedObject.StableId)
        };

        experimentLogger?.LogEvent(
            evaluation.isCorrect
                ? "target_selection_correct"
                : "target_selection_incorrect",
            evaluation.message);
        PlacementEvaluated?.Invoke(evaluation);

        if (!evaluation.isCorrect)
        {
            return;
        }

        if (preparationSequence.CurrentStepRequiresAction)
        {
            experimentLogger?.LogEvent("sequence_action_pending", step.completionAction);
            return;
        }

        stateController?.MarkOperatorActing();
        experimentLogger?.LogEvent(
            "sequence_step_completed",
            $"stepIndex={evaluation.stepIndex};object={releasedObject.StableId};" +
            $"zone={evaluation.expectedZoneId}");

        if (autoAdvanceSequence)
        {
            preparationSequence.NextStep();
        }
    }

    private string GetCurrentExpectedObjectId()
    {
        if (preparationSequence != null &&
            preparationSequence.IsRunning &&
            preparationSequence.CurrentStep != null)
        {
            return GetStepTargetId(preparationSequence.CurrentStep);
        }

        return stateController?.TargetId;
    }

    private static string GetStepTargetId(
        PreparationSequenceController.Step step)
    {
        if (step == null)
        {
            return string.Empty;
        }

        SemanticObject semanticObject = step.target == null
            ? null
            : step.target.GetComponentInParent<SemanticObject>();
        return semanticObject != null &&
               !string.IsNullOrWhiteSpace(semanticObject.StableId)
            ? semanticObject.StableId
            : step.id;
    }

    private static string BuildSelectionEvaluationMessage(
        bool objectMatches,
        string expectedObjectId,
        string releasedObjectId)
    {
        if (objectMatches)
        {
            return $"Target selected: {releasedObjectId}.";
        }

        return $"Wrong target: expected {expectedObjectId}, " +
               $"released {releasedObjectId}.";
    }
}
