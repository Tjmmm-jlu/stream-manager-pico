using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public enum ExperimentLayoutComplexity
{
    Low,
    Medium,
    High
}

[Serializable]
public sealed class ExperimentDistractorDefinition
{
    public GameObject prefab;
    public string idPrefix = "distractor";
    public string displayName;
    public string objectType;
    public string category;
    public string function;
    public string color;
    public string transparency;
    public string shape;
    public List<string> materials = new List<string>();
    public string contents = "none";
    public string contentColor = "none";
    public List<string> stateTags = new List<string>();
    public List<string> aliases = new List<string>();
    [Min(0.01f)] public float scale = 1f;
}

[DisallowMultipleComponent]
public sealed class GeneratedDistractorMarker : MonoBehaviour
{
    [SerializeField] private int seed;
    [SerializeField] private int sequenceIndex;
    [SerializeField] private string definitionPrefix;

    public void Configure(int layoutSeed, int index, string prefix)
    {
        seed = layoutSeed;
        sequenceIndex = index;
        definitionPrefix = prefix;
    }
}

[DisallowMultipleComponent]
public sealed class ExperimentDistractorLayoutGenerator : MonoBehaviour
{
    private const string ContainerName = "Generated Distractors";

    [Header("Placement area")]
    [Tooltip("The XZ rectangle used to place distractors. Its Y position is the tabletop height.")]
    [SerializeField] private Transform placementArea;
    [Tooltip("Renderer of the actual tabletop. The placement area is refreshed from its bounds before generation.")]
    [SerializeField] private Renderer placementSurface;
    [SerializeField] private Vector2 areaSize = new Vector2(1.6f, 0.9f);
    [Min(0f)] [SerializeField] private float edgePadding = 0.04f;

    [Header("Controlled density")]
    [Min(0)] [SerializeField] private int lowCount = 4;
    [Min(0)] [SerializeField] private int mediumCount = 8;
    [Min(0)] [SerializeField] private int highCount = 10;
    [SerializeField] private ExperimentLayoutComplexity complexity =
        ExperimentLayoutComplexity.Low;
    [SerializeField] private int randomSeed = 20260811;

    [Header("Separation")]
    [Min(0f)] [SerializeField] private float minimumSpacing = 0.04f;
    [Min(0f)] [SerializeField] private float targetClearance = 0.08f;
    [Min(1)] [SerializeField] private int attemptsPerObject = 80;

    [Header("Semantic distractor pool")]
    [SerializeField] private List<ExperimentDistractorDefinition> definitions =
        new List<ExperimentDistractorDefinition>();

    [Header("Runtime policy")]
    [Tooltip("Keep disabled for controlled experiments; generate explicitly before each trial.")]
    [SerializeField] private bool generateOnStart;

    private ExperimentLayoutSelection _currentSelection =
        ExperimentLayoutSelection.Clear;

    public Transform PlacementArea => placementArea;
    public Renderer PlacementSurface => placementSurface;
    public Vector2 AreaSize => areaSize;
    public int RandomSeed => randomSeed;
    public int GeneratedCount => GetComponentsInChildren<GeneratedDistractorMarker>(true)
        .Count(marker => marker != null && marker.gameObject.activeInHierarchy);
    public IReadOnlyList<ExperimentDistractorDefinition> Definitions => definitions;
    public ExperimentLayoutSelection CurrentSelection => _currentSelection;

    private void Start()
    {
        if (generateOnStart)
        {
            Generate(complexity, randomSeed);
        }
    }

    public void Configure(
        Transform area,
        Renderer surface,
        Vector2 size,
        IEnumerable<ExperimentDistractorDefinition> pool,
        int low,
        int medium,
        int high,
        int seed)
    {
        placementArea = area;
        placementSurface = surface;
        areaSize = new Vector2(Mathf.Max(0.1f, size.x), Mathf.Max(0.1f, size.y));
        definitions = pool?.Where(item => item != null).ToList() ??
            new List<ExperimentDistractorDefinition>();
        lowCount = Mathf.Max(0, low);
        mediumCount = Mathf.Max(lowCount, medium);
        highCount = Mathf.Max(mediumCount, high);
        randomSeed = seed;
        generateOnStart = false;
    }

    [ContextMenu("Generate Current Complexity")]
    public void GenerateCurrent()
    {
        Generate(complexity, randomSeed);
    }

