using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.RenderStreaming;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;


[InitializeOnLoad]
public static class PreparationExperimentSceneBuilder
{
    public const string SourceScenePath =
        "Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity";
    public const string ExperimentScenePath =
        "Assets/Scenes/Preparation Experiment Scene.unity";

    private const string AssetFolder = "Assets/PreparationExperiment";
    private const string MaterialFolder = AssetFolder + "/Materials";

    static PreparationExperimentSceneBuilder()
    {
        if (!Application.isBatchMode &&
            AssetDatabase.LoadAssetAtPath<SceneAsset>(ExperimentScenePath) == null)
        {
            EditorApplication.delayCall += CreateExperimentSceneIfPossible;
        }
    }

    [MenuItem("PICO/Create Preparation Experiment Scene", priority = 20)]
    public static void CreateExperimentScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning(
                "[Preparation Builder] Exit Play Mode before creating the scene.");
            return;
        }

        EditorSceneManager.SaveOpenScenes();
        EnsureFolder("Assets/Scenes");
        EnsureFolder(AssetFolder);
        EnsureFolder(MaterialFolder);

        Scene sourceScene =
            EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
        GameObject sourceRig = GameObject.Find("PICO XR Origin");
        GameObject sourceStreamCamera = GameObject.Find("streamcamera");
        if (sourceRig == null || sourceStreamCamera == null)
        {
            throw new InvalidOperationException(
                "The migrated source scene must contain PICO XR Origin and streamcamera.");
        }

        Scene experimentScene =
            EditorSceneManager.NewScene(
                NewSceneSetup.EmptyScene,
                NewSceneMode.Additive);
        SceneManager.SetActiveScene(experimentScene);

        GameObject rig = UnityEngine.Object.Instantiate(sourceRig);
        rig.name = "PICO XR Origin";
        SceneManager.MoveGameObjectToScene(rig, experimentScene);

        GameObject streamCameraObject =
            UnityEngine.Object.Instantiate(sourceStreamCamera);
        streamCameraObject.name = "streamcamera";
        SceneManager.MoveGameObjectToScene(streamCameraObject, experimentScene);

        ConfigureRigAndStreaming(rig, streamCameraObject);
        List<PreparationSequenceController.Step> steps =
            BuildLightweightWorkstation();
        ConfigureSequence(rig, steps);
        CreateLighting();
        ConfigureRenderSettings();

        EditorSceneManager.MarkSceneDirty(experimentScene);
        EditorSceneManager.SaveScene(experimentScene, ExperimentScenePath);
        EditorSceneManager.CloseScene(sourceScene, true);
        SceneManager.SetActiveScene(experimentScene);
        ConfigureBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        VerifyExperimentScene();
        Debug.Log(
            "[Preparation Builder] Created lightweight preparation scene with " +
            $"{steps.Count} guidance steps: {ExperimentScenePath}");
    }

    public static void BuildAndroid()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ExperimentScenePath) == null)
        {
            CreateExperimentScene();
        }

        VerifyExperimentScene();
        ConfigureBuildSettings();

        string outputDirectory = Path.GetFullPath("Builds");
        Directory.CreateDirectory(outputDirectory);
        string outputPath =
            Path.Combine(
                outputDirectory,
                "stream-manager-pico-preparation.apk");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ExperimentScenePath },
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"Preparation APK build failed: {report.summary.result}, " +
                $"errors={report.summary.totalErrors}, " +
                $"warnings={report.summary.totalWarnings}");
        }

        Debug.Log(
            $"[Preparation Builder] Android build succeeded: {outputPath}, " +
            $"size={report.summary.totalSize} bytes.");
    }

    private static void CreateExperimentSceneIfPossible()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ExperimentScenePath) == null)
        {
            CreateExperimentScene();
        }
    }

    private static void ConfigureRigAndStreaming(
        GameObject rig,
        GameObject streamCameraObject)
    {
        rig.transform.SetPositionAndRotation(
            new Vector3(0f, 0f, -0.9f),
            Quaternion.identity);

        Camera headsetCamera =
            rig.GetComponentsInChildren<Camera>(true)
                .FirstOrDefault(camera => camera.name == "Main Camera") ??
            rig.GetComponentInChildren<Camera>(true);
        Camera streamCamera = streamCameraObject.GetComponent<Camera>();
        if (headsetCamera == null || streamCamera == null)
        {
            throw new InvalidOperationException(
                "The cloned PICO rig is missing Main Camera or streamcamera.");
        }

        StreamCameraFollow follow =
            streamCameraObject.GetComponent<StreamCameraFollow>();
        if (follow == null)
        {
            follow = streamCameraObject.AddComponent<StreamCameraFollow>();
        }
        follow.vrCamera = headsetCamera.transform;
        streamCameraObject.transform.SetPositionAndRotation(
            headsetCamera.transform.position,
            headsetCamera.transform.rotation);

        VideoStreamSender sender = rig.GetComponent<VideoStreamSender>();
        if (sender == null)
        {
            throw new InvalidOperationException(
                "PICO XR Origin is missing VideoStreamSender.");
        }
        sender.sourceCamera = streamCamera;

        GuidanceManager manager = rig.GetComponent<GuidanceManager>();
        GuidanceDataChannelReceiver guidanceReceiver =
            rig.GetComponent<GuidanceDataChannelReceiver>();
        PlayerController player = rig.GetComponent<PlayerController>();
        if (manager == null || guidanceReceiver == null || player == null)
        {
            throw new InvalidOperationException(
                "The cloned rig is missing guidance or selection components.");
        }

        manager.GuidanceCamera = streamCamera;
        player.renderStreamingCamera = streamCamera;
        player.videoStreamSender = sender;

        GazeDataChannelReceiver gaze =
            rig.GetComponent<GazeDataChannelReceiver>();
        if (gaze != null)
        {
            SerializedObject serializedGaze = new SerializedObject(gaze);
            SetObjectReference(
                serializedGaze, "renderStreamingCamera", streamCamera);
            SetBoolean(serializedGaze, "showDebugRay", false);
            SetBoolean(serializedGaze, "showHitMarker", false);
            SetBoolean(serializedGaze, "showRenderedRay", false);
            serializedGaze.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    private static void ConfigureSequence(
        GameObject rig,
        List<PreparationSequenceController.Step> steps)
    {
        GuidanceManager manager = rig.GetComponent<GuidanceManager>();
        PreparationSequenceController sequence =
            rig.GetComponent<PreparationSequenceController>();
        if (sequence == null)
        {
            sequence = rig.AddComponent<PreparationSequenceController>();
        }
        sequence.Configure(manager, steps);

        GuidanceDataChannelReceiver receiver =
            rig.GetComponent<GuidanceDataChannelReceiver>();
        receiver.GuidanceManager = manager;
        receiver.PreparationSequence = sequence;

        EditorUtility.SetDirty(sequence);
        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(manager);
    }

    private static List<PreparationSequenceController.Step>
        BuildLightweightWorkstation()
    {
        Material floorMaterial =
            GetOrCreateMaterial("Floor", new Color(0.48f, 0.53f, 0.56f));
        Material wallMaterial =
            GetOrCreateMaterial("Wall", new Color(0.86f, 0.89f, 0.9f));
        Material benchMaterial =
            GetOrCreateMaterial("Bench", new Color(0.16f, 0.28f, 0.32f));
        Material metalMaterial =
            GetOrCreateMaterial("Metal", new Color(0.55f, 0.59f, 0.62f));
        Material glassMaterial =
            GetOrCreateMaterial("GlassBlue", new Color(0.2f, 0.65f, 0.82f));
        Material reagentAMaterial =
            GetOrCreateMaterial("ReagentA", new Color(0.92f, 0.32f, 0.22f));
        Material reagentBMaterial =
            GetOrCreateMaterial("ReagentB", new Color(0.45f, 0.25f, 0.78f));
        Material toolMaterial =
            GetOrCreateMaterial("Tool", new Color(0.95f, 0.72f, 0.12f));
        Material resultMaterial =
            GetOrCreateMaterial("Result", new Color(0.25f, 0.75f, 0.38f));

        GameObject environment = new GameObject("Preparation Workstation");
        CreatePart(
            "Floor", PrimitiveType.Cube, environment.transform,
            new Vector3(0f, -0.05f, 0.45f),
            new Vector3(5f, 0.1f, 4f), floorMaterial);
        CreatePart(
            "Back Wall", PrimitiveType.Cube, environment.transform,
            new Vector3(0f, 1.4f, 1.65f),
            new Vector3(5f, 2.8f, 0.1f), wallMaterial);
        CreatePart(
            "Left Boundary", PrimitiveType.Cube, environment.transform,
            new Vector3(-2.45f, 1.1f, 0.2f),
            new Vector3(0.1f, 2.2f, 3f), wallMaterial);
        CreatePart(
            "Right Boundary", PrimitiveType.Cube, environment.transform,
            new Vector3(2.45f, 1.1f, 0.2f),
            new Vector3(0.1f, 2.2f, 3f), wallMaterial);

        GameObject bench = new GameObject("Experiment Bench");
        bench.transform.SetParent(environment.transform, false);
        CreatePart(
            "Bench Top", PrimitiveType.Cube, bench.transform,
            new Vector3(0f, 0.82f, 0.35f),
            new Vector3(2.7f, 0.1f, 1.1f), benchMaterial);
        CreatePart(
            "Left Leg", PrimitiveType.Cube, bench.transform,
            new Vector3(-1.15f, 0.4f, 0.35f),
            new Vector3(0.12f, 0.8f, 0.8f), metalMaterial);
        CreatePart(
            "Right Leg", PrimitiveType.Cube, bench.transform,
            new Vector3(1.15f, 0.4f, 0.35f),
            new Vector3(0.12f, 0.8f, 0.8f), metalMaterial);
        CreatePart(
            "Shelf", PrimitiveType.Cube, bench.transform,
            new Vector3(0f, 1.42f, 1.05f),
            new Vector3(2.5f, 0.08f, 0.42f), benchMaterial);
        CreatePart(
            "Shelf Left Support", PrimitiveType.Cube, bench.transform,
            new Vector3(-1.15f, 1.12f, 1.05f),
            new Vector3(0.08f, 0.6f, 0.08f), metalMaterial);
        CreatePart(
            "Shelf Right Support", PrimitiveType.Cube, bench.transform,
            new Vector3(1.15f, 1.12f, 1.05f),
            new Vector3(0.08f, 0.6f, 0.08f), metalMaterial);

        const float surfaceY = 0.87f;
        GameObject beaker =
            CreateBeaker(
                "PE_Target_EmptyBeaker",
                new Vector3(-0.85f, surfaceY, 0.18f),
                glassMaterial);
        GameObject reagentA =
            CreateBottle(
                "PE_Target_ReagentA",
                "A",
                new Vector3(-0.35f, surfaceY, 0.52f),
                reagentAMaterial,
                toolMaterial);
        GameObject cylinder =
            CreateGraduatedCylinder(
                "PE_Target_GraduatedCylinder",
                new Vector3(0.2f, surfaceY, 0.15f),
                glassMaterial,
                metalMaterial);
        GameObject scale =
            CreateScale(
                "PE_Target_DigitalScale",
                new Vector3(0.75f, surfaceY, 0.48f),
                metalMaterial,
                benchMaterial);
        GameObject reagentB =
            CreateBottle(
                "PE_Target_ReagentB",
                "B",
                new Vector3(0.85f, 1.47f, 1.05f),
                reagentBMaterial,
                toolMaterial);
        GameObject pipette =
            CreateLongTool(
                "PE_Target_Pipette",
                "PIPETTE",
                new Vector3(-0.52f, surfaceY + 0.035f, 0.72f),
                toolMaterial,
                0.48f,
                0.035f);
        GameObject stirrer =
            CreateLongTool(
                "PE_Target_StirringRod",
                "STIR",
                new Vector3(0.18f, surfaceY + 0.025f, 0.76f),
                metalMaterial,
                0.52f,
                0.018f);
        GameObject tray =
            CreateTray(
                "PE_Target_ResultTray",
                new Vector3(1.05f, surfaceY, 0.03f),
                resultMaterial);

        return new List<PreparationSequenceController.Step>
        {
            Step("empty_beaker", "找到空烧杯并将其放到操作区。", beaker),
            Step("reagent_a", "找到试剂 A。", reagentA),
            Step("graduated_cylinder", "选择量筒以测量液体。", cylinder),
            Step("digital_scale", "将容器移动到电子秤位置。", scale),
            Step("reagent_b", "从上方置物架找到试剂 B。", reagentB),
            Step("pipette", "选择移液器加入少量液体。", pipette),
            Step("stirring_rod", "找到搅拌棒并完成混合。", stirrer),
            Step("result_tray", "将制备完成的容器放到绿色成品托盘。", tray)
        };
    }

    private static GameObject CreateBeaker(
        string name, Vector3 position, Material material)
    {
        GameObject root = CreateTargetRoot(name, position, true);
        CreatePart(
            "Beaker Body", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.13f, 0f),
            new Vector3(0.14f, 0.13f, 0.14f), material);
        CreatePart(
            "Beaker Rim", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.27f, 0f),
            new Vector3(0.155f, 0.012f, 0.155f), material);
        AddLabel(root.transform, "BEAKER", new Vector3(0f, 0.36f, 0f));
        return root;
    }

    private static GameObject CreateBottle(
        string name,
        string label,
        Vector3 position,
        Material bodyMaterial,
        Material capMaterial)
    {
        GameObject root = CreateTargetRoot(name, position, true);
        CreatePart(
            "Bottle Body", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.16f, 0f),
            new Vector3(0.13f, 0.16f, 0.13f), bodyMaterial);
        CreatePart(
            "Bottle Shoulder", PrimitiveType.Sphere, root.transform,
            new Vector3(0f, 0.31f, 0f),
            new Vector3(0.12f, 0.08f, 0.12f), bodyMaterial);
        CreatePart(
            "Bottle Cap", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.39f, 0f),
            new Vector3(0.07f, 0.045f, 0.07f), capMaterial);
        AddLabel(root.transform, $"REAGENT {label}", new Vector3(0f, 0.5f, 0f));
        return root;
    }

    private static GameObject CreateGraduatedCylinder(
        string name,
        Vector3 position,
        Material bodyMaterial,
        Material baseMaterial)
    {
        GameObject root = CreateTargetRoot(name, position, true);
        CreatePart(
            "Cylinder Base", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.025f, 0f),
            new Vector3(0.13f, 0.025f, 0.13f), baseMaterial);
        CreatePart(
            "Cylinder Body", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.25f, 0f),
            new Vector3(0.065f, 0.23f, 0.065f), bodyMaterial);
        AddLabel(root.transform, "100 ml", new Vector3(0f, 0.56f, 0f));
        return root;
    }

    private static GameObject CreateScale(
        string name,
        Vector3 position,
        Material metalMaterial,
        Material baseMaterial)
    {
        GameObject root = CreateTargetRoot(name, position, false);
        CreatePart(
            "Scale Base", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 0.06f, 0f),
            new Vector3(0.42f, 0.12f, 0.32f), baseMaterial);
        CreatePart(
            "Scale Plate", PrimitiveType.Cylinder, root.transform,
            new Vector3(0f, 0.14f, 0f),
            new Vector3(0.18f, 0.025f, 0.18f), metalMaterial);
        AddLabel(root.transform, "SCALE", new Vector3(0f, 0.25f, 0f));
        return root;
    }

    private static GameObject CreateLongTool(
        string name,
        string label,
        Vector3 position,
        Material material,
        float length,
        float radius)
    {
        GameObject root = CreateTargetRoot(name, position, true);
        GameObject body = CreatePart(
            "Tool Body", PrimitiveType.Capsule, root.transform,
            Vector3.zero,
            new Vector3(radius, length * 0.5f, radius), material);
        body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
        AddLabel(root.transform, label, new Vector3(0f, 0.12f, 0f));
        return root;
    }

    private static GameObject CreateTray(
        string name,
        Vector3 position,
        Material material)
    {
        GameObject root = CreateTargetRoot(name, position, false);
        CreatePart(
            "Tray Base", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 0.035f, 0f),
            new Vector3(0.48f, 0.07f, 0.38f), material);
        AddLabel(root.transform, "RESULT", new Vector3(0f, 0.16f, 0f));
        return root;
    }

    private static GameObject CreateTargetRoot(
        string name, Vector3 position, bool grabbable)
    {
        GameObject root = new GameObject(name);
        root.transform.position = position;

        if (grabbable)
        {
            Rigidbody rigidbody = root.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;

            UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab =
                root.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Kinematic;
            grab.throwOnDetach = false;
        }

        return root;
    }

    private static GameObject CreatePart(
        string name,
        PrimitiveType primitiveType,
        Transform parent,
        Vector3 localPosition,
        Vector3 localScale,
        Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitiveType);
        part.name = name;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
        }
        return part;
    }

    private static void AddLabel(
        Transform parent, string text, Vector3 localPosition)
    {
        GameObject labelObject = new GameObject("Label");
        labelObject.transform.SetParent(parent, false);
        labelObject.transform.localPosition = localPosition;
        labelObject.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        labelObject.transform.localScale = Vector3.one * 0.035f;
        TextMesh textMesh = labelObject.AddComponent<TextMesh>();
        textMesh.text = text;
        textMesh.anchor = TextAnchor.MiddleCenter;
        textMesh.alignment = TextAlignment.Center;
        textMesh.fontSize = 48;
        textMesh.color = Color.black;
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

    private static void CreateLighting()
    {
        GameObject lightObject = new GameObject("Preparation Key Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.96f, 0.9f);
        light.intensity = 1.15f;
        light.shadows = LightShadows.None;
        lightObject.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
    }

    private static void ConfigureRenderSettings()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.62f, 0.65f, 0.68f);
        RenderSettings.fog = false;
    }

    private static Material GetOrCreateMaterial(
        string name, Color color)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Standard");
            material = new Material(shader)
            {
                name = name,
                color = color
            };
            material.SetFloat("_Glossiness", 0.2f);
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.color = color;
            EditorUtility.SetDirty(material);
        }

        return material;
    }

    private static void ConfigureBuildSettings()
    {
        List<EditorBuildSettingsScene> scenes =
            EditorBuildSettings.scenes.ToList();
        bool found = false;
        for (int index = 0; index < scenes.Count; index++)
        {
            EditorBuildSettingsScene scene = scenes[index];
            bool enabled = scene.path == ExperimentScenePath;
            if (enabled)
            {
                found = true;
            }
            scenes[index] =
                new EditorBuildSettingsScene(scene.path, enabled);
        }

        if (!found)
        {
            scenes.Add(
                new EditorBuildSettingsScene(ExperimentScenePath, true));
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void VerifyExperimentScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ExperimentScenePath)
        {
            scene =
                EditorSceneManager.OpenScene(
                    ExperimentScenePath,
                    OpenSceneMode.Single);
        }

        GameObject rig = GameObject.Find("PICO XR Origin");
        GameObject streamCamera = GameObject.Find("streamcamera");
        PreparationSequenceController sequence =
            rig != null
                ? rig.GetComponent<PreparationSequenceController>()
                : null;
        if (rig == null ||
            streamCamera == null ||
            sequence == null ||
            sequence.StepCount != 8 ||
            rig.GetComponent<GuidanceManager>() == null ||
            rig.GetComponent<GuidanceDataChannelReceiver>() == null)
        {
            throw new InvalidOperationException(
                "Preparation scene verification failed.");
        }
    }

    private static void SetObjectReference(
        SerializedObject serialized,
        string propertyName,
        UnityEngine.Object value)
    {
        SerializedProperty property =
            serialized.FindProperty(propertyName);
        if (property != null)
        {
            property.objectReferenceValue = value;
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

    private static void EnsureFolder(string path)
    {
        string[] segments = path.Split('/');
        string current = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }
            current = next;
        }
    }
}
