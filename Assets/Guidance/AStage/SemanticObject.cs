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
    [SerializeField] private string objectType;

    [Header("Semantic attributes")]
    [SerializeField] private string category;
    [SerializeField] private string function;
    [SerializeField] private string color;
    [SerializeField] private string transparency;
    [SerializeField] private string shape;
    [SerializeField] private List<string> materials = new List<string>();
    [SerializeField] private string contents;
    [SerializeField] private string contentColor;
    [SerializeField] private List<string> stateTags = new List<string>();
    [SerializeField] private List<string> aliases = new List<string>();

    [Header("Experiment capabilities")]
    [SerializeField] private bool selectable = true;
    [SerializeField] private bool grabbable = true;

    public string StableId => stableId;
    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? gameObject.name : displayName;
    public string ObjectType => objectType;
    public string Category => category;
    public string Function => function;
    public string Color => color;
    public string Transparency => transparency;
    public string Shape => shape;
    public IReadOnlyList<string> Materials => materials;
    public string Contents => contents;
    public string ContentColor => contentColor;
    public IReadOnlyList<string> StateTags => stateTags;
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
        string legacyCategory = NormalizeToken(objectCategory);
        bool legacyTransparent = string.Equals(
            objectColor?.Trim(), "transparent",
            StringComparison.OrdinalIgnoreCase);
        string canonicalType = string.IsNullOrWhiteSpace(objectType)
            ? NormalizeId(id)
            : objectType;
        IEnumerable<string> canonicalMaterials =
            materials != null && materials.Count > 0
                ? materials
                : InferMaterials(canonicalType);
        IEnumerable<string> canonicalStateTags =
            stateTags != null && stateTags.Count > 0
                ? stateTags
                : new[] { "empty" };
        ConfigureDetailed(
            id,
            objectDisplayName,
            canonicalType,
            InferCategory(legacyCategory),
            string.IsNullOrWhiteSpace(function)
                ? InferFunction(legacyCategory)
                : function,
            legacyTransparent ? "colorless" : objectColor,
            string.IsNullOrWhiteSpace(transparency)
                ? legacyTransparent ? "transparent" : "opaque"
                : transparency,
            objectShape,
            canonicalMaterials,
            string.IsNullOrWhiteSpace(contents) ? "none" : contents,
            string.IsNullOrWhiteSpace(contentColor) ? "none" : contentColor,
            canonicalStateTags,
            objectAliases,
            canSelect,
            canGrab);
    }

    public void ConfigureDetailed(
        string id,
        string objectDisplayName,
        string canonicalObjectType,
        string objectCategory,
        string objectFunction,
        string objectColor,
        string objectTransparency,
        string objectShape,
        IEnumerable<string> objectMaterials,
        string objectContents,
        string objectContentColor,
        IEnumerable<string> objectStateTags,
        IEnumerable<string> objectAliases,
        bool canSelect,
        bool canGrab)
    {
        stableId = NormalizeId(id);
        displayName = objectDisplayName?.Trim() ?? string.Empty;
        objectType = NormalizeToken(canonicalObjectType);
        category = NormalizeToken(objectCategory);
        function = NormalizeToken(objectFunction);
        color = NormalizeToken(objectColor);
        transparency = NormalizeToken(objectTransparency);
        shape = NormalizeToken(objectShape);
        materials = NormalizeValues(objectMaterials, true);
        contents = NormalizeToken(objectContents);
        contentColor = NormalizeToken(objectContentColor);
        stateTags = NormalizeValues(objectStateTags, true);
        aliases = NormalizeValues(objectAliases, false);
        selectable = canSelect;
        grabbable = canGrab;
    }

    public bool Matches(
        string query,
        string requiredCategory = null,
        string requiredColor = null)
    {
        if (!MatchesAttribute(category, requiredCategory) ||
            !MatchesAnyAttribute(requiredColor, color, transparency, contentColor))
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
            new[]
                {
                    stableId, DisplayName, objectType, category, function,
                    color, transparency, shape, contents, contentColor,
                    gameObject.name
                }
                .Concat(materials)
                .Concat(stateTags)
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
        objectType = NormalizeToken(objectType);
        category = NormalizeToken(category);
        function = NormalizeToken(function);
        color = NormalizeToken(color);
        transparency = NormalizeToken(transparency);
        shape = NormalizeToken(shape);
        materials = NormalizeValues(materials, true);
        contents = NormalizeToken(contents);
        contentColor = NormalizeToken(contentColor);
        stateTags = NormalizeValues(stateTags, true);
        aliases = NormalizeValues(aliases, false);
    }

    private static bool MatchesAttribute(string value, string required)
    {
        return string.IsNullOrWhiteSpace(required) ||
               string.Equals(
                   value?.Trim(), required.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAnyAttribute(
        string required,
        params string[] values)
    {
        return string.IsNullOrWhiteSpace(required) ||
               values.Any(value => MatchesAttribute(value, required));
    }

    private static List<string> NormalizeValues(
        IEnumerable<string> values,
        bool canonical)
    {
        if (values == null)
        {
            return new List<string>();
        }

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => canonical
                ? NormalizeToken(value)
                : value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string NormalizeToken(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string InferCategory(string legacyCategory)
    {
        if (legacyCategory.EndsWith("_tool", StringComparison.Ordinal))
        {
            return "tool";
        }

        switch (legacyCategory)
        {
            case "measuring_container": return "container";
            case "heating_device": return "device";
            default: return legacyCategory;
        }
    }

    private static string InferFunction(string legacyCategory)
    {
        switch (legacyCategory)
        {
            case "measuring_container": return "measuring";
            case "heating_device": return "heating";
            case "filtering_tool": return "filtering";
            case "transfer_tool": return "transferring";
            case "handling_tool": return "handling";
            case "support": return "supporting";
            case "container": return "holding";
            default: return string.Empty;
        }
    }

    private static IEnumerable<string> InferMaterials(string canonicalType)
    {
        switch (NormalizeToken(canonicalType))
        {
            case "china_dish":
            case "crucible":
                return new[] { "ceramic" };
            case "test_tube_rack":
                return new[] { "wood" };
            case "spirit_lamp":
                return new[] { "metal", "glass" };
            case "crucible_tongs":
            case "forceps":
                return new[] { "metal" };
            default:
                return new[] { "glass" };
        }
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