    public void ApplySelection(
        ExperimentLayoutSelection selection,
        string source = "runtime",
        int? seed = null)
    {
        int requestedSeed = seed ?? randomSeed;
        switch (selection)
        {
            case ExperimentLayoutSelection.Low:
                Generate(ExperimentLayoutComplexity.Low, requestedSeed);
                break;
            case ExperimentLayoutSelection.Medium:
                Generate(ExperimentLayoutComplexity.Medium, requestedSeed);
                break;
            case ExperimentLayoutSelection.High:
                Generate(ExperimentLayoutComplexity.High, requestedSeed);
                break;
            default:
                ClearGenerated();
                break;
        }

        _currentSelection = selection;

        string message =
            $"selection={selection};generated={GeneratedCount};" +
            $"seed={randomSeed};source={source}";
        FindObjectOfType<ExperimentLogger>(true)?.LogEvent(
            "layout_changed", message);
        Debug.Log($"[DistractorLayout] {message}", this);
    }

    public void Generate(ExperimentLayoutComplexity requestedComplexity, int seed)
    {
        if (placementArea == null)
        {
            Debug.LogError("[DistractorLayout] Placement area is not assigned.", this);
            return;
        }

        RefreshPlacementAreaFromSurface();

        List<ExperimentDistractorDefinition> usableDefinitions = definitions
            .Where(item => item != null && item.prefab != null)
            .ToList();
        if (usableDefinitions.Count == 0)
        {
            Debug.LogError("[DistractorLayout] No distractor prefabs are configured.", this);
            return;
        }

        ClearGenerated();
        complexity = requestedComplexity;
        randomSeed = seed;

        Transform container = GetOrCreateContainer();
        var random = new System.Random(seed);
        var occupied = CollectProtectedTargetFootprints();
        int requestedCount = GetCount(requestedComplexity);
        int generated = 0;

        for (int index = 0; index < requestedCount; index++)
        {
            bool placed = false;
            // A randomly chosen large model may not fit late in a dense
            // layout even though a smaller model still can. Rotate through
            // multiple definitions before declaring the slot unavailable.
            int definitionAttempts = Mathf.Max(usableDefinitions.Count * 3, 12);
            for (int definitionAttempt = 0;
                 definitionAttempt < definitionAttempts && !placed;
                 definitionAttempt++)
            {
                ExperimentDistractorDefinition definition =
                    usableDefinitions[random.Next(usableDefinitions.Count)];
                placed = TryPlace(
                    definition, index, seed, random, container, occupied);
            }

            if (!placed)
            {
                Debug.LogWarning(
                    $"[DistractorLayout] Could not fill slot {index + 1}/{requestedCount}; " +
                    "increase the area or reduce density/spacing.",
                    this);
                break;
            }

            generated++;
        }

        SceneObjectRegistry registry = FindObjectOfType<SceneObjectRegistry>(true);
        registry?.Refresh();
        Debug.Log(
            $"[DistractorLayout] Generated {generated}/{requestedCount} " +
            $"distractors for {requestedComplexity}, seed={seed}. Registry=" +
            $"{registry?.Count ?? 0}.",
            this);
    }

    [ContextMenu("Clear Generated Distractors")]
    public void ClearGenerated()
    {
        GeneratedDistractorMarker[] markers =
            GetComponentsInChildren<GeneratedDistractorMarker>(true);
        foreach (GeneratedDistractorMarker marker in markers)
        {
            if (marker != null)
            {
                DestroySafe(marker.gameObject);
            }
        }

        Transform container = transform.Find(ContainerName);
        if (container != null && container.childCount == 0)
        {
            DestroySafe(container.gameObject);
        }

        SceneObjectRegistry registry = FindObjectOfType<SceneObjectRegistry>(true);
        registry?.Refresh();
    }

