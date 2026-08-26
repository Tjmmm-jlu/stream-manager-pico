using UnityEngine;

[RequireComponent(typeof(Collider))]
[DisallowMultipleComponent]
public sealed class ExperimentVrLayoutButton : MonoBehaviour
{
    [SerializeField] private ExperimentVrLayoutSelector selector;
    [SerializeField] private ExperimentLayoutSelection selection;
    [SerializeField] private Renderer visual;
    [SerializeField] private Color normalColor = new Color(0.15f, 0.45f, 0.85f);
    [SerializeField] private Color hoverColor = new Color(0.3f, 0.85f, 1f);

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
        hoverColor = Color.Lerp(color, Color.white, 0.32f);
    }

    private void OnEnable()
    {
        normalColor = GetHighContrastColor(selection);
        hoverColor = Color.Lerp(normalColor, Color.white, 0.32f);
    }

    public void ActivateFromRay()
    {
        selector?.Select(selection);
    }

    public void SetRayHover(bool hovered)
    {
        // The button materials are serialized by the editor installer. Avoid
        // runtime renderer property writes on the PICO GLES graphics worker.
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

}
