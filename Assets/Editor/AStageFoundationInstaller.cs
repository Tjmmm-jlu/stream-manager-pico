using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public static class AStageFoundationInstaller
{
    // Revision 4.2: register the eight restored laboratory targets with a
    // canonical English vocabulary and bilingual expert-facing aliases.
    private const string SourceScene =
        "Assets/Scenes/Preparation Experiment Scene.unity";
    private const string DevelopmentScene =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string InstallRequestFile =
        "Temp/AStageFoundation.install-request";

    private static readonly string[] RequiredSemanticIds =
    {
        "china_dish",
        "graduated_cylinder",
        "beaker",
        "dropper",
        "florence_flask",
        "spirit_lamp",
        "test_tube_rack",
        "glass_funnel"
    };

    [InitializeOnLoadMethod]
    private static void QueueRequestedInstallation()
    {
        string requestPath = Path.GetFullPath(InstallRequestFile);
        if (!File.Exists(requestPath))
        {
            return;
        }

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                QueueRequestedInstallation();
                return;
            }

            try
            {
                Install();
                File.Delete(requestPath);
                Debug.Log("[AStageInstaller] Requested installation completed.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        };
    }

    private readonly struct Metadata
    {
        public Metadata(
            string displayName,
            string category,
            string color,
            string shape,
            params string[] aliases)
        {
            DisplayName = displayName;
            Category = category;
            Color = color;
            Shape = shape;
            Aliases = aliases;
        }

        public string DisplayName { get; }
        public string Category { get; }
        public string Color { get; }
        public string Shape { get; }
        public string[] Aliases { get; }
    }

    [MenuItem("Tools/Stream Manager/Install A-Stage Foundation")]
    public static void InstallFromMenu()
    {
        Install();
        EditorUtility.DisplayDialog(
            "A-Stage Foundation",
            "A 阶段基础已安装到开发场景：\n" + DevelopmentScene,
            "确定");
    }

    public static void InstallBatch()
    {
        try
        {
            Install();
            Debug.Log("[AStageInstaller] Batch installation completed.");
            EditorApplication.Exit(0);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            EditorApplication.Exit(1);
        }
    }

    private static void Install()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
        {
            throw new InvalidOperationException(
                $"Source scene not found: {SourceScene}");
        }

        Scene activeScene = SceneManager.GetActiveScene();
        bool activeSceneIsSource = string.Equals(
            activeScene.path, SourceScene, StringComparison.OrdinalIgnoreCase);
        if (activeSceneIsSource)
        {
            // Preserve the exact scene currently visible in the user's editor,
            // including unsaved changes, while keeping the source asset intact.
            if (!EditorSceneManager.SaveScene(
                    activeScene, DevelopmentScene, false))
            {
                throw new InvalidOperationException(
                    $"Could not save the active scene as {DevelopmentScene}");
            }
            AssetDatabase.Refresh();
        }
        else if (AssetDatabase.LoadAssetAtPath<SceneAsset>(DevelopmentScene) == null)
        {
            if (!AssetDatabase.CopyAsset(SourceScene, DevelopmentScene))
            {
                throw new InvalidOperationException(
                    $"Could not create development scene: {DevelopmentScene}");
            }
            AssetDatabase.SaveAssets();
        }

        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                DevelopmentScene,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(DevelopmentScene, OpenSceneMode.Single);
        GuidanceManager guidanceManager =
            UnityEngine.Object.FindObjectOfType<GuidanceManager>(true);
        PreparationSequenceController sequence =
            UnityEngine.Object.FindObjectOfType<PreparationSequenceController>(true);
        GuidanceDataChannelReceiver receiver =
            UnityEngine.Object.FindObjectOfType<GuidanceDataChannelReceiver>(true);

        if (guidanceManager == null || sequence == null || receiver == null)
        {
            throw new InvalidOperationException(
                "The preparation scene is missing GuidanceManager, " +
                "PreparationSequenceController, or GuidanceDataChannelReceiver.");
        }

        GameObject experimentRoot = guidanceManager.gameObject;
        SceneObjectRegistry registry =
            GetOrAdd<SceneObjectRegistry>(experimentRoot);
        ExperimentStateController stateController =
            GetOrAdd<ExperimentStateController>(experimentRoot);
        ExperimentLogger experimentLogger =
            GetOrAdd<ExperimentLogger>(experimentRoot);
        GetOrAdd<ExperimentInteractionTracker>(experimentRoot);

        sequence.ShowTargetAutomatically = false;
        experimentLogger.StateController = stateController;
        receiver.GuidanceManager = guidanceManager;
        receiver.PreparationSequence = sequence;
        receiver.ObjectRegistry = registry;
        receiver.StateController = stateController;
        receiver.ExperimentLogger = experimentLogger;

        var guidanceSerialized = new SerializedObject(guidanceManager);
        SerializedProperty initialMode =
            guidanceSerialized.FindProperty("initialMode");
        if (initialMode != null)
        {
            initialMode.enumValueIndex = (int)GuidanceMode.Highlight;
            guidanceSerialized.ApplyModifiedPropertiesWithoutUndo();
        }

        int configuredCount = ConfigureSemanticTargets(sequence);
        registry.Refresh();

        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(registry);
        EditorUtility.SetDirty(stateController);
        EditorUtility.SetDirty(experimentLogger);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        ActivateDevelopmentSceneInBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        ValidateInstallation(configuredCount, sequence, receiver, registry);
        Debug.Log(
            $"[AStageInstaller] Installed {configuredCount} semantic targets " +
            $"in {DevelopmentScene}. Automatic target cues are disabled.");
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static int ConfigureSemanticTargets(
        PreparationSequenceController sequence)
    {
        int count = 0;
        foreach (PreparationSequenceController.Step step in sequence.Steps)
        {
            if (step == null || step.target == null ||
                string.IsNullOrWhiteSpace(step.id))
            {
                continue;
            }

            SemanticObject semanticObject =
                GetOrAdd<SemanticObject>(step.target);
            Metadata metadata = GetMetadata(step.id, step.target.name);
            bool grabbable =
                step.target.GetComponentInChildren<XRGrabInteractable>(true) != null;
            semanticObject.Configure(
                step.id,
                metadata.DisplayName,
                metadata.Category,
                metadata.Color,
                metadata.Shape,
                metadata.Aliases,
                true,
                grabbable);
            EditorUtility.SetDirty(semanticObject);
            count++;
        }
        return count;
    }

    private static Metadata GetMetadata(string id, string fallbackName)
    {
        switch (id.Trim().ToLowerInvariant())
        {
            case "china_dish":
                return new Metadata(
                    "蒸发皿", "container", "white", "shallow_dish",
                    "蒸发皿", "瓷皿", "白色小皿", "蒸发盘",
                    "china dish", "evaporating dish", "porcelain dish");
            case "graduated_cylinder":
                return new Metadata(
                    "量筒", "measuring_container", "transparent", "tall_cylinder",
                    "量筒", "刻度筒", "透明量筒",
                    "graduated cylinder", "measuring cylinder", "cylinder");
            case "beaker":
                return new Metadata(
                    "烧杯", "container", "transparent", "wide_cylinder",
                    "烧杯", "玻璃烧杯", "透明烧杯", "杯状容器",
                    "beaker", "glass beaker");
            case "dropper":
                return new Metadata(
                    "玻璃滴管", "transfer_tool", "transparent", "slender_tube",
                    "滴管", "玻璃滴管", "滴液管", "透明滴管",
                    "dropper", "glass dropper");
            case "florence_flask":
                return new Metadata(
                    "佛罗伦萨烧瓶", "container", "transparent", "spherical_flask",
                    "佛罗伦萨烧瓶", "平底烧瓶", "烧瓶", "沸腾烧瓶",
                    "florence flask", "boiling flask", "flask");
            case "spirit_lamp":
                return new Metadata(
                    "酒精灯", "heating_device", "metallic", "lamp",
                    "酒精灯", "加热灯", "灯",
                    "spirit lamp", "alcohol lamp", "heating lamp");
            case "test_tube_rack":
                return new Metadata(
                    "试管架", "support", "brown", "rectangular_rack",
                    "试管架", "试管支架", "管架", "木架",
                    "test tube rack", "tube rack", "rack");
            case "glass_funnel":
                return new Metadata(
                    "玻璃漏斗", "filtering_tool", "transparent", "funnel",
                    "玻璃漏斗", "漏斗", "过滤漏斗", "透明漏斗",
                    "glass funnel", "filter funnel", "funnel");
            case "empty_beaker":
                return new Metadata(
                    "空烧杯", "容器", "透明", "圆柱形",
                    "烧杯", "空杯", "beaker", "empty beaker");
            case "reagent_a":
                return new Metadata(
                    "试剂 A", "试剂瓶", "蓝色", "瓶形",
                    "A试剂", "蓝色试剂", "reagent A", "blue bottle");
            case "graduated_cylinder_legacy_unused":
                return new Metadata(
                    "量筒", "测量容器", "透明", "细长圆柱形",
                    "量杯", "刻度筒", "graduated cylinder", "cylinder");
            case "digital_scale":
                return new Metadata(
                    "电子秤", "仪器", "灰色", "长方体",
                    "秤", "天平", "digital scale", "scale");
            case "reagent_b":
                return new Metadata(
                    "试剂 B", "试剂瓶", "绿色", "瓶形",
                    "B试剂", "绿色试剂", "reagent B", "green bottle");
            case "pipette":
                return new Metadata(
                    "移液器", "工具", "蓝色", "细长形",
                    "移液枪", "滴管", "pipette", "dropper");
            case "stirring_rod":
                return new Metadata(
                    "搅拌棒", "工具", "银色", "细长杆形",
                    "玻璃棒", "搅拌杆", "stirring rod", "rod");
            case "result_tray":
                return new Metadata(
                    "成品托盘", "托盘", "绿色", "长方形",
                    "绿色托盘", "结果托盘", "result tray", "green tray");
            default:
                return new Metadata(
                    fallbackName, "未分类", string.Empty, string.Empty,
                    fallbackName);
        }
    }

    private static void ActivateDevelopmentSceneInBuildSettings()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        bool developmentSceneFound = false;
        for (int index = 0; index < scenes.Count; index++)
        {
            EditorBuildSettingsScene entry = scenes[index];
            if (string.Equals(
                    entry.path, SourceScene,
                    StringComparison.OrdinalIgnoreCase))
            {
                scenes[index] = new EditorBuildSettingsScene(entry.path, false);
            }
            else if (string.Equals(
                         entry.path, DevelopmentScene,
                         StringComparison.OrdinalIgnoreCase))
            {
                scenes[index] = new EditorBuildSettingsScene(entry.path, true);
                developmentSceneFound = true;
            }
            else if (entry.enabled)
            {
                scenes[index] = new EditorBuildSettingsScene(entry.path, false);
            }
        }

        if (!developmentSceneFound)
        {
            scenes.Add(new EditorBuildSettingsScene(DevelopmentScene, true));
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void ValidateInstallation(
        int configuredCount,
        PreparationSequenceController sequence,
        GuidanceDataChannelReceiver receiver,
        SceneObjectRegistry registry)
    {
        if (configuredCount != sequence.StepCount || configuredCount == 0)
        {
            throw new InvalidOperationException(
                $"Only {configuredCount}/{sequence.StepCount} steps received semantic IDs.");
        }
        if (sequence.ShowTargetAutomatically)
        {
            throw new InvalidOperationException(
                "Automatic step target highlighting is still enabled.");
        }
        if (receiver.ObjectRegistry == null ||
            receiver.StateController == null ||
            receiver.ExperimentLogger == null)
        {
            throw new InvalidOperationException(
                "Guidance receiver experiment references are incomplete.");
        }
        if (registry.Count != configuredCount)
        {
            throw new InvalidOperationException(
                $"Registry contains {registry.Count} objects; expected {configuredCount}.");
        }
        if (configuredCount != RequiredSemanticIds.Length)
        {
            throw new InvalidOperationException(
                $"Configured {configuredCount} targets; expected exactly " +
                $"{RequiredSemanticIds.Length} required laboratory targets.");
        }
        foreach (string id in RequiredSemanticIds)
        {
            if (!registry.TryGet(id, out SemanticObject semanticObject))
            {
                throw new InvalidOperationException(
                    $"Required semantic target '{id}' was not registered.");
            }

            Metadata expected = GetMetadata(id, semanticObject.name);
            bool metadataMatches =
                string.Equals(semanticObject.DisplayName, expected.DisplayName,
                    StringComparison.Ordinal) &&
                string.Equals(semanticObject.Category, expected.Category,
                    StringComparison.Ordinal) &&
                string.Equals(semanticObject.Color, expected.Color,
                    StringComparison.Ordinal) &&
                string.Equals(semanticObject.Shape, expected.Shape,
                    StringComparison.Ordinal) &&
                expected.Aliases.All(alias =>
                    semanticObject.Aliases.Contains(
                        alias, StringComparer.OrdinalIgnoreCase));
            if (!metadataMatches || !semanticObject.Selectable)
            {
                throw new InvalidOperationException(
                    $"Semantic metadata validation failed for '{id}'.");
            }
        }
        if (!EditorBuildSettings.scenes.Any(scene =>
                scene.enabled &&
                string.Equals(
                    scene.path, DevelopmentScene,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "A-stage development scene is not active in Build Settings.");
        }
    }
}
