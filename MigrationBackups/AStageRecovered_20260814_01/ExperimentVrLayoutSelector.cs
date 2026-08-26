using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public enum ExperimentLayoutSelection
{
    Low,
    Medium,
    High,
    Clear
}

[DisallowMultipleComponent]
public sealed class ExperimentVrLayoutSelector : MonoBehaviour
{
    [SerializeField] private ExperimentDistractorLayoutGenerator generator;
    [SerializeField] private TextMesh statusText;
    [SerializeField] private int layoutSeed = 20260811;

    private void Awake()
    {
        // Keep status readable in the restored, build-safe scene without adding
        // new serialized material objects to level0.
        if (statusText == null)
        {
            Transform statusTransform = transform.Find("Status");
            if (statusTransform != null) statusText = statusTransform.GetComponent<TextMesh>();
        }
        if (statusText != null) statusText.color = new Color(0.25f, 0.9f, 1f, 1f);
    }

    public void Configure(
        ExperimentDistractorLayoutGenerator layoutGenerator,
        TextMesh status,
        int seed)
    {
        generator = layoutGenerator;
        statusText = status;
        layoutSeed = seed;
        UpdateStatus("Current: CLEAN (8 targets)");
    }

    public void Select(ExperimentLayoutSelection selection)
    {
        if (generator == null)
        {
            UpdateStatus("ERROR: generator missing");
            Debug.LogError("[VrLayoutSelector] Layout generator is missing.", this);
            return;
        }

        switch (selection)
        {
            case ExperimentLayoutSelection.Low:
                generator.Generate(ExperimentLayoutComplexity.Low, layoutSeed);
                UpdateStatus($"Current: LOW ({generator.GeneratedCount})");
                break;
            case ExperimentLayoutSelection.Medium:
                generator.Generate(ExperimentLayoutComplexity.Medium, layoutSeed);
                UpdateStatus($"Current: MEDIUM ({generator.GeneratedCount})");
                break;
            case ExperimentLayoutSelection.High:
                generator.Generate(ExperimentLayoutComplexity.High, layoutSeed);
                UpdateStatus($"Current: HIGH ({generator.GeneratedCount})");
                break;
            default:
                generator.ClearGenerated();
                UpdateStatus("Current: CLEAN (8 targets)");
                break;
        }
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
        Debug.Log($"[VrLayoutSelector] {message}", this);
    }
}

[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(XRSimpleInteractable))]
[DisallowMultipleComponent]
public sealed class ExperimentVrLayoutButton : MonoBehaviour
{
    [SerializeField] private ExperimentVrLayoutSelector selector;
    [SerializeField] private ExperimentLayoutSelection selection;
    [SerializeField] private Renderer visual;
    [SerializeField] private Color normalColor = new Color(0.15f, 0.45f, 0.85f);
    [SerializeField] private Color hoverColor = new Color(0.3f, 0.85f, 1f);

    private XRSimpleInteractable _interactable;
    private MaterialPropertyBlock _propertyBlock;

    public void Configure(
        ExperimentVrLayoutSelector layoutSelector,
        ExperimentLayoutSelection layoutSelection,
        Renderer targetVisual,
        Color color)
    {
        selector = layoutSelector;
        selection = layoutSelection;
        visual = targetVisual;
        normalColor = color;
        // Keep the category colour recognizable while making ray hover obvious.
        hoverColor = Color.Lerp(color, Color.white, 0.32f);
        ApplyColor(normalColor);
    }

    private void OnEnable()
    {
        normalColor = GetHighContrastColor(selection);
        hoverColor = Color.Lerp(normalColor, Color.white, 0.32f);
        _interactable = GetComponent<XRSimpleInteractable>();
        _interactable.hoverEntered.AddListener(OnHoverEntered);
        _interactable.hoverExited.AddListener(OnHoverExited);
        _interactable.selectEntered.AddListener(OnSelectEntered);
        ApplyColor(normalColor);
    }

    private static Color GetHighContrastColor(ExperimentLayoutSelection value)
    {
        switch (value)
        {
            case ExperimentLayoutSelection.Low:
                return new Color(0f, 0.52f, 1f, 1f);
            case ExperimentLayoutSelection.Medium:
                return new Color(0f, 0.82f, 0.28f, 1f);
            case ExperimentLayoutSelection.High:
                return new Color(1f, 0.38f, 0f, 1f);
            default:
                return new Color(0.95f, 0.02f, 0.12f, 1f);
        }
    }

    private void OnDisable()
    {
        if (_interactable == null) return;
        _interactable.hoverEntered.RemoveListener(OnHoverEntered);
        _interactable.hoverExited.RemoveListener(OnHoverExited);
        _interactable.selectEntered.RemoveListener(OnSelectEntered);
    }

    private void OnHoverEntered(HoverEnterEventArgs args)
    {
        ApplyColor(hoverColor);
    }

    private void OnHoverExited(HoverExitEventArgs args)
    {
        ApplyColor(normalColor);
    }

    private void OnSelectEntered(SelectEnterEventArgs args)
    {
        ActivateFromRay();
    }

    public void ActivateFromRay()
    {
        selector?.Select(selection);
    }

    public void SetRayHover(bool hovered)
    {
        ApplyColor(hovered ? hoverColor : normalColor);
    }

    private void ApplyColor(Color color)
    {
        if (visual == null) return;
        _propertyBlock ??= new MaterialPropertyBlock();
        visual.GetPropertyBlock(_propertyBlock);
        _propertyBlock.SetColor("_Color", color);
        _propertyBlock.SetColor("_BaseColor", color);
        visual.SetPropertyBlock(_propertyBlock);
    }
}
