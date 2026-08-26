using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class ExperimentInteractionTracker : MonoBehaviour
{
    [SerializeField] private ExperimentStateController stateController;
    [SerializeField] private ExperimentLogger experimentLogger;

    private readonly List<XRGrabInteractable> _trackedObjects =
        new List<XRGrabInteractable>();

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

        bool isExpected = stateController != null &&
                          string.Equals(
                              stateController.TargetId,
                              semanticObject.StableId,
                              System.StringComparison.OrdinalIgnoreCase);
        experimentLogger?.LogEvent(
            isExpected ? "target_grab_started" : "other_object_grab_started",
            semanticObject.StableId);
        if (isExpected)
        {
            stateController.MarkOperatorActing();
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
        }
    }
}
