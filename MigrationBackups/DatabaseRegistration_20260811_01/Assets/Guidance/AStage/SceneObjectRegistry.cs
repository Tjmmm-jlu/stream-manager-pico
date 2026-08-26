using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[Serializable]
public sealed class SemanticObjectSummary
{
    public string id;
    public string displayName;
    public string category;
    public string color;
    public string shape;
    public string[] aliases;
    public bool selectable;
    public bool grabbable;

    public static SemanticObjectSummary From(SemanticObject semanticObject)
    {
        return new SemanticObjectSummary
        {
            id = semanticObject.StableId,
            displayName = semanticObject.DisplayName,
            category = semanticObject.Category,
            color = semanticObject.Color,
            shape = semanticObject.Shape,
            aliases = semanticObject.Aliases.ToArray(),
            selectable = semanticObject.Selectable,
            grabbable = semanticObject.Grabbable
        };
    }
}

[DisallowMultipleComponent]
public sealed class SceneObjectRegistry : MonoBehaviour
{
    [SerializeField] private bool includeInactiveObjects = true;
    [SerializeField] private bool logCatalogOnStart;

    private readonly Dictionary<string, SemanticObject> _objectsById =
        new Dictionary<string, SemanticObject>(StringComparer.OrdinalIgnoreCase);
    private readonly List<SemanticObject> _objects = new List<SemanticObject>();

    public static SceneObjectRegistry Instance { get; private set; }
    public IReadOnlyList<SemanticObject> Objects => _objects;
    public int Count => _objects.Count;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[SemanticRegistry] More than one registry exists.");
        }
        Instance = this;
        Refresh();
    }

    private void Start()
    {
        if (logCatalogOnStart)
        {
            Debug.Log(
                $"[SemanticRegistry] Ready with {Count} objects: " +
                string.Join(", ", _objects.Select(item => item.StableId)));
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    public void Refresh()
    {
        _objects.Clear();
        _objectsById.Clear();

        SemanticObject[] sceneObjects =
            FindObjectsOfType<SemanticObject>(includeInactiveObjects);
        foreach (SemanticObject sceneObject in sceneObjects
                     .OrderBy(item => item.StableId, StringComparer.OrdinalIgnoreCase))
        {
            if (sceneObject == null ||
                string.IsNullOrWhiteSpace(sceneObject.StableId))
            {
                Debug.LogWarning(
                    $"[SemanticRegistry] Ignored object without stable ID: " +
                    $"{sceneObject?.name ?? "null"}.");
                continue;
            }

            if (_objectsById.ContainsKey(sceneObject.StableId))
            {
                Debug.LogError(
                    $"[SemanticRegistry] Duplicate stable ID " +
                    $"'{sceneObject.StableId}' on {sceneObject.name}.");
                continue;
            }

            _objectsById.Add(sceneObject.StableId, sceneObject);
            _objects.Add(sceneObject);
        }
    }

    public bool TryGet(string stableId, out SemanticObject semanticObject)
    {
        semanticObject = null;
        return !string.IsNullOrWhiteSpace(stableId) &&
               _objectsById.TryGetValue(stableId.Trim(), out semanticObject);
    }

    public List<SemanticObject> FindCandidates(
        string query,
        string category,
        string color,
        bool selectableOnly = true)
    {
        return _objects
            .Where(item => item != null)
            .Where(item => !selectableOnly || item.Selectable)
            .Where(item => item.Matches(query, category, color))
            .OrderBy(item => item.StableId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public SemanticObjectSummary[] GetCatalog()
    {
        return _objects.Select(SemanticObjectSummary.From).ToArray();
    }
}
