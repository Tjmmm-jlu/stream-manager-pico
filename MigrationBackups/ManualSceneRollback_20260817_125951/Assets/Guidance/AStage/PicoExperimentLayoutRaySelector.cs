using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public sealed class PicoExperimentLayoutRaySelector : MonoBehaviour
{
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private ExperimentVrLayoutSelector layoutSelector;
    [Tooltip("PICO official PXR_Hand component. Read through its public RayValid and Pinch properties.")]
    [SerializeField] private MonoBehaviour picoHand;
    [SerializeField] private LineRenderer lineRenderer;
    [Tooltip("Optional mesh-based beam used on PICO instead of a dynamic LineRenderer material.")]
    [SerializeField] private Renderer beamRenderer;
    [SerializeField] private LayerMask raycastLayers = ~0;
    [Min(0.1f)] [SerializeField] private float maxDistance = 5f;

    private PropertyInfo _rayValidProperty;
    private PropertyInfo _pinchProperty;
    private ExperimentVrLayoutButton _hovered;
    private bool _wasPressed;
    private InputDevice _rightController;

    public Transform RayOrigin => rayOrigin;
    public MonoBehaviour PicoHand => picoHand;

    public void Configure(
        Transform origin,
        MonoBehaviour hand,
        ExperimentVrLayoutSelector selector,
        LineRenderer visual,
        float distance)
    {
        rayOrigin = origin;
        picoHand = hand;
        layoutSelector = selector;
        lineRenderer = visual;
        maxDistance = Mathf.Max(0.1f, distance);
        CacheHandProperties();
    }

    public void ConfigureBeam(Renderer visual)
    {
        beamRenderer = visual;
        if (beamRenderer != null) beamRenderer.enabled = false;
    }

    private void Awake()
    {
        CacheHandProperties();
    }

    private void OnDisable()
    {
        SetHovered(null);
        if (lineRenderer != null) lineRenderer.enabled = false;
        if (beamRenderer != null) beamRenderer.enabled = false;
    }

    private void Update()
    {
        if (rayOrigin == null)
        {
            SetHovered(null);
            return;
        }

        bool handRayValid = ReadHandBool(_rayValidProperty, rayOrigin.gameObject.activeInHierarchy);
        bool pinchPressed = ReadHandBool(_pinchProperty, false);
        bool controllerPressed = ReadControllerSelect();
        bool rayAvailable = handRayValid || _rightController.isValid;

        RaycastHit hit = default;
        bool hasHit = rayAvailable && Physics.Raycast(
            rayOrigin.position,
            rayOrigin.forward,
            out hit,
            maxDistance,
            raycastLayers,
            QueryTriggerInteraction.Collide);
        ExperimentVrLayoutButton button = hasHit
            ? hit.collider.GetComponentInParent<ExperimentVrLayoutButton>()
            : null;
        SetHovered(button);

        float visualDistance = hasHit ? hit.distance : maxDistance;
        UpdateLine(rayAvailable, visualDistance, button != null);

        bool pressed = pinchPressed || controllerPressed;
        if (pressed && !_wasPressed && _hovered != null)
        {
            _hovered.ActivateFromRay();
        }
        else if (pressed && !_wasPressed && hasHit)
        {
            ActivateByHitName(hit.collider.transform);
        }
        _wasPressed = pressed;
    }

    private void CacheHandProperties()
    {
        if (picoHand == null) return;
        Type type = picoHand.GetType();
        _rayValidProperty = type.GetProperty("RayValid", BindingFlags.Instance | BindingFlags.Public);
        _pinchProperty = type.GetProperty("Pinch", BindingFlags.Instance | BindingFlags.Public);
    }

    private bool ReadHandBool(PropertyInfo property, bool fallback)
    {
        if (picoHand == null || property == null) return fallback;
        try
        {
            object value = property.GetValue(picoHand);
            return value is bool result ? result : fallback;
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private bool ReadControllerSelect()
    {
        if (!_rightController.isValid)
        {
            _rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        }

        return _rightController.isValid &&
               _rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool pressed) &&
               pressed;
    }

    private void SetHovered(ExperimentVrLayoutButton next)
    {
        if (_hovered == next) return;
        _hovered?.SetRayHover(false);
        _hovered = next;
        _hovered?.SetRayHover(true);
    }

    private void UpdateLine(bool visible, float distance, bool isButton)
    {
        Color color = isButton
            ? new Color(0.25f, 1f, 0.45f, 1f)
            : new Color(0.15f, 0.75f, 1f, 0.85f);

        if (lineRenderer != null)
        {
            lineRenderer.enabled = visible;
            if (visible)
            {
                lineRenderer.positionCount = 2;
                lineRenderer.SetPosition(0, rayOrigin.position);
                lineRenderer.SetPosition(
                    1,
                    rayOrigin.position + rayOrigin.forward * distance);
                lineRenderer.startColor = color;
                lineRenderer.endColor = color;
            }
        }

        // Keep the prebuilt mesh disabled on PICO. Enabling it and updating a
        // MaterialPropertyBlock during the first scene frame crashes this
        // project's GLES graphics worker. Raycasts and selection stay active.
        if (beamRenderer != null && beamRenderer.enabled)
        {
            beamRenderer.enabled = false;
        }
    }

    private void ActivateByHitName(Transform hitTransform)
    {
        if (layoutSelector == null || hitTransform == null) return;
        Transform current = hitTransform;
        while (current != null)
        {
            switch (current.name)
            {
                case "Button Low":
                    layoutSelector.Select(ExperimentLayoutSelection.Low);
                    return;
                case "Button Medium":
                    layoutSelector.Select(ExperimentLayoutSelection.Medium);
                    return;
                case "Button High":
                    layoutSelector.Select(ExperimentLayoutSelection.High);
                    return;
                case "Button Clear":
                    layoutSelector.Select(ExperimentLayoutSelection.Clear);
                    return;
            }
            current = current.parent;
        }
    }
}
