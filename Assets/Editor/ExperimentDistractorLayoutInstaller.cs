using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentDistractorLayoutInstaller
{
    private const string ScenePath =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath =
        "Temp/ExperimentDistractorLayoutInstaller.request";
    private const string ReportPath =
        "Temp/ExperimentDistractorLayoutInstaller.txt";
    private const string GeneratorName = "Experiment Distractor Layout";
    private const string AreaName = "Distractor Spawn Area";
    private const int DefaultSeed = 20260811;

    private const string PrefabFolder =
        "Assets/3D Laboratory Environment with Appratus/Prefabs/";

    [InitializeOnLoadMethod]
    private static void RunRequestedInstallation()
    {
        if (!File.Exists(Path.GetFullPath(RequestPath)))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedInstallation();
                return;
            }

            InstallOrRefresh();
            File.Delete(Path.GetFullPath(RequestPath));
        };
    }

    [MenuItem("Tools/Stream Manager/Distractors/Install or Refresh Generator")]
    public static void InstallOrRefresh()
    {
        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        PreparationSequenceController sequence =
            UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        if (sequence == null)
        {
            throw new InvalidOperationException(
                "PreparationSequenceController was not found.");
        }

        List<GameObject> targets = sequence.Steps
            .Where(step => step?.target != null)
            .Select(step => step.target)
            .Distinct()
            .ToList();
        if (targets.Count != 8)
        {
            throw new InvalidOperationException(
                $"Expected 8 protected targets, found {targets.Count}.");
        }

        GameObject generatorObject = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == GeneratorName);
        if (generatorObject == null)
        {
            generatorObject = new GameObject(GeneratorName);
        }

        ExperimentDistractorLayoutGenerator generator =
            generatorObject.GetComponent<ExperimentDistractorLayoutGenerator>() ??
            generatorObject.AddComponent<ExperimentDistractorLayoutGenerator>();
        generator.ClearGenerated();
        int orphanedDistractorsRemoved = RemoveOrphanedDistractors();

        Transform area = generatorObject.transform.Find(AreaName);
        if (area == null)
        {
            var areaObject = new GameObject(AreaName);
            area = areaObject.transform;
            area.SetParent(generatorObject.transform, false);
        }

        Bounds combinedBounds = CalculateCombinedBounds(targets);
        GameObject tabletopRoot = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name == "shelf (1)");
        if (tabletopRoot == null)
        {
            throw new InvalidOperationException("Required tabletop root 'shelf (1)' was not found.");
        }
        Renderer tabletop = tabletopRoot.GetComponentsInChildren<Renderer>(true)
            .OrderByDescending(renderer => renderer.bounds.size.x * renderer.bounds.size.z)
            .FirstOrDefault();
        if (tabletop == null)
        {
            throw new InvalidOperationException("The tabletop has no Renderer.");
        }
        Bounds tabletopBounds = tabletop.bounds;
        area.position = new Vector3(
            tabletopBounds.center.x,
            tabletopBounds.max.y,
            tabletopBounds.center.z);
        area.rotation = Quaternion.identity;
        area.localScale = Vector3.one;

        // The initial area surrounds the eight targets. It is deliberately
        // editable in the Inspector and shown as a cyan gizmo.
        Vector2 areaSize = new Vector2(
            tabletopBounds.size.x,
            tabletopBounds.size.z);
        List<ExperimentDistractorDefinition> definitions = BuildDefinitions();
        ExperimentLayoutRegion region =
            area.GetComponent<ExperimentLayoutRegion>() ??
            area.gameObject.AddComponent<ExperimentLayoutRegion>();
        region.Configure(tabletop, areaSize, 0.04f);
        generator.Configure(
            area,
            tabletop,
            areaSize,
            definitions,
            4,
            8,
            10,
            DefaultSeed);
        generator.SetLayoutRegion(region);

        SceneObjectRegistry registry =
            UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        if (registry == null)
        {
            throw new InvalidOperationException("SceneObjectRegistry was not found.");
        }
        registry.Refresh();
        if (registry.Count != 8 || generator.GeneratedCount != 0)
        {
            throw new InvalidOperationException(
                $"Clean base validation failed. Registry={registry.Count}, " +
                $"generated={generator.GeneratedCount}.");
        }

        EditorUtility.SetDirty(generator);
        EditorUtility.SetDirty(area);
        EditorUtility.SetDirty(region);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
        {
            throw new InvalidOperationException("Could not save generator setup.");
        }

        AssetDatabase.SaveAssets();
        var report = new[]
        {
            $"Scene: {ScenePath}",
            $"Generator: {GeneratorName}",
            $"Placement center: {area.position}",
            $"Placement size XZ: {areaSize}",
            $"Placement surface: {tabletopRoot.name}/{tabletop.name}",
            "Counts: low=4, medium=8, high=10",
            $"Seed: {DefaultSeed}",
            $"Distractor definitions: {definitions.Count}",
            $"Orphaned distractors removed: {orphanedDistractorsRemoved}",
            $"Current generated distractors: {generator.GeneratedCount}",
            $"Current semantic registry count: {registry.Count}",
            "Generate on start: false"
        };
        File.WriteAllLines(Path.GetFullPath(ReportPath), report);
        Debug.Log(
            "[DistractorLayoutInstaller] Generator configured. The clean base " +
            "contains zero distractors; use the Distractors menu to generate a layout.");
    }

    private static int RemoveOrphanedDistractors()
    {
        SemanticObject[] orphaned = UnityEngine.Object
            .FindObjectsOfType<SemanticObject>(true)
            .Where(item => item != null &&
                item.StableId.IndexOf("_distractor_", StringComparison.OrdinalIgnoreCase) >= 0)
            .ToArray();
        foreach (SemanticObject semantic in orphaned)
        {
            UnityEngine.Object.DestroyImmediate(semantic.gameObject);
        }
        return orphaned.Length;
    }

    [MenuItem("Tools/Stream Manager/Distractors/Generate Low (4)")]
    private static void GenerateLow()
    {
        Generate(ExperimentLayoutComplexity.Low);
    }

    [MenuItem("Tools/Stream Manager/Distractors/Generate Medium (8)")]
    private static void GenerateMedium()
    {
        Generate(ExperimentLayoutComplexity.Medium);
    }

    [MenuItem("Tools/Stream Manager/Distractors/Generate High (10)")]
    private static void GenerateHigh()
    {
        Generate(ExperimentLayoutComplexity.High);
    }

    [MenuItem("Tools/Stream Manager/Distractors/Clear Generated")]
    private static void ClearGenerated()
    {
        ExperimentDistractorLayoutGenerator generator = GetGenerator();
        generator.ClearGenerated();
        SaveActiveScene();
    }

    private static void Generate(ExperimentLayoutComplexity complexity)
    {
        ExperimentDistractorLayoutGenerator generator = GetGenerator();
        generator.Generate(complexity, generator.RandomSeed);
        SaveActiveScene();
    }

    private static ExperimentDistractorLayoutGenerator GetGenerator()
    {
        if (!string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase))
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        ExperimentDistractorLayoutGenerator generator =
            UnityEngine.Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        if (generator == null)
        {
            throw new InvalidOperationException(
                "Distractor generator is not installed. Run Install or Refresh Generator first.");
        }
        return generator;
    }

    private static void SaveActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
    }

    private static List<ExperimentDistractorDefinition> BuildDefinitions()
    {
        return new List<ExperimentDistractorDefinition>
        {
            Definition("Beaker.prefab", "beaker_distractor", "Beaker", "container", "transparent", "wide_cylinder", "烧杯", "玻璃烧杯", "beaker"),
            Definition("Erlenmeyer_flask.prefab", "erlenmeyer_distractor", "Erlenmeyer flask", "container", "transparent", "conical_flask", "锥形瓶", "三角瓶", "Erlenmeyer flask"),
            Definition("florence_flask.prefab", "florence_distractor", "Florence flask", "container", "transparent", "round_flask", "圆底烧瓶", "烧瓶", "Florence flask"),
            Definition("Graduated_Cylinder.prefab", "cylinder_distractor", "Graduated cylinder", "measuring_container", "transparent", "tall_cylinder", "量筒", "刻度筒", "graduated cylinder"),
            Definition("china dish.prefab", "dish_distractor", "China dish", "container", "white", "shallow_dish", "蒸发皿", "瓷皿", "china dish"),
            Definition("glass_funnel.prefab", "funnel_distractor", "Glass funnel", "transfer_tool", "transparent", "cone", "玻璃漏斗", "漏斗", "glass funnel"),
            Definition("Glass_Lab_test_tube.prefab", "test_tube_distractor", "Test tube", "container", "transparent", "slender_tube", "试管", "玻璃试管", "test tube"),
            Definition("crucible_and_cover.prefab", "crucible_distractor", "Crucible", "container", "white", "small_bowl", "坩埚", "带盖坩埚", "crucible"),
            Definition("forceps.prefab", "forceps_distractor", "Forceps", "handling_tool", "metallic", "slender_tool", "镊子", "夹具", "forceps"),
            Definition("Crucible_Tongs.prefab", "tongs_distractor", "Crucible tongs", "handling_tool", "metallic", "scissor_tongs", "坩埚钳", "夹钳", "crucible tongs")
        };
    }

    private static ExperimentDistractorDefinition Definition(
        string prefabName,
        string prefix,
        string displayName,
        string category,
        string color,
        string shape,
        params string[] aliases)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            PrefabFolder + prefabName);
        if (prefab == null)
        {
            throw new FileNotFoundException(
                $"Distractor prefab was not found: {PrefabFolder}{prefabName}");
        }

        return new ExperimentDistractorDefinition
        {
            prefab = prefab,
            idPrefix = prefix,
            displayName = displayName,
            category = category,
            color = color,
            shape = shape,
            aliases = aliases.ToList(),
            scale = 1f
        };
    }

    private static Bounds CalculateCombinedBounds(IEnumerable<GameObject> targets)
    {
        Bounds[] bounds = targets.Select(CalculateBounds).ToArray();
        Bounds combined = bounds[0];
        for (int index = 1; index < bounds.Length; index++)
        {
            combined.Encapsulate(bounds[index]);
        }
        return combined;
    }

    private static Bounds CalculateBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(target.transform.position, Vector3.one * 0.05f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }
}
