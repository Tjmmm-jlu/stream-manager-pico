using System;
using System.Collections.Generic;
using UnityEngine;

// Preserve Highlight=3 so existing serialized AStage scenes remain valid.
public enum GuidanceMode
{
    None = 0,
    Highlight = 3
}

/// <summary>
/// Operator-side cue used by the collaboration experiments.
/// The receiving cue is intentionally fixed to object highlighting so the
/// experiment isolates expert-side language and gaze input.
/// </summary>
public sealed class GuidanceManager : MonoBehaviour
{
    [Header("Collaboration experiment cue")]
    [SerializeField] private GuidanceMode initialMode = GuidanceMode.Highlight;
    [SerializeField] private Color highlightColor = Color.yellow;

    private GameObject _currentTarget;
    private GameObject _highlightedTarget;
    private GuidanceMode _activeMode;
    private readonly Dictionary<Renderer, MaterialPropertyBlock>
        _originalPropertyBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

    private static readonly int ColorProperty =
        Shader.PropertyToID("_Color");
    private static readonly int BaseColorProperty =
        Shader.PropertyToID("_BaseColor");

    public event Action<GameObject, GuidanceMode> TargetShown;
    public event Action TargetCleared;

    // Kept as no-op compatibility properties for old editor installers. The
    // highlight-only manager does not create camera-facing overlay geometry.
    public Camera GuidanceCamera { get; set; }
    public Material OverlayMaterial { get; set; }

    public GuidanceMode ActiveMode => _activeMode;
    public GameObject CurrentTarget => _currentTarget;

    private void Awake()
    {
        _activeMode = initialMode == GuidanceMode.Highlight
            ? GuidanceMode.Highlight
            : GuidanceMode.None;
    }

    private void OnDisable()
    {
        ClearHighlight();
    }

    private void OnDestroy()
    {
        ClearHighlight();
    }

    public void SetMode(GuidanceMode mode)
    {
        _activeMode = mode == GuidanceMode.Highlight
            ? GuidanceMode.Highlight
            : GuidanceMode.None;
        RefreshHighlight();
        Debug.Log($"[Guidance] Mode changed to {GetModeName(_activeMode)}.");
    }

    public bool TrySetMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return false;
        }

        switch (mode.Trim().ToLowerInvariant())
        {
            case "none":
            case "off":
                SetMode(GuidanceMode.None);
                return true;
            case "highlight":
            case "highlighting":
                SetMode(GuidanceMode.Highlight);
                return true;
            default:
                return false;
        }
    }

    public void ShowTarget(GameObject target)
    {
        if (target == null)
        {
            ClearTarget();
            return;
        }

        if (_currentTarget != target)
        {
            ClearHighlight();
            _currentTarget = target;
        }

        RefreshHighlight();
        TargetShown?.Invoke(target, _activeMode);
        Debug.Log(
            $"[Guidance] Target={target.name} mode={GetModeName(_activeMode)} " +
            $"time={Time.realtimeSinceStartupAsDouble:F6}");
    }

    public void ClearTarget()
    {
        bool hadTarget = _currentTarget != null;
        ClearHighlight();
        _currentTarget = null;
        if (!hadTarget)
        {
            return;
        }

        TargetCleared?.Invoke();
        Debug.Log(
            $"[Guidance] Target cleared at " +
            $"{Time.realtimeSinceStartupAsDouble:F6}.");
    }

    public static string GetModeName(GuidanceMode mode)
    {
        return mode == GuidanceMode.Highlight ? "highlight" : "none";
    }

    private void RefreshHighlight()
    {
        if (_activeMode == GuidanceMode.Highlight && _currentTarget != null)
        {
            ApplyHighlight(_currentTarget);
        }
        else
        {
            ClearHighlight();
        }
    }

    private void ApplyHighlight(GameObject target)
    {
        if (target == null || _highlightedTarget == target)
        {
            return;
        }

        ClearHighlight();
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer)
            {
                continue;
            }

            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock);
            _originalPropertyBlocks[renderer] = originalBlock;

            var highlightBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(highlightBlock);
            highlightBlock.SetColor(ColorProperty, highlightColor);
            highlightBlock.SetColor(BaseColorProperty, highlightColor);
            renderer.SetPropertyBlock(highlightBlock);
        }

        _highlightedTarget = target;
    }

    private void ClearHighlight()
    {
        foreach (KeyValuePair<Renderer, MaterialPropertyBlock> entry
                 in _originalPropertyBlocks)
        {
            if (entry.Key != null)
            {
                entry.Key.SetPropertyBlock(entry.Value);
            }
        }

        _originalPropertyBlocks.Clear();
        _highlightedTarget = null;
    }
}
