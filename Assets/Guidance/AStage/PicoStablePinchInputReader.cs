using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DefaultExecutionOrder(XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
public sealed class PicoStablePinchInputReader : MonoBehaviour, IXRInputButtonReader, IXRSelectFilter
{
    [SerializeField]
    private string handUsage = "LeftHand";

    [SerializeField, Min(0f)]
    private float releaseGraceSeconds = 0.06f;

    [SerializeField, Min(0.01f)] private float pressDwellSeconds = 0.05f;
    [SerializeField, Min(0.005f)] private float pinchDistance = 0.025f;
    [SerializeField, Min(0.005f)] private float openDistance = 0.035f;

    private bool isPerformed;
    private bool wasPerformedThisFrame;
    private bool wasCompletedThisFrame;
    private float belowReleaseSince = -1f;
    private float currentStrength;
    private float abovePressSince = -1f;
    private bool armed;
    private bool blockNewSelection = true;
    private bool trackingValid;
    private XRDirectInteractor handInteractor;
    private XRHandSubsystem handSubsystem;
    private readonly List<XRHandSubsystem> subsystems = new List<XRHandSubsystem>();

    public bool canProcess => isActiveAndEnabled;

    public void BindInteractor(XRDirectInteractor interactor)
    {
        UnbindInteractor();
        handInteractor = interactor;
        if (handInteractor == null || !isActiveAndEnabled) return;
        handInteractor.selectFilters.Add(this);
        handInteractor.selectEntered.AddListener(OnSelected);
        handInteractor.selectExited.AddListener(OnReleased);
    }

    private void UnbindInteractor()
    {
        if (handInteractor == null) return;
        handInteractor.selectFilters.Remove(this);
        handInteractor.selectEntered.RemoveListener(OnSelected);
        handInteractor.selectExited.RemoveListener(OnReleased);
    }

    private void OnSelected(SelectEnterEventArgs args) => blockNewSelection = true;
    private void OnReleased(SelectExitEventArgs args) => blockNewSelection = true;

    public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable)
    {
        if (interactor.IsSelecting(interactable)) return isPerformed;
        return trackingValid && isPerformed && !blockNewSelection &&
            !interactor.hasSelection && !interactable.isSelected;
    }

    public void Configure(string usage)
    {
        if (handUsage == usage) return;
        handUsage = usage;
        ResetInput();
    }

    private void OnEnable()
    {
        ResetInput();
        BindInteractor(GetComponent<XRDirectInteractor>());
    }

    private void OnDisable()
    {
        UnbindInteractor();
        ResetInput();
    }

    private void ResetInput()
    {
        isPerformed = false;
        wasPerformedThisFrame = false;
        wasCompletedThisFrame = false;
        belowReleaseSince = -1f;
        currentStrength = 0f;
        abovePressSince = -1f;
        armed = trackingValid = false;
        blockNewSelection = true;
    }

    private void Update()
    {
        bool valid = TryGetFingerDistance(out float distance);
        SamplePinch(distance, valid, Time.unscaledTime);
    }

    private void SamplePinch(float distance, bool valid, float now)
    {
        bool previous = isPerformed;
        trackingValid = valid && !float.IsNaN(distance) && !float.IsInfinity(distance) && distance >= 0f;
        currentStrength = trackingValid ? Mathf.InverseLerp(openDistance, pinchDistance, distance) : (isPerformed ? 1f : 0f);
        if (!trackingValid)
        {
            // Missing joints do not prove that a held hand has opened.
            isPerformed = isPerformed && handInteractor != null && handInteractor.hasSelection;
            armed = false;
            blockNewSelection = true;
            abovePressSince = belowReleaseSince = -1f;
        }
        else if (distance >= openDistance)
        {
            abovePressSince = -1f;
            if (belowReleaseSince < 0f) belowReleaseSince = now;
            if (now - belowReleaseSince >= releaseGraceSeconds)
            {
                isPerformed = false;
                armed = true;
                blockNewSelection = false;
            }
        }
        else
        {
            belowReleaseSince = -1f;
            if (armed && !isPerformed && distance <= pinchDistance)
            {
                if (abovePressSince < 0f) abovePressSince = now;
                if (now - abovePressSince >= pressDwellSeconds)
                {
                    isPerformed = true;
                    armed = false;
                    abovePressSince = -1f;
                }
            }
            else abovePressSince = -1f;
        }

        wasPerformedThisFrame = !previous && isPerformed;
        wasCompletedThisFrame = previous && !isPerformed;
    }

    private bool TryGetFingerDistance(out float distance)
    {
        distance = 0f;
        if (handSubsystem == null || !handSubsystem.running)
        {
            handSubsystem = null;
            SubsystemManager.GetSubsystems(subsystems);
            foreach (var subsystem in subsystems)
                if (subsystem.running) { handSubsystem = subsystem; break; }
        }
        if (handSubsystem == null) return false;
        var hand = handUsage == "LeftHand" ? handSubsystem.leftHand : handSubsystem.rightHand;
        if (!hand.isTracked || !hand.GetJoint(XRHandJointID.ThumbTip).TryGetPose(out var thumb) ||
            !hand.GetJoint(XRHandJointID.IndexTip).TryGetPose(out var index)) return false;
        distance = Vector3.Distance(thumb.position, index.position);
        return true;
    }

    public bool ReadIsPerformed()
    {
        return isPerformed;
    }

    public bool ReadWasPerformedThisFrame()
    {
        return wasPerformedThisFrame;
    }

    public bool ReadWasCompletedThisFrame()
    {
        return wasCompletedThisFrame;
    }

    public float ReadValue()
    {
        return currentStrength;
    }

    public bool TryReadValue(out float value)
    {
        value = currentStrength;
        return trackingValid || isPerformed;
    }

    private void OnValidate()
    {
        releaseGraceSeconds = Mathf.Max(0f, releaseGraceSeconds);
        pressDwellSeconds = Mathf.Max(0.01f, pressDwellSeconds);
        pinchDistance = Mathf.Max(0.005f, pinchDistance);
        openDistance = Mathf.Max(pinchDistance + 0.005f, openDistance);
    }
}
