using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SemanticObject : MonoBehaviour
{
    [Header("Stable identity")]
    [SerializeField] private string stableId;
    [SerializeField] private string displayName;

    [Header("Semantic attributes")]
    [SerializeField] private string category;
    [SerializeField] private string color;
    [SerializeField] private string shape;
    [SerializeField] private List<string> aliases = new List<string>();

    [Header("Experiment capabilities")]
    [SerializeField] private bool selectable = true;
    [SerializeField] private bool grabbable = true;

    public string StableId => stableId;
    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public string Category => category;
    public string Color => color;
    public string Shape => shape;
    public IReadOnlyList<string> Aliases => aliases;
    public bool Selectable => selectable;
    public bool Grabbable => grabbable;

    public void Configure(
        string id,
        string objectDisplayName,
        string objectCategory,
        string objectColor,
        string objectShape,
        IEnumerable<string> objectAliases,
        bool canSelect,
        bool canGrab)
    {
        stableId = NormalizeId(id);
        displayName = objectDisplayName?.Trim() ?? string.Empty;
        category = objectCategory?.Trim() ?? string.Empty;
        color = objectColor?.Trim() ?? string.Empty;
        shape = objectShape?.Trim() ?? string.Empty;
        aliases = objectAliases == null
            ? new List<string>()
            : objectAliases
                .Where(alias => !string.IsNullOrWhiteSpace(alias))
                .Select(alias => alias.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        selectable = canSelect;
        grabbable = canGrab;
    }

    public bool Matches(
        string query,
        string requiredCategory = null,
        string requiredColor = null)
    {
        if (!MatchesAttribute(category, requiredCategory) ||
            !MatchesAttribute(color, requiredColor))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        string[] tokens = query
            .Split(new[] { ' ', '\t', ',', '，', ';', '；' },
                StringSplitOptions.RemoveEmptyEntries);
        string searchText = string.Join(
            " ",
            new[] { stableId, DisplayName, category, color, shape, gameObject.name }
                .Concat(aliases)
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();

        return tokens.All(token =>
            searchText.Contains(token.Trim().ToLowerInvariant()));
    }

    private void OnValidate()
    {
        stableId = NormalizeId(stableId);
        displayName = displayName?.Trim() ?? string.Empty;
        category = category?.Trim() ?? string.Empty;
        color = color?.Trim() ?? string.Empty;
        shape = shape?.Trim() ?? string.Empty;
    }

    private static bool MatchesAttribute(string value, string required)
    {
        return string.IsNullOrWhiteSpace(required) ||
               string.Equals(
                   value?.Trim(), required.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