    private bool TryPlace(
        ExperimentDistractorDefinition definition,
        int index,
        int seed,
        System.Random random,
        Transform container,
        List<Footprint> occupied)
    {
        for (int attempt = 0; attempt < attemptsPerObject; attempt++)
        {
            float halfX = Mathf.Max(0f, areaSize.x * 0.5f - edgePadding);
            float halfZ = Mathf.Max(0f, areaSize.y * 0.5f - edgePadding);
            float localX = Mathf.Lerp(-halfX, halfX, (float)random.NextDouble());
            float localZ = Mathf.Lerp(-halfZ, halfZ, (float)random.NextDouble());
            Vector3 worldPosition = placementArea.TransformPoint(localX, 0f, localZ);
            Quaternion worldRotation = Quaternion.Euler(
                0f,
                placementArea.eulerAngles.y + (float)random.NextDouble() * 360f,
                0f);

            GameObject instance = Instantiate(
                definition.prefab,
                worldPosition,
                worldRotation,
                container);
            instance.name = $"Distractor_{index + 1:000}_{definition.prefab.name}";
            instance.transform.localScale *= Mathf.Max(0.01f, definition.scale);

            Bounds bounds = CalculateBounds(instance);
            float tabletopY = placementArea.position.y;
            instance.transform.position += Vector3.up * (tabletopY - bounds.min.y);
            bounds = CalculateBounds(instance);

            if (!IsFullyInsidePlacementArea(bounds))
            {
                DestroySafe(instance);
                continue;
            }

            Vector2 center = new Vector2(bounds.center.x, bounds.center.z);
            float radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            bool overlaps = occupied.Any(item =>
                Vector2.Distance(center, item.Center) <
                radius + item.Radius + minimumSpacing +
                (item.ProtectedTarget ? targetClearance : 0f));
            if (overlaps)
            {
                DestroySafe(instance);
                continue;
            }

            EnsureCollider(instance);
            string prefix = NormalizeId(definition.idPrefix);
            string stableId = $"{prefix}_{seed}_{index + 1:000}";
            SemanticObject semantic = instance.GetComponent<SemanticObject>() ??
                instance.AddComponent<SemanticObject>();
            string canonicalType = string.IsNullOrWhiteSpace(definition.objectType)
                ? InferObjectType(prefix)
                : definition.objectType;
            string canonicalCategory = string.IsNullOrWhiteSpace(definition.function)
                ? InferCategory(definition.category)
                : definition.category;
            string canonicalFunction = string.IsNullOrWhiteSpace(definition.function)
                ? InferFunction(definition.category)
                : definition.function;
            bool legacyTransparent = string.Equals(
                definition.color, "transparent",
                StringComparison.OrdinalIgnoreCase);
            IEnumerable<string> canonicalMaterials =
                definition.materials != null && definition.materials.Count > 0
                    ? definition.materials
                    : InferMaterials(canonicalType);
            IEnumerable<string> canonicalStateTags =
                definition.stateTags != null && definition.stateTags.Count > 0
                    ? definition.stateTags
                    : new[] { "empty" };
            semantic.ConfigureDetailed(
                stableId,
                string.IsNullOrWhiteSpace(definition.displayName)
                    ? definition.prefab.name
                    : definition.displayName,
                canonicalType,
                canonicalCategory,
                canonicalFunction,
                legacyTransparent ? "colorless" : definition.color,
                string.IsNullOrWhiteSpace(definition.transparency)
                    ? legacyTransparent ? "transparent" : "opaque"
                    : definition.transparency,
                definition.shape,
                canonicalMaterials,
                string.IsNullOrWhiteSpace(definition.contents)
                    ? "none"
                    : definition.contents,
                string.IsNullOrWhiteSpace(definition.contentColor)
                    ? "none"
                    : definition.contentColor,
                canonicalStateTags,
                definition.aliases,
                true,
                false);
            GeneratedDistractorMarker marker =
                instance.AddComponent<GeneratedDistractorMarker>();
            marker.Configure(seed, index, prefix);
            occupied.Add(new Footprint(center, radius, false));
            return true;
        }

        return false;
    }

    private List<Footprint> CollectProtectedTargetFootprints()
    {
        var result = new List<Footprint>();
        PreparationSequenceController sequence =
            FindObjectOfType<PreparationSequenceController>(true);
        if (sequence == null)
        {
            return result;
        }

        foreach (PreparationSequenceController.Step step in sequence.Steps)
        {
            if (step?.target == null)
            {
                continue;
            }

            Bounds bounds = CalculateBounds(step.target);
            result.Add(new Footprint(
                new Vector2(bounds.center.x, bounds.center.z),
                Mathf.Max(bounds.extents.x, bounds.extents.z),
                true));
        }
        return result;
    }

    private Transform GetOrCreateContainer()
    {
        Transform container = transform.Find(ContainerName);
        if (container != null)
        {
            return container;
        }

        var containerObject = new GameObject(ContainerName);
        containerObject.transform.SetParent(transform, false);
        return containerObject.transform;
    }

    private int GetCount(ExperimentLayoutComplexity value)
    {
        switch (value)
        {
            case ExperimentLayoutComplexity.Medium:
                return mediumCount;
            case ExperimentLayoutComplexity.High:
                return highCount;
            default:
                return lowCount;
        }
    }

    private void RefreshPlacementAreaFromSurface()
    {
        if (placementSurface == null || placementArea == null)
        {
            return;
        }

        Bounds surfaceBounds = placementSurface.bounds;
        placementArea.position = new Vector3(
            surfaceBounds.center.x,
            surfaceBounds.max.y,
            surfaceBounds.center.z);
        placementArea.rotation = Quaternion.identity;
        placementArea.localScale = Vector3.one;
        areaSize = new Vector2(surfaceBounds.size.x, surfaceBounds.size.z);
    }

