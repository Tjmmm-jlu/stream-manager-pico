using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AStageSemanticCatalogInstaller
{
    // Revision 1: canonical apparatus semantics for the AStage experiment scene.
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string ComplexRootName = "complexSceneObj";
    private const string RequestPath = "Temp/AStageSemanticCatalog.request";
    private const string ReportPath = "Temp/AStageSemanticCatalog.txt";

    private static readonly IReadOnlyDictionary<string, TypeMetadata> Types =
        CreateTypeCatalog();

    private static readonly IReadOnlyDictionary<string, InstanceSpec> Targets =
        new Dictionary<string, InstanceSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["china_dish"] = Empty("china_dish", "china_dish"),
            ["graduated_cylinder"] = Empty(
                "graduated_cylinder", "graduated_cylinder"),
            ["beaker"] = Water("beaker", "beaker"),
            ["dropper"] = Empty("dropper", "dropper"),
            ["florence_flask"] = Water(
                "florence_flask", "florence_flask"),
            ["spirit_lamp"] = Water("spirit_lamp", "spirit_lamp"),
            ["test_tube_rack"] = Empty(
                "test_tube_rack", "test_tube_rack"),
            ["glass_funnel"] = Empty("glass_funnel", "glass_funnel")
        };

    private static readonly IReadOnlyDictionary<string, InstanceSpec> ComplexObjects =
        new Dictionary<string, InstanceSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["Beaker water"] = Water("complex_beaker_water", "beaker"),
            ["Glass_Lab_test_tube"] = Empty(
                "complex_test_tube_empty", "test_tube"),
            ["Glass_Lab_test_tube with liquid"] = Liquid(
                "complex_test_tube_liquid", "test_tube"),
            ["Glass_Lab_test_tube water"] = Water(
                "complex_test_tube_water", "test_tube"),
            ["florence_flask"] = Empty(
                "complex_florence_flask_empty", "florence_flask"),
            ["Crucible_Tongs"] = Empty(
                "complex_crucible_tongs", "crucible_tongs"),
            ["crucible_and_cover"] = Empty(
                "complex_crucible_with_cover", "crucible"),
            ["china dish"] = Empty(
                "complex_china_dish", "china_dish"),
            ["glass_funnel"] = Empty(
                "complex_glass_funnel", "glass_funnel"),
            ["Graduated_Cylinder with liquid"] = Liquid(
                "complex_graduated_cylinder_liquid", "graduated_cylinder"),
            ["Spirit_Lamp with water"] = Water(
                "complex_spirit_lamp_water", "spirit_lamp")
        };

    private static readonly IReadOnlyDictionary<string, string> GeneratorTypes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["beaker_distractor"] = "beaker",
            ["erlenmeyer_distractor"] = "erlenmeyer_flask",
            ["florence_distractor"] = "florence_flask",
            ["cylinder_distractor"] = "graduated_cylinder",
            ["dish_distractor"] = "china_dish",
            ["funnel_distractor"] = "glass_funnel",
            ["test_tube_distractor"] = "test_tube",
            ["crucible_distractor"] = "crucible",
            ["forceps_distractor"] = "forceps",
            ["tongs_distractor"] = "crucible_tongs"
        };

    [InitializeOnLoadMethod]
    private static void RunRequestedUpdate()
    {
        string request = Path.GetFullPath(RequestPath);
        if (!File.Exists(request))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedUpdate();
                return;
            }

            File.Delete(request);
            try
            {
                ApplyAndAudit();
            }
            catch (Exception exception)
            {
                File.WriteAllText(
                    Path.GetFullPath(ReportPath),
                    "FAILED\n" + exception);
                Debug.LogException(exception);
            }
        };
    }

    [MenuItem("Tools/Stream Manager/Semantics/Apply AStage Catalog")]
    public static void ApplyAndAudit()
    {
        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        if (!scene.IsValid() || !scene.isLoaded)
        {
            throw new InvalidOperationException($"Could not open {ScenePath}.");
        }

        EditorSceneManager.SaveScene(scene);
        string backupPath = CreateSceneBackup();

        SemanticObject[] existing = UnityEngine.Object
            .FindObjectsOfType<SemanticObject>(true);
        Dictionary<string, SemanticObject> existingById = existing
            .Where(item => item != null && !string.IsNullOrWhiteSpace(item.StableId))
            .ToDictionary(
                item => item.StableId,
                item => item,
                StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, InstanceSpec> target in Targets)
        {
            if (!existingById.TryGetValue(target.Key, out SemanticObject semantic))
            {
                throw new InvalidOperationException(
                    $"Existing target semantic '{target.Key}' was not found.");
            }

            ApplyMetadata(
                semantic,
                target.Value,
                semantic.Selectable,
                semantic.Grabbable);
        }

        GameObject complexRoot = scene.GetRootGameObjects()
            .SingleOrDefault(root => string.Equals(
                root.name, ComplexRootName, StringComparison.Ordinal));
        if (complexRoot == null)
        {
            throw new InvalidOperationException(
                $"Root object '{ComplexRootName}' was not found.");
        }

        Transform[] complexChildren = complexRoot.transform
            .Cast<Transform>()
            .ToArray();
        if (complexChildren.Length != ComplexObjects.Count)
        {
            throw new InvalidOperationException(
                $"Expected {ComplexObjects.Count} direct children under " +
                $"{ComplexRootName}, found {complexChildren.Length}.");
        }

        foreach (Transform child in complexChildren)
        {
            if (!ComplexObjects.TryGetValue(child.name, out InstanceSpec spec))
            {
                throw new InvalidOperationException(
                    $"No semantic definition exists for '{child.name}'.");
            }

            SemanticObject semantic = child.GetComponent<SemanticObject>() ??
                child.gameObject.AddComponent<SemanticObject>();
            ApplyMetadata(semantic, spec, true, false);
        }

        UpdateGeneratorDefinitions();

        SceneObjectRegistry registry = UnityEngine.Object
            .FindObjectOfType<SceneObjectRegistry>(true);
        if (registry == null)
        {
            throw new InvalidOperationException("SceneObjectRegistry was not found.");
        }

        registry.Refresh();
        EditorUtility.SetDirty(registry);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save the updated AStage scene.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        SemanticObject[] catalog = UnityEngine.Object
            .FindObjectsOfType<SemanticObject>(true)
            .OrderBy(item => item.StableId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ValidateCatalog(catalog, complexRoot, registry);
        WriteReport(catalog, backupPath, registry.Count);

        Debug.Log(
            $"[AStageSemanticCatalog] Applied and audited {catalog.Length} " +
            $"semantic apparatus. Backup: {backupPath}");
    }

    private static void ApplyMetadata(
        SemanticObject semantic,
        InstanceSpec instance,
        bool selectable,
        bool grabbable)
    {
        TypeMetadata type = Types[instance.ObjectType];
        semantic.ConfigureDetailed(
            instance.StableId,
            type.DisplayName,
            instance.ObjectType,
            type.Category,
            type.Function,
            type.Color,
            type.Transparency,
            type.Shape,
            type.Materials,
            instance.Contents,
            instance.ContentColor,
            instance.StateTags,
            type.Aliases,
            selectable,
            grabbable);
        EditorUtility.SetDirty(semantic);
    }

    private static void UpdateGeneratorDefinitions()
    {
        ExperimentDistractorLayoutGenerator generator = UnityEngine.Object
            .FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        if (generator == null)
        {
            return;
        }

        foreach (ExperimentDistractorDefinition definition in generator.Definitions)
        {
            if (definition == null ||
                !GeneratorTypes.TryGetValue(definition.idPrefix, out string objectType))
            {
                continue;
            }

            TypeMetadata type = Types[objectType];
            definition.displayName = type.DisplayName;
            definition.objectType = objectType;
            definition.category = type.Category;
            definition.function = type.Function;
            definition.color = type.Color;
            definition.transparency = type.Transparency;
            definition.shape = type.Shape;
            definition.materials = type.Materials.ToList();
            definition.contents = "none";
            definition.contentColor = "none";
            definition.stateTags = new List<string> { "empty" };
            definition.aliases = type.Aliases.ToList();
        }

        EditorUtility.SetDirty(generator);
    }

    private static void ValidateCatalog(
        IReadOnlyList<SemanticObject> catalog,
        GameObject complexRoot,
        SceneObjectRegistry registry)
    {
        int expected = Targets.Count + ComplexObjects.Count;
        if (catalog.Count != expected || registry.Count != expected)
        {
            throw new InvalidOperationException(
                $"Expected {expected} semantic objects, found catalog={catalog.Count}, " +
                $"registry={registry.Count}.");
        }

        string[] duplicateIds = catalog
            .GroupBy(item => item.StableId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIds.Length > 0)
        {
            throw new InvalidOperationException(
                "Duplicate Stable IDs: " + string.Join(", ", duplicateIds));
        }

        foreach (SemanticObject semantic in catalog)
        {
            if (string.IsNullOrWhiteSpace(semantic.StableId) ||
                string.IsNullOrWhiteSpace(semantic.DisplayName) ||
                string.IsNullOrWhiteSpace(semantic.ObjectType) ||
                string.IsNullOrWhiteSpace(semantic.Category) ||
                string.IsNullOrWhiteSpace(semantic.Function) ||
                string.IsNullOrWhiteSpace(semantic.Color) ||
                string.IsNullOrWhiteSpace(semantic.Transparency) ||
                string.IsNullOrWhiteSpace(semantic.Shape) ||
                semantic.Materials.Count == 0 ||
                string.IsNullOrWhiteSpace(semantic.Contents) ||
                string.IsNullOrWhiteSpace(semantic.ContentColor) ||
                semantic.StateTags.Count == 0 ||
                semantic.Aliases.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Incomplete semantic block on '{GetHierarchyPath(semantic.transform)}'.");
            }
        }

        foreach (Transform child in complexRoot.transform)
        {
            if (child.GetComponents<SemanticObject>().Length != 1)
            {
                throw new InvalidOperationException(
                    $"'{child.name}' must have exactly one SemanticObject component.");
            }
        }
    }

    private static string CreateSceneBackup()
    {
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string backupPath =
            $"Assets/Scenes/Preparation Experiment Scene_AStage_" +
            $"BeforeSemanticCatalog_{timestamp}.unity";
        if (!AssetDatabase.CopyAsset(ScenePath, backupPath))
        {
            throw new InvalidOperationException(
                $"Could not create scene backup at {backupPath}.");
        }

        AssetDatabase.SaveAssets();
        return backupPath;
    }

    private static void WriteReport(
        IReadOnlyList<SemanticObject> catalog,
        string backupPath,
        int registryCount)
    {
        var lines = new List<string>
        {
            "PASS",
            $"Scene: {ScenePath}",
            $"Backup: {backupPath}",
            $"Semantic objects: {catalog.Count}",
            $"Registry count: {registryCount}",
            $"Existing targets updated: {Targets.Count}",
            $"complexSceneObj objects configured: {ComplexObjects.Count}",
            string.Empty,
            "stableId | objectType | category | function | color | transparency | contents | hierarchy"
        };

        lines.AddRange(catalog.Select(item => string.Join(
            " | ",
            item.StableId,
            item.ObjectType,
            item.Category,
            item.Function,
            item.Color,
            item.Transparency,
            item.Contents,
            GetHierarchyPath(item.transform))));
        File.WriteAllLines(Path.GetFullPath(ReportPath), lines);
    }

    private static string GetHierarchyPath(Transform transform)
    {
        var names = new Stack<string>();
        Transform current = transform;
        while (current != null)
        {
            names.Push(current.name);
            current = current.parent;
        }
        return string.Join("/", names);
    }

    private static InstanceSpec Empty(string stableId, string objectType)
    {
        return new InstanceSpec(
            stableId,
            objectType,
            "none",
            "none",
            new[] { "empty" });
    }

    private static InstanceSpec Water(string stableId, string objectType)
    {
        return new InstanceSpec(
            stableId,
            objectType,
            "water",
            "colorless",
            new[] { "contains_liquid", "contains_water" });
    }

    private static InstanceSpec Liquid(string stableId, string objectType)
    {
        return new InstanceSpec(
            stableId,
            objectType,
            "liquid",
            "unspecified",
            new[] { "contains_liquid" });
    }

    private static IReadOnlyDictionary<string, TypeMetadata> CreateTypeCatalog()
    {
        return new Dictionary<string, TypeMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["beaker"] = new TypeMetadata(
                "烧杯", "container", "mixing", "colorless", "transparent",
                "wide_cylinder", new[] { "glass" },
                "烧杯", "玻璃烧杯", "透明烧杯", "杯状容器",
                "beaker", "glass beaker"),
            ["china_dish"] = new TypeMetadata(
                "蒸发皿", "container", "evaporating", "white", "opaque",
                "shallow_dish", new[] { "ceramic" },
                "蒸发皿", "瓷皿", "白色小皿", "蒸发盘",
                "china dish", "evaporating dish", "porcelain dish"),
            ["crucible"] = new TypeMetadata(
                "坩埚", "container", "heating", "white", "opaque",
                "small_bowl", new[] { "ceramic" },
                "坩埚", "带盖坩埚", "瓷坩埚", "crucible",
                "covered crucible", "porcelain crucible"),
            ["crucible_tongs"] = new TypeMetadata(
                "坩埚钳", "tool", "handling", "silver", "opaque",
                "scissor_tongs", new[] { "metal" },
                "坩埚钳", "坩埚夹", "夹钳", "crucible tongs"),
            ["dropper"] = new TypeMetadata(
                "玻璃滴管", "tool", "transferring", "colorless", "transparent",
                "slender_tube", new[] { "glass", "rubber" },
                "滴管", "玻璃滴管", "滴液管", "透明滴管",
                "dropper", "glass dropper"),
            ["erlenmeyer_flask"] = new TypeMetadata(
                "锥形瓶", "container", "mixing", "colorless", "transparent",
                "conical_flask", new[] { "glass" },
                "锥形瓶", "三角瓶", "锥形烧瓶",
                "Erlenmeyer flask", "conical flask"),
            ["florence_flask"] = new TypeMetadata(
                "佛罗伦萨烧瓶", "container", "heating", "colorless", "transparent",
                "spherical_flask", new[] { "glass" },
                "佛罗伦萨烧瓶", "平底烧瓶", "沸腾烧瓶", "烧瓶",
                "Florence flask", "boiling flask"),
            ["forceps"] = new TypeMetadata(
                "镊子", "tool", "handling", "silver", "opaque",
                "slender_tool", new[] { "metal" },
                "镊子", "金属镊子", "forceps", "tweezers"),
            ["glass_funnel"] = new TypeMetadata(
                "玻璃漏斗", "tool", "filtering", "colorless", "transparent",
                "funnel", new[] { "glass" },
                "玻璃漏斗", "漏斗", "过滤漏斗", "透明漏斗",
                "glass funnel", "filter funnel", "funnel"),
            ["graduated_cylinder"] = new TypeMetadata(
                "量筒", "container", "measuring", "colorless", "transparent",
                "tall_cylinder", new[] { "glass" },
                "量筒", "刻度量筒", "刻度筒", "透明量筒",
                "graduated cylinder", "measuring cylinder"),
            ["spirit_lamp"] = new TypeMetadata(
                "酒精灯", "device", "heating", "silver", "opaque",
                "lamp", new[] { "metal", "glass" },
                "酒精灯", "加热灯", "spirit lamp", "alcohol lamp",
                "heating lamp"),
            ["test_tube"] = new TypeMetadata(
                "试管", "container", "holding", "colorless", "transparent",
                "slender_tube", new[] { "glass" },
                "试管", "玻璃试管", "透明试管", "test tube", "glass test tube"),
            ["test_tube_rack"] = new TypeMetadata(
                "试管架", "support", "supporting", "brown", "opaque",
                "rectangular_rack", new[] { "wood" },
                "试管架", "试管支架", "管架", "木制试管架",
                "test tube rack", "tube rack")
        };
    }

    private sealed class TypeMetadata
    {
        public TypeMetadata(
            string displayName,
            string category,
            string function,
            string color,
            string transparency,
            string shape,
            string[] materials,
            params string[] aliases)
        {
            DisplayName = displayName;
            Category = category;
            Function = function;
            Color = color;
            Transparency = transparency;
            Shape = shape;
            Materials = materials;
            Aliases = aliases;
        }

        public string DisplayName { get; }
        public string Category { get; }
        public string Function { get; }
        public string Color { get; }
        public string Transparency { get; }
        public string Shape { get; }
        public string[] Materials { get; }
        public string[] Aliases { get; }
    }

    private sealed class InstanceSpec
    {
        public InstanceSpec(
            string stableId,
            string objectType,
            string contents,
            string contentColor,
            string[] stateTags)
        {
            StableId = stableId;
            ObjectType = objectType;
            Contents = contents;
            ContentColor = contentColor;
            StateTags = stateTags;
        }

        public string StableId { get; }
        public string ObjectType { get; }
        public string Contents { get; }
        public string ContentColor { get; }
        public string[] StateTags { get; }
    }
}
