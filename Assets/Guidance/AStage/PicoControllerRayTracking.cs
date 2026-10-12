using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

[DisallowMultipleComponent]
[DefaultExecutionOrder(-29900)]
public sealed class PicoControllerRayTracking : MonoBehaviour
{
    private XRRayInteractor[] _rays;
    private ActionBasedController[] _controllers;
    private XRInteractorLineVisual[] _visuals;
    private LineRenderer[] _lines;
    private float _nextHandCheck;
    private float _nextDiagnostic;

    public void RefreshControllers()
    {
        _rays = GetComponentsInChildren<XRRayInteractor>(true);
        _controllers = _rays.Select(r => r.GetComponentInParent<ActionBasedController>()).ToArray();
        _visuals = _rays.Select(r => r.GetComponent<XRInteractorLineVisual>()).ToArray();
        _lines = _rays.Select(r => r.GetComponent<LineRenderer>()).ToArray();
    }

    private void Awake() => RefreshControllers();

    private void Update()
    {
        if (Time.unscaledTime >= _nextHandCheck)
        {
            PicoControllerGrabBootstrap.DisableHandObjects(transform);
            _nextHandCheck = Time.unscaledTime + 0.5f;
        }
        if (_rays == null) return;
        for (int i = 0; i < _rays.Length; i++)
        {
            var controller = _controllers[i];
            var ray = _rays[i];
            if (ray == null) continue;
            bool tracked = HasTrackedPose(controller, ray.handedness);
            // Normal release preserves the apparatus return-home behavior on tracking loss.
            if (!tracked && ray.hasSelection && ray.interactionManager != null)
                foreach (var item in ray.interactablesSelected.ToArray())
                    ray.interactionManager.SelectExit((IXRSelectInteractor)ray, item);
            ray.enabled = tracked;
            if (_visuals[i] != null) _visuals[i].enabled = tracked;
            if (!tracked && _lines[i] != null) _lines[i].enabled = false;
        }
    }

    private static bool HasTrackedPose(ActionBasedController controller, InteractorHandedness hand)
    {
        if (controller == null || !controller.isActiveAndEnabled) return false;
        var tracked = controller.isTrackedAction.action;
        var state = controller.trackingStateAction.action;
        bool hasPose = state != null && state.enabled && (state.ReadValue<int>() & 3) == 3;
        if ((tracked != null && tracked.enabled && tracked.ReadValue<float>() > 0.5f) || hasPose)
            return true;

        // PXR may expose native XR tracking before the Input System actions resolve.
        var device = InputDevices.GetDeviceAtXRNode(hand == InteractorHandedness.Left ? XRNode.LeftHand : XRNode.RightHand);
        if (!device.isValid || !device.TryGetFeatureValue(CommonUsages.isTracked, out bool nativeTracked) || !nativeTracked ||
            !device.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 position) ||
            !device.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion rotation)) return false;
        controller.transform.SetLocalPositionAndRotation(position, rotation);
        return true;
    }

    private void LateUpdate()
    {
        if (_rays == null || Time.unscaledTime < _nextDiagnostic || Time.unscaledTime > 35f) return;
        _nextDiagnostic = Time.unscaledTime + 5f;
        for (int i = 0; i < _rays.Length; i++)
        {
            var controller = _controllers[i];
            if (controller == null || _rays[i] == null) continue;
            var tracked = controller.isTrackedAction.action;
            var state = controller.trackingStateAction.action;
            var position = controller.positionAction.action;
            var device = InputDevices.GetDeviceAtXRNode(_rays[i].handedness == InteractorHandedness.Left ? XRNode.LeftHand : XRNode.RightHand);
            var line = _lines[i];
            Debug.Log($"[PICO Ray Visibility v2] {_rays[i].handedness}: native={device.name}/{device.isValid}, " +
                $"tracked={tracked?.enabled}/{tracked?.ReadValue<float>()}, state={state?.ReadValue<int>()}, " +
                $"poseControls={position?.controls.Count}, ray={_rays[i].enabled}, visual={_visuals[i]?.enabled}, " +
                $"line={line?.enabled}/{line?.positionCount}, shader={line?.sharedMaterial?.shader?.name}, " +
                $"localPose={controller.transform.localPosition}");
        }
    }
}