    private bool IsFullyInsidePlacementArea(Bounds worldBounds)
    {
        float halfX = Mathf.Max(0f, areaSize.x * 0.5f - edgePadding);
        float halfZ = Mathf.Max(0f, areaSize.y * 0.5f - edgePadding);
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        var corners = new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };
        foreach (Vector3 corner in corners)
        {
            Vector3 local = placementArea.InverseTransformPoint(corner);
            if (local.x < -halfX || local.x > halfX ||
                local.z < -halfZ || local.z > halfZ)
            {
                return false;
            }
        }
        return true;
    }

    private static Bounds CalculateBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }
            return bounds;
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        if (colliders.Length > 0)
        {
            Bounds bounds = colliders[0].bounds;
            for (int index = 1; index < colliders.Length; index++)
            {
                bounds.Encapsulate(colliders[index].bounds);
            }
            return bounds;
        }

        return new Bounds(target.transform.position, Vector3.one * 0.05f);
    }

    private static void EnsureCollider(GameObject target)
    {
        if (target.GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        Bounds worldBounds = CalculateBounds(target);
        Vector3 min = worldBounds.min;
        Vector3 max = worldBounds.max;
        var corners = new[]
        {
            new Vector3(min.x, min.y, min.z),
            new Vector3(min.x, min.y, max.z),
            new Vector3(min.x, max.y, min.z),
            new Vector3(min.x, max.y, max.z),
            new Vector3(max.x, min.y, min.z),
            new Vector3(max.x, min.y, max.z),
            new Vector3(max.x, max.y, min.z),
            new Vector3(max.x, max.y, max.z)
        };
        Vector3 firstLocal = target.transform.InverseTransformPoint(corners[0]);
        var localBounds = new Bounds(firstLocal, Vector3.zero);
        for (int index = 1; index < corners.Length; index++)
        {
            localBounds.Encapsulate(
                target.transform.InverseTransformPoint(corners[index]));
        }

        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.center = localBounds.center;
        collider.size = localBounds.size;
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "distractor"
            : value.Trim().ToLowerInvariant().Replace(' ', '_');
    }

    private static string InferObjectType(string prefix)
    {
        string value = NormalizeId(prefix).Replace("_distractor", string.Empty);
        switch (value)
        {
            case "cylinder": return "graduated_cylinder";
            case "dish": return "china_dish";
            case "erlenmeyer": return "erlenmeyer_flask";
            case "florence": return "florence_flask";
            case "funnel": return "glass_funnel";
            case "tongs": return "crucible_tongs";
            default: return value;
        }
    }

    private static string InferCategory(string legacyCategory)
    {
        string value = NormalizeId(legacyCategory);
        if (value.EndsWith("_tool", StringComparison.Ordinal))
        {
            return "tool";
        }

        switch (value)
        {
            case "measuring_container": return "container";
            default: return value;
        }
    }

    private static string InferFunction(string legacyCategory)
    {
        switch (NormalizeId(legacyCategory))
        {
            case "measuring_container": return "measuring";
            case "filtering_tool": return "filtering";
            case "transfer_tool": return "transferring";
            case "handling_tool": return "handling";
            case "container": return "holding";
            case "support": return "supporting";
            default: return string.Empty;
        }
    }

    private static IEnumerable<string> InferMaterials(string objectType)
    {
        switch (NormalizeId(objectType))
        {
            case "china_dish":
            case "crucible":
                return new[] { "ceramic" };
            case "forceps":
            case "crucible_tongs":
                return new[] { "metal" };
            default:
                return new[] { "glass" };
        }
    }

    private static void DestroySafe(UnityEngine.Object target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            // Stop old renderers and colliders synchronously, then let Unity
            // release native graphics resources at its safe end-of-frame point.
            if (target is GameObject gameObject)
            {
                gameObject.SetActive(false);
            }
            else if (target is Component component)
            {
                component.gameObject.SetActive(false);
            }

            Destroy(target);
            return;
        }

        DestroyImmediate(target);
    }

    private void OnDrawGizmosSelected()
    {
        if (placementArea == null)
        {
            return;
        }

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;
        Gizmos.matrix = Matrix4x4.TRS(
            placementArea.position,
            Quaternion.Euler(0f, placementArea.eulerAngles.y, 0f),
            Vector3.one);
        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(areaSize.x, 0.01f, areaSize.y));
        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }

    private readonly struct Footprint
    {
        public Footprint(Vector2 center, float radius, bool protectedTarget)
        {
            Center = center;
            Radius = Mathf.Max(0.01f, radius);
            ProtectedTarget = protectedTarget;
        }

        public Vector2 Center { get; }
        public float Radius { get; }
        public bool ProtectedTarget { get; }
    }
}
