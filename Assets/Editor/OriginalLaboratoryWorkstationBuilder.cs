using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OriginalLaboratoryWorkstationBuilder
{
    private const string SourceScenePath =
        "Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity";
    private const string ExperimentScenePath =
        "Assets/Scenes/Preparation Experiment Scene.unity";
    private const string SessionKey =
        "PreparationExperiment.OriginalWorkstationCropV1";

    private static readonly Bounds CropBounds =
        new Bounds(
            new Vector3(5.35f, 2f, 5.45f),
            new Vector3(3.6f, 5f, 3.2f));

    private static double _buildAt;

    static OriginalLaboratoryWorkstationBuilder()
    {
        if (!Application.isBatchMode &&
            !SessionState.GetBool(SessionKey, false))
        {
            SessionState.SetBool(SessionKey, true);
            _buildAt = EditorApplication.timeSinceStartup + 1d;
            EditorApplication.update += BuildWhenReady;
        }
    }

    [MenuItem("PICO/Rebuild Preparation Scene From Original Lab", priority = 21)]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "[Preparation Crop] Exit Play Mode before rebuilding the scene.");
            return;
        }

        Scene source =
            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        int sourceRendererCount =
            UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length;

        EditorSceneManager.SaveScene(
            source,
            ExperimentScenePath,
            true);
        Scene experiment =
            EditorSceneManager.OpenScene(
                ExperimentScenePath,
                OpenSceneMode.Single);

        PruneToSingleWorkstation(experiment);
        ConfigurePreparationSequence();
        PicoExperimentInteractionSetup.ApplyToOpenScene();
        DisableGazeDebugVisualization();
        ConfigureBuildSettings();

        EditorSceneManager.MarkSceneDirty(experiment);
        EditorSceneManager.SaveScene(experiment, ExperimentScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int finalRendererCount =
            UnityEngine.Object.FindObjectsOfType<Renderer>(true).Length;
        Verify();
        Debug.Log(
            $"[Preparation Crop] Rebuilt from original laboratory. " +
            $"renderers={sourceRendererCount}->{finalRendererCount}, " +
            $"cropCenter={CropBounds.center}, cropSize={CropBounds.size}, " +
            $"scene={ExperimentScenePath}");
    }

    private static void BuildWhenReady()
    {
        if (EditorApplication.timeSinceStartup < _buildAt ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            return;
        }

        EditorApplication.update -= BuildWhenReady;
        Build();
    }

    private static void PruneToSingleWorkstation(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects().ToArray())
        {
            if (IsEssentialRoot(root))
            {
                continue;
            }

            if (!SubtreeIntersectsCrop(root.transform) &&
                !ShouldKeepLight(root))
            {
                UnityEngine.Object.DestroyImmediate(root);
                continue;
            }

            PruneChildren(root.transform);
        }
    }

    private static void PruneChildren(Transform parent)
    {
        for (int index = parent.childCount - 1; index >= 0; index--)
        {
            Transform child = parent.GetChild(index);
            if (!SubtreeIntersectsCrop(child) &&
                !ShouldKeepLight(child.gameObject))
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                continue;
            }

            PruneChildren(child);

            Renderer ownRenderer = child.GetComponent<Renderer>();
            if (ownRenderer != null &&
                !ownRenderer.bounds.Intersects(CropBounds))
            {
                ownRenderer.enabled = false;
                Collider ownCollider = child.GetComponent<Collider>();
                if (ownCollider != null)
                {
                    ownCollider.enabled = false;
                }
            }
        }
    }

    private static bool SubtreeIntersectsCrop(Transform transform)
    {
        Renderer[] renderers =
            transform.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer != null &&
                renderer.enabled &&
                renderer.bounds.Intersects(CropBounds))
            {
                return true;
            }
        }

        Light light = transform.GetComponentInChildren<Light>(true);
        return light != null && ShouldKeepLight(light.gameObject);
    }

    private static bool IsEssentialRoot(GameObject root)
    {
        return root.name == "PICO XR Origin" ||
               root.name == "streamcamera";
    }

    private static bool ShouldKeepLight(GameObject gameObject)
    {
        Light light = gameObject.GetComponentInChildren<Light>(true);
        if (light == null)
        {
            return false;
        }

        return light.type == LightType.Directional ||
               CropBounds.SqrDistance(light.transform.position) < 4f;
    }

    private static void ConfigurePreparationSequence()
    {
        GameObject rig = GameObject.Find("PICO XR Origin");
        if (rig == null)
        {
            throw new InvalidOperationException(
                "Cropped scene is missing PICO XR Origin.");
        }

        GuidanceManager manager = rig.GetComponent<GuidanceManager>();
        GuidanceDataChannelReceiver receiver =
            rig.GetComponent<GuidanceDataChannelReceiver>();
        if (manager == null || receiver == null)
        {
            throw new InvalidOperationException(
                "Cropped scene is missing the guidance components.");
        }

        PreparationSequenceController sequence =
            rig.GetComponent<PreparationSequenceController>();
        if (sequence == null)
        {
            sequence = rig.AddComponent<PreparationSequenceController>();
        }

        List<PreparationSequenceController.Step> steps =
            new List<PreparationSequenceController.Step>
            {
                Step(
                    "china_dish",
                    "找到蒸发皿并放到实验台中央。",
                    FindRequiredTarget("china dish (1)")),
                Step(
                    "graduated_cylinder",
                    "找到量筒，准备量取液体。",
                    FindRequiredTarget("Graduated_Cylinder (1)")),
                Step(
                    "beaker",
                    "找到装有液体的烧杯。",
                    FindRequiredTarget("Beaker water")),
                Step(
                    "dropper",
                    "选择玻璃滴管，准备转移少量液体。",
                    FindRequiredTarget("Glass_medical_dropper")),
                Step(
                    "florence_flask",
                    "找到蒸馏烧瓶并作为混合容器。",
                    FindRequiredTarget("florence_flask with water")),
                Step(
                    "spirit_lamp",
                    "找到酒精灯，准备加热步骤。",
                    FindRequiredTarget("Spirit_Lamp with water")),
                Step(
                    "test_tube_rack",
                    "找到试管架并检查试管。",
                    FindRequiredTarget("Test_tube_rack")),
                Step(
                    "glass_funnel",
                    "最后找到玻璃漏斗，准备过滤。",
                    FindRequiredTarget("glass_funnel (1)"))
            };

        foreach (PreparationSequenceController.Step step in steps)
        {
            EnsureSelectable(step.target);
        }

        sequence.Configure(manager, steps);
        receiver.GuidanceManager = manager;
        receiver.PreparationSequence = sequence;
        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(receiver);
    }

    private static PreparationSequenceController.Step Step(
        string id, string instruction, GameObject target)
    {
        return new PreparationSequenceController.Step
        {
            id = id,
            instruction = instruction,
            target = target
        };
    }

    private static GameObject FindRequiredTarget(string name)
    {
        GameObject target =
            UnityEngine.Object.FindObjectsOfType<Transform>(true)
                .Where(transform => transform.name == name)
                .Select(transform => transform.gameObject)
                .OrderBy(gameObject =>
                    CropBounds.SqrDistance(
                        GetCombinedBounds(gameObject).center))
                .FirstOrDefault();
        if (target == null)
        {
            throw new InvalidOperationException(
                $"Required preparation target was not found: {name}");
        }

        return target;
    }

    private static void EnsureSelectable(GameObject target)
    {
        if (target.GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }

        Bounds bounds = GetCombinedBounds(target);
        BoxCollider collider = target.AddComponent<BoxCollider>();
        collider.center =
            target.transform.InverseTransformPoint(bounds.center);
        Vector3 scale = target.transform.lossyScale;
        collider.size = new Vector3(
            bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
    }

    private static Bounds GetCombinedBounds(GameObject target)
    {
        Renderer[] renderers =
            target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(target.transform.position, Vector3.one * 0.2f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }

    private static void DisableGazeDebugVisualization()
    {
        GameObject rig = GameObject.Find("PICO XR Origin");
        GazeDataChannelReceiver gaze =
            rig != null ? rig.GetComponent<GazeDataChannelReceiver>() : null;
        if (gaze == null)
        {
            return;
        }

        SerializedObject serialized = new SerializedObject(gaze);
        SetBoolean(serialized, "showDebugRay", false);
        SetBoolean(serialized, "showHitMarker", false);
        SetBoolean(serialized, "showRenderedRay", false);
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(gaze);
    }

    private static void ConfigureBuildSettings()
    {
        EditorBuildSettings.scenes =
            EditorBuildSettings.scenes
                .Select(scene =>
                    new EditorBuildSettingsScene(
                        scene.path,
                        scene.path == ExperimentScenePath))
                .Concat(
                    EditorBuildSettings.scenes.Any(
                        scene => scene.path == ExperimentScenePath)
                        ? Array.Empty<EditorBuildSettingsScene>()
                        : new[]
                        {
                            new EditorBuildSettingsScene(
                                ExperimentScenePath,
                                true)
                        })
                .ToArray();
    }

    private static void Verify()
    {
        if (SceneManager.GetActiveScene().path != ExperimentScenePath)
        {
            throw new InvalidOperationException(
                "The cropped preparation scene is not active.");
        }

        GameObject rig = GameObject.Find("PICO XR Origin");
        PreparationSequenceController sequence =
            rig != null
                ? rig.GetComponent<PreparationSequenceController>()
                : null;
        if (rig == null ||
            GameObject.Find("streamcamera") == null ||
            sequence == null ||
            sequence.StepCount != 8)
        {
            throw new InvalidOperationException(
                "Cropped preparation scene verification failed.");
        }
    }

    private static void SetBoolean(
        SerializedObject serialized,
        string propertyName,
        bool value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.boolValue = value;
        }
    }
}
