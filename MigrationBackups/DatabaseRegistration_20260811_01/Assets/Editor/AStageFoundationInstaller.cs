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
    // Revision 2: installation is now requested after leaving Play Mode.
    private const string SourceScene =
        "Assets/Scenes/Preparation Experiment Scene.unity";
    private const string DevelopmentScene =
        "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string InstallRequestFile =
        "Temp/AStageFoundation.install-request";

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
            case "empty_beaker":
                return new Metadata(
                    "空烧杯", "容器", "透明", "圆柱形",
                    "烧杯", "空杯", "beaker", "empty beaker");
            case "reagent_a":
                return new Metadata(
                    "试剂 A", "试剂瓶", "蓝色", "瓶形",
                    "A试剂", "蓝色试剂", "reagent A", "blue bottle");
            case "graduated_cylinder":
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
