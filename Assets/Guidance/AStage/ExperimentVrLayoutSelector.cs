using UnityEngine;

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
        if (generator != null) UpdateStatus(FormatStatus(generator.CurrentSelection));
    }

    public void Configure(
        ExperimentDistractorLayoutGenerator layoutGenerator,
        TextMesh status,
        int seed)
    {
        generator = layoutGenerator;
        statusText = status;
        layoutSeed = seed;
        UpdateStatus(FormatStatus(generator.CurrentSelection));
    }

    public void Select(ExperimentLayoutSelection selection)
    {
        if (generator == null)
        {
            UpdateStatus("ERROR: generator missing");
            Debug.LogError("[VrLayoutSelector] Layout generator is missing.", this);
            return;
        }

        generator.ApplySelection(selection, "vr_layout_panel", layoutSeed);
        UpdateStatus(FormatStatus(selection));
    }

    private string FormatStatus(ExperimentLayoutSelection selection)
    {
        return selection == ExperimentLayoutSelection.Clear
            ? "Current: CLEAN (8 targets)"
            : $"Current: {selection.ToString().ToUpperInvariant()} " +
              $"({generator.GeneratedCount})";
    }

    private void UpdateStatus(string message)
    {
        if (statusText != null && statusText.text != message)
        {
            statusText.text = message;
        }
        Debug.Log($"[VrLayoutSelector] {message}", this);
    }
}
