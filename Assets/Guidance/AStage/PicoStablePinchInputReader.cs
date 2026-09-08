using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;

[DefaultExecutionOrder(XRInteractionUpdateOrder.k_XRInputDeviceButtonReader)]
public sealed class PicoStablePinchInputReader : MonoBehaviour, IXRInputButtonReader
{
    [SerializeField]
    private string handUsage = "LeftHand";

    [SerializeField, Range(0f, 1f)]
    private float pressThreshold = 0.8f;

    [SerializeField, Range(0f, 1f)]
    private float releaseThreshold = 0.35f;

    [SerializeField, Min(0f)]
    private float releaseGraceSeconds = 0.12f;

    private InputAction pinchStrengthAction;
    private bool isPerformed;
    private bool wasPerformedThisFrame;
    private bool wasCompletedThisFrame;
    private float belowReleaseSince = -1f;
    private float currentStrength;

    public void Configure(string usage)
    {
        if (handUsage == usage && pinchStrengthAction != null)
            return;

        handUsage = usage;
        if (isActiveAndEnabled)
            RebuildAction();
    }

    private void OnEnable()
    {
        RebuildAction();
    }

    private void OnDisable()
    {
        DisposeAction();
        isPerformed = false;
        wasPerformedThisFrame = false;
        wasCompletedThisFrame = false;
        belowReleaseSince = -1f;
        currentStrength = 0f;
    }

    private void Update()
    {
        currentStrength = pinchStrengthAction != null
            ? Mathf.Clamp01(pinchStrengthAction.ReadValue<float>())
            : 0f;

        bool previous = isPerformed;
        if (!isPerformed)
        {
            isPerformed = currentStrength >= pressThreshold;
            belowReleaseSince = -1f;
        }
        else if (currentStrength > releaseThreshold)
        {
            belowReleaseSince = -1f;
        }
        else
        {
            if (belowReleaseSince < 0f)
                belowReleaseSince = Time.unscaledTime;

            if (Time.unscaledTime - belowReleaseSince >= releaseGraceSeconds)
            {
                isPerformed = false;
                belowReleaseSince = -1f;
            }
        }

        wasPerformedThisFrame = !previous && isPerformed;
        wasCompletedThisFrame = previous && !isPerformed;
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
        return pinchStrengthAction != null && pinchStrengthAction.enabled;
    }

    private void RebuildAction()
    {
        DisposeAction();
        pinchStrengthAction = new InputAction(
            $"{name} Pinch Strength",
            InputActionType.Value,
            $"<PicoAimHand>{{{handUsage}}}/pinchStrengthIndex",
            expectedControlType: "Axis");
        pinchStrengthAction.Enable();
    }

    private void DisposeAction()
    {
        if (pinchStrengthAction == null)
            return;

        pinchStrengthAction.Disable();
        pinchStrengthAction.Dispose();
        pinchStrengthAction = null;
    }

    private void OnValidate()
    {
        pressThreshold = Mathf.Clamp01(pressThreshold);
        releaseThreshold = Mathf.Clamp(releaseThreshold, 0f, pressThreshold);
        releaseGraceSeconds = Mathf.Max(0f, releaseGraceSeconds);
    }
}
