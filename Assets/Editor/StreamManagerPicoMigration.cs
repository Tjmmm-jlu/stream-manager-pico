using System;
using System.IO;
using System.Linq;
using Unity.RenderStreaming;
using Unity.XR.CoreUtils;
using Unity.XR.PXR;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Management;

public static class StreamManagerPicoMigration
{
    private const string ScenePath =
        "Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity";
    private const string QuestRigName = "[BuildingBlock] Camera Rig";
    private const string PicoRigName = "PICO XR Origin";
    private const string StreamCameraName = "streamcamera";
    private const string PicoLoaderType = "Unity.XR.PXR.PXR_Loader";
    private const string OpenXrLoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
    private const string PicoSettingsKey = "Unity.XR.PXR.Settings";
    private const string PicoSettingsPath = "Assets/XR/Settings/PXR_Settings.asset";
    private const string RenderStreamingSettingsPath = "Assets/stream manager.asset";
    private const string SignalingUrl = "ws://192.168.3.232";

    public static void Run()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        GameObject rig = GameObject.Find(PicoRigName) ?? GameObject.Find(QuestRigName);
        if (rig == null)
        {
            throw new InvalidOperationException(
                $"Could not find either '{QuestRigName}' or '{PicoRigName}' in {ScenePath}.");
        }

        bool firstMigration = rig.name != PicoRigName;
        if (firstMigration)
        {
            RemoveQuestInteractionPrefab(rig.transform);
            RemoveMetaComponents(rig);
            rig.name = PicoRigName;
            NormalizeRigRotation(rig.transform);
        }

        GameObject cameraOffset = PrepareCameraOffset(rig.transform, firstMigration);
        Camera picoCamera = PreparePicoCamera(cameraOffset.transform);
        ConfigureXrOrigin(rig, cameraOffset, picoCamera);
        ConfigurePicoManager(rig);
        ConfigurePicoControllers(rig, cameraOffset);
        ConvertQuestGrabObjects();
        RebindProjectComponents(picoCamera);
        ConfigureSignaling();
        ConfigurePicoProject();
        ConfigureAndroidLoader();
        RemoveQuestAssets();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        VerifySceneOrThrow(scene);
        Debug.Log("[PICO MIGRATION] Scene migration completed successfully.");
    }

    public static void Verify()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        VerifySceneOrThrow(scene);
        Debug.Log("[PICO MIGRATION] Verification completed successfully.");
    }

    public static void RepairKnownMissingScripts()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        string[] knownPaths = { "CM vcam1", "CM vcam1/cm", "DollyTrack1" };
        int removed = 0;

        foreach (string path in knownPaths)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null)
            {
                throw new InvalidOperationException(
                    $"Expected legacy scene object '{path}' was not found.");
            }

            int count =
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one missing script on '{path}', found {count}.");
            }

            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            removed += count;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        VerifySceneOrThrow(scene);
        Debug.Log(
            $"[PICO MIGRATION] Removed {removed} pre-existing legacy missing scripts " +
            "and verified the scene successfully.");
    }

    public static void BuildAndroid()
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        VerifySceneOrThrow(scene);

        string outputDirectory = Path.GetFullPath("Builds");
        Directory.CreateDirectory(outputDirectory);
        string outputPath = Path.Combine(outputDirectory, "stream-manager-pico.apk");

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes
                .Where(item => item.enabled)
                .Select(item => item.path)
                .ToArray(),
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
        {
            throw new InvalidOperationException(
                $"PICO Android build failed: {report.summary.result}, " +
                $"errors={report.summary.totalErrors}, warnings={report.summary.totalWarnings}");
        }

        Debug.Log(
            $"[PICO MIGRATION] Android build succeeded: {outputPath}, " +
            $"size={report.summary.totalSize} bytes.");
    }

    private static void RemoveQuestInteractionPrefab(Transform rig)
    {
        for (int index = rig.childCount - 1; index >= 0; index--)
        {
            Transform child = rig.GetChild(index);
            if (child.name.IndexOf("OVRInteraction", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }
    }

    private static void RemoveMetaComponents(GameObject rig)
    {
        Component[] components = rig.GetComponents<Component>();
        foreach (Component component in components)
        {
            if (component == null || component is Transform)
            {
                continue;
            }

            Type type = component.GetType();
            string assembly = type.Assembly.GetName().Name ?? string.Empty;
            string fullName = type.FullName ?? type.Name;
            bool isMeta =
                assembly.IndexOf("Oculus", StringComparison.OrdinalIgnoreCase) >= 0 ||
                assembly.IndexOf("Meta.XR", StringComparison.OrdinalIgnoreCase) >= 0 ||
                fullName.StartsWith("OVR", StringComparison.Ordinal) ||
                fullName.StartsWith("Meta.XR", StringComparison.Ordinal);

            if (isMeta)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }
    }

    private static void NormalizeRigRotation(Transform rig)
    {
        float yaw = rig.eulerAngles.y;
        rig.rotation = Quaternion.Euler(0f, yaw, 0f);
        rig.localScale = Vector3.one;
    }

    private static GameObject PrepareCameraOffset(Transform rig, bool firstMigration)
    {
        Transform cameraOffset = rig.Find("Camera Offset") ?? rig.Find("TrackingSpace");
        if (cameraOffset == null)
        {
            GameObject offsetObject = new GameObject("Camera Offset");
            cameraOffset = offsetObject.transform;
            cameraOffset.SetParent(rig, false);
        }

        cameraOffset.name = "Camera Offset";
        cameraOffset.localPosition = Vector3.zero;
        cameraOffset.localRotation = Quaternion.identity;
        cameraOffset.localScale = Vector3.one;

        if (firstMigration)
        {
            for (int index = cameraOffset.childCount - 1; index >= 0; index--)
            {
                UnityEngine.Object.DestroyImmediate(cameraOffset.GetChild(index).gameObject);
            }
        }

        return cameraOffset.gameObject;
    }

    private static Camera PreparePicoCamera(Transform cameraOffset)
    {
        Transform cameraTransform = cameraOffset.Find("Main Camera");
        if (cameraTransform == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            cameraTransform = cameraObject.transform;
            cameraTransform.SetParent(cameraOffset, false);
        }

        cameraTransform.localPosition = Vector3.zero;
        cameraTransform.localRotation = Quaternion.identity;
        cameraTransform.localScale = Vector3.one;
        cameraTransform.gameObject.tag = "MainCamera";

        Camera camera = cameraTransform.GetComponent<Camera>();
        if (camera == null)
        {
            camera = cameraTransform.gameObject.AddComponent<Camera>();
        }

        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 1000f;
        camera.fieldOfView = 90f;
        camera.stereoTargetEye = StereoTargetEyeMask.Both;

        if (cameraTransform.GetComponent<AudioListener>() == null)
        {
            cameraTransform.gameObject.AddComponent<AudioListener>();
        }

        TrackedPoseDriver driver = cameraTransform.GetComponent<TrackedPoseDriver>();
        if (driver == null)
        {
            driver = cameraTransform.gameObject.AddComponent<TrackedPoseDriver>();
        }

        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.ignoreTrackingState = false;
        driver.positionInput = new InputActionProperty(
            new InputAction(
                "PICO HMD Position",
                InputActionType.Value,
                "<XRHMD>/centerEyePosition",
                expectedControlType: "Vector3"));
        driver.rotationInput = new InputActionProperty(
            new InputAction(
                "PICO HMD Rotation",
                InputActionType.Value,
                "<XRHMD>/centerEyeRotation",
                expectedControlType: "Quaternion"));
        driver.trackingStateInput = new InputActionProperty(
            new InputAction(
                "PICO HMD Tracking State",
                InputActionType.Value,
                "<XRHMD>/trackingState",
                expectedControlType: "Integer"));

        return camera;
    }

    private static void ConfigureXrOrigin(GameObject rig, GameObject cameraOffset, Camera camera)
    {
        XROrigin origin = rig.GetComponent<XROrigin>();
        if (origin == null)
        {
            origin = rig.AddComponent<XROrigin>();
        }

        origin.Origin = rig;
        origin.CameraFloorOffsetObject = cameraOffset;
        origin.Camera = camera;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
        origin.CameraYOffset = 0f;
    }

    private static void ConfigurePicoManager(GameObject rig)
    {
        PXR_Manager manager = rig.GetComponent<PXR_Manager>();
        if (manager == null)
        {
            manager = rig.AddComponent<PXR_Manager>();
        }

        // Existing shared gaze comes from the PC/Tobii DataChannel, not from
        // the headset eye tracker. Leave PICO eye tracking disabled here.
        manager.eyeTracking = false;
        manager.openMRC = false;
        manager.faceTracking = false;
        manager.bodyTracking = false;
        EditorUtility.SetDirty(manager);
    }

    private static void ConfigurePicoControllers(GameObject rig, GameObject cameraOffset)
    {
        Transform managerTransform = rig.transform.Find("XR Interaction Manager");
        GameObject managerObject;
        if (managerTransform == null)
        {
            managerObject = new GameObject("XR Interaction Manager");
            managerObject.transform.SetParent(rig.transform, false);
        }
        else
        {
            managerObject = managerTransform.gameObject;
        }

        XRInteractionManager interactionManager =
            managerObject.GetComponent<XRInteractionManager>();
        if (interactionManager == null)
        {
            interactionManager = managerObject.AddComponent<XRInteractionManager>();
        }

        ConfigureController(
            cameraOffset.transform, interactionManager, "Left Controller", "LeftHand");
        ConfigureController(
            cameraOffset.transform, interactionManager, "Right Controller", "RightHand");
    }

    private static void ConfigureController(
        Transform parent,
        XRInteractionManager interactionManager,
        string objectName,
        string usage)
    {
        Transform controllerTransform = parent.Find(objectName);
        if (controllerTransform == null)
        {
            GameObject controllerObject = new GameObject(objectName);
            controllerTransform = controllerObject.transform;
            controllerTransform.SetParent(parent, false);
        }

        controllerTransform.localPosition = Vector3.zero;
        controllerTransform.localRotation = Quaternion.identity;

        ActionBasedController controller =
            controllerTransform.GetComponent<ActionBasedController>();
        if (controller == null)
        {
            controller = controllerTransform.gameObject.AddComponent<ActionBasedController>();
        }

        string device = $"<XRController>{{{usage}}}";
        controller.positionAction = CreateAction(
            $"{objectName} Position", InputActionType.Value,
            $"{device}/devicePosition", "Vector3");
        controller.rotationAction = CreateAction(
            $"{objectName} Rotation", InputActionType.Value,
            $"{device}/deviceRotation", "Quaternion");
        controller.isTrackedAction = CreateAction(
            $"{objectName} Is Tracked", InputActionType.Button,
            $"{device}/isTracked", "Button");
        controller.trackingStateAction = CreateAction(
            $"{objectName} Tracking State", InputActionType.Value,
            $"{device}/trackingState", "Integer");
        controller.selectAction = CreateAction(
            $"{objectName} Select", InputActionType.Button,
            $"{device}/gripPressed", "Button");
        controller.selectActionValue = CreateAction(
            $"{objectName} Select Value", InputActionType.Value,
            $"{device}/grip", "Axis");
        controller.activateAction = CreateAction(
            $"{objectName} Activate", InputActionType.Button,
            $"{device}/triggerPressed", "Button");
        controller.activateActionValue = CreateAction(
            $"{objectName} Activate Value", InputActionType.Value,
            $"{device}/trigger", "Axis");

        SphereCollider collider =
            controllerTransform.GetComponent<SphereCollider>();
        if (collider == null)
        {
            collider = controllerTransform.gameObject.AddComponent<SphereCollider>();
        }
        collider.isTrigger = true;
        collider.radius = 0.08f;

        UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor directInteractor =
            controllerTransform.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
        if (directInteractor == null)
        {
            directInteractor =
                controllerTransform.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>();
        }
        directInteractor.interactionManager = interactionManager;
        directInteractor.improveAccuracyWithSphereCollider = true;

        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(directInteractor);
    }

    private static InputActionProperty CreateAction(
        string name,
        InputActionType type,
        string binding,
        string expectedControlType)
    {
        return new InputActionProperty(
            new InputAction(
                name,
                type,
                binding,
                expectedControlType: expectedControlType));
    }

    private static void ConvertQuestGrabObjects()
    {
        GameObject lamp = GameObject.Find("Spirit_Lamp with water");
        if (lamp == null)
        {
            throw new InvalidOperationException(
                "Could not find the Quest-grabbable Spirit_Lamp with water.");
        }
        RemoveMissingScripts(lamp);
        EnsureXrGrabInteractable(lamp, false);

        GameObject cube = GameObject.Find("[BuildingBlock] Cube");
        if (cube == null)
        {
            throw new InvalidOperationException(
                "Could not find the Quest hand-grab sample cube.");
        }

        Transform installationRoutine =
            cube.transform.Find("[BuildingBlock] HandGrabInstallationRoutine");
        if (installationRoutine != null)
        {
            UnityEngine.Object.DestroyImmediate(installationRoutine.gameObject);
        }

        RemoveMissingScripts(cube);
        EnsureXrGrabInteractable(cube, true);
    }

    private static void RemoveMissingScripts(GameObject gameObject)
    {
        int count =
            GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
        if (count > 0)
        {
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
            Debug.Log(
                $"[PICO MIGRATION] Removed {count} Quest-only components from " +
                GetHierarchyPath(gameObject.transform));
        }
    }

    private static void EnsureXrGrabInteractable(
        GameObject gameObject, bool forceNonTriggerCollider)
    {
        Rigidbody rigidbody = gameObject.GetComponent<Rigidbody>();
        if (rigidbody == null)
        {
            rigidbody = gameObject.AddComponent<Rigidbody>();
        }

        Collider collider = gameObject.GetComponent<Collider>();
        if (collider == null)
        {
            collider = gameObject.AddComponent<BoxCollider>();
        }
        if (forceNonTriggerCollider)
        {
            collider.isTrigger = false;
        }

        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab =
            gameObject.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab == null)
        {
            grab = gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        }
        grab.throwOnDetach = true;

        EditorUtility.SetDirty(rigidbody);
        EditorUtility.SetDirty(collider);
        EditorUtility.SetDirty(grab);
    }

    private static void RebindProjectComponents(Camera picoCamera)
    {
        GameObject streamCameraObject = GameObject.Find(StreamCameraName);
        if (streamCameraObject == null)
        {
            throw new InvalidOperationException($"Could not find '{StreamCameraName}'.");
        }

        Camera streamCamera = streamCameraObject.GetComponent<Camera>();
        if (streamCamera == null)
        {
            throw new InvalidOperationException($"'{StreamCameraName}' has no Camera component.");
        }

        StreamCameraFollow follow = streamCameraObject.GetComponent<StreamCameraFollow>();
        if (follow == null)
        {
            follow = streamCameraObject.AddComponent<StreamCameraFollow>();
        }
        follow.vrCamera = picoCamera.transform;

        VideoStreamSender sender = UnityEngine.Object.FindObjectOfType<VideoStreamSender>(true);
        if (sender == null)
        {
            throw new InvalidOperationException("No VideoStreamSender exists in the main scene.");
        }
        sender.source = VideoStreamSource.Camera;
        sender.sourceCamera = streamCamera;

        foreach (PlayerController controller in
                 UnityEngine.Object.FindObjectsOfType<PlayerController>(true))
        {
            controller.renderStreamingCamera = streamCamera;
            controller.videoStreamSender = sender;
            EditorUtility.SetDirty(controller);
        }

        foreach (WebObjectSelector selector in
                 UnityEngine.Object.FindObjectsOfType<WebObjectSelector>(true))
        {
            selector.renderStreamingCamera = streamCamera;
            selector.videoStreamSender = sender;
            EditorUtility.SetDirty(selector);
        }

        foreach (GazeDataChannelReceiver receiver in
                 UnityEngine.Object.FindObjectsOfType<GazeDataChannelReceiver>(true))
        {
            SerializedObject serialized = new SerializedObject(receiver);
            serialized.FindProperty("renderStreamingCamera").objectReferenceValue = streamCamera;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(receiver);
        }

        StreamManager streamManager = UnityEngine.Object.FindObjectOfType<StreamManager>(true);
        if (streamManager != null)
        {
            SerializedObject serialized = new SerializedObject(streamManager);
            SerializedProperty videoProperty = serialized.FindProperty("videoStreamSender");
            if (videoProperty != null)
            {
                videoProperty.objectReferenceValue = sender;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(streamManager);
        }

        EditorUtility.SetDirty(follow);
        EditorUtility.SetDirty(sender);
    }

    private static void ConfigureSignaling()
    {
        RenderStreamingSettings settings =
            AssetDatabase.LoadAssetAtPath<RenderStreamingSettings>(
                RenderStreamingSettingsPath);
        if (settings == null)
        {
            throw new InvalidOperationException(
                $"Could not load {RenderStreamingSettingsPath}.");
        }

        SerializedObject serialized = new SerializedObject(settings);
        SerializedProperty signaling = serialized.FindProperty("signalingSettings");
        SerializedProperty url =
            signaling != null ? signaling.FindPropertyRelative("m_url") : null;
        if (url == null)
        {
            throw new InvalidOperationException(
                "Could not find the Render Streaming signaling URL.");
        }

        url.stringValue = SignalingUrl;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(settings);
        Debug.Log($"[PICO MIGRATION] Signaling URL set to {SignalingUrl}.");
    }

    private static void ConfigurePicoProject()
    {
        PlayerSettings.productName = "stream manager PICO";
        PlayerSettings.SetApplicationIdentifier(
            BuildTargetGroup.Android, "com.defaultcompany.streammanagerpico");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(
            BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        ConfigureInputSystemOnly();
        string defines =
            PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
        string filteredDefines = string.Join(
            ";",
            defines.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(symbol =>
                    !symbol.StartsWith("OVR_", StringComparison.Ordinal) &&
                    !symbol.StartsWith("META_", StringComparison.Ordinal)));
        PlayerSettings.SetScriptingDefineSymbolsForGroup(
            BuildTargetGroup.Android, filteredDefines);

        PXR_ProjectSetting projectSetting = PXR_ProjectSetting.GetProjectConfig();
        if (projectSetting != null)
        {
            projectSetting.eyeTracking = false;
            projectSetting.eyetrackingCalibration = false;
            projectSetting.openMRC = false;
            PXR_ProjectSetting.SaveAssets();
        }

        PXR_Settings settings = PXR_Settings.GetSettings();
        if (settings == null)
        {
            settings = AssetDatabase.LoadAssetAtPath<PXR_Settings>(PicoSettingsPath);
        }
        if (settings == null)
        {
            EnsureAssetFolder("Assets/XR/Settings");
            settings = ScriptableObject.CreateInstance<PXR_Settings>();
            settings.name = "PXR_Settings";
            AssetDatabase.CreateAsset(settings, PicoSettingsPath);
        }
        settings.stereoRenderingModeAndroid =
            PXR_Settings.StereoRenderingModeAndroid.Multiview;
        settings.systemDisplayFrequency =
            PXR_Settings.SystemDisplayFrequency.Default;
        settings.optimizeBufferDiscards = true;
        settings.enableAppSpaceWarp = false;
        EditorBuildSettings.AddConfigObject(PicoSettingsKey, settings, true);
        EditorUtility.SetDirty(settings);
    }

    private static void ConfigureInputSystemOnly()
    {
        UnityEngine.Object[] projectSettings =
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset");
        if (projectSettings.Length == 0)
        {
            throw new InvalidOperationException(
                "Could not load ProjectSettings.asset.");
        }

        SerializedObject serialized = new SerializedObject(projectSettings[0]);
        SerializedProperty inputHandler =
            serialized.FindProperty("activeInputHandler");
        if (inputHandler == null)
        {
            throw new InvalidOperationException(
                "Could not find activeInputHandler in ProjectSettings.");
        }
        inputHandler.intValue = 1;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void EnsureAssetFolder(string path)
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

    private static void ConfigureAndroidLoader()
    {
        XRGeneralSettings general =
            XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
                BuildTargetGroup.Android);
        if (general == null || general.Manager == null)
        {
            throw new InvalidOperationException(
                "XR General Settings for Android were not created.");
        }

        XRPackageMetadataStore.RemoveLoader(
            general.Manager, OpenXrLoaderType, BuildTargetGroup.Android);
        if (!XRPackageMetadataStore.AssignLoader(
                general.Manager, PicoLoaderType, BuildTargetGroup.Android))
        {
            bool alreadyAssigned = general.Manager.activeLoaders.Any(
                loader => loader != null &&
                          loader.GetType().FullName == PicoLoaderType);
            if (!alreadyAssigned)
            {
                throw new InvalidOperationException("Could not assign PXR Loader to Android.");
            }
        }

        EditorUtility.SetDirty(general);
        EditorUtility.SetDirty(general.Manager);
    }

    private static void RemoveQuestAssets()
    {
        string[] questAssets =
        {
            "Assets/Oculus",
            "Assets/Plugins/Android/AndroidManifest.xml",
            "Assets/Resources/ImmersiveDebuggerSettings.asset",
            "Assets/Resources/MetaXRAcousticMaterialMapping.asset",
            "Assets/Resources/MetaXRAcousticSettings.asset",
            "Assets/Resources/MetaXRAudioSettings.asset",
            "Assets/Resources/OculusRuntimeSettings.asset",
            "Assets/Resources/OVRBuildConfig.asset",
            "Assets/Resources/OVRPlatformToolSettings.asset"
        };

        foreach (string asset in questAssets)
        {
            if (AssetDatabase.LoadMainAssetAtPath(asset) != null ||
                AssetDatabase.IsValidFolder(asset))
            {
                AssetDatabase.DeleteAsset(asset);
            }
        }
    }

    private static void VerifySceneOrThrow(Scene scene)
    {
        GameObject rig = GameObject.Find(PicoRigName);
        if (rig == null)
        {
            throw new InvalidOperationException("PICO XR Origin is missing.");
        }
        if (rig.GetComponent<XROrigin>() == null ||
            rig.GetComponent<PXR_Manager>() == null)
        {
            throw new InvalidOperationException(
                "PICO XR Origin is missing XROrigin or PXR_Manager.");
        }

        if (rig.GetComponentInChildren<XRInteractionManager>(true) == null ||
            rig.transform.Find("Camera Offset/Left Controller")
                ?.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>() == null ||
            rig.transform.Find("Camera Offset/Right Controller")
                ?.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.XRDirectInteractor>() == null)
        {
            throw new InvalidOperationException(
                "PICO controller grab interaction rig is incomplete.");
        }

        GameObject lamp = GameObject.Find("Spirit_Lamp with water");
        GameObject cube = GameObject.Find("[BuildingBlock] Cube");
        if (lamp == null || lamp.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() == null ||
            cube == null || cube.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>() == null)
        {
            throw new InvalidOperationException(
                "Quest grab objects were not converted to XR Grab Interactable.");
        }

        Camera mainCamera = Camera.main;
        if (mainCamera == null ||
            mainCamera.GetComponent<TrackedPoseDriver>() == null)
        {
            throw new InvalidOperationException(
                "PICO Main Camera or its TrackedPoseDriver is missing.");
        }

        GameObject streamCameraObject = GameObject.Find(StreamCameraName);
        Camera streamCamera =
            streamCameraObject != null ? streamCameraObject.GetComponent<Camera>() : null;
        StreamCameraFollow follow =
            streamCameraObject != null
                ? streamCameraObject.GetComponent<StreamCameraFollow>()
                : null;
        if (streamCamera == null || follow == null ||
            follow.vrCamera != mainCamera.transform)
        {
            throw new InvalidOperationException(
                "Stream camera is not following the PICO Main Camera.");
        }

        VideoStreamSender sender =
            UnityEngine.Object.FindObjectOfType<VideoStreamSender>(true);
        if (sender == null || sender.sourceCamera != streamCamera)
        {
            throw new InvalidOperationException(
                "VideoStreamSender is not bound to streamcamera.");
        }

        foreach (PlayerController controller in
                 UnityEngine.Object.FindObjectsOfType<PlayerController>(true))
        {
            if (controller.renderStreamingCamera != streamCamera)
            {
                throw new InvalidOperationException(
                    "A PlayerController is not bound to streamcamera.");
            }
        }

        int missingScripts = 0;
        foreach (GameObject gameObject in scene.GetRootGameObjects()
                     .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                     .Select(transform => transform.gameObject))
        {
            int objectMissingScripts =
                GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            if (objectMissingScripts > 0)
            {
                Debug.LogError(
                    $"[PICO MIGRATION] Missing scripts={objectMissingScripts} on " +
                    GetHierarchyPath(gameObject.transform));
                missingScripts += objectMissingScripts;
            }
        }
        if (missingScripts != 0)
        {
            throw new InvalidOperationException(
                $"The migrated main scene contains {missingScripts} missing scripts.");
        }

        XRGeneralSettings general =
            XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(
                BuildTargetGroup.Android);
        bool hasPicoLoader =
            general != null && general.Manager != null &&
            general.Manager.activeLoaders.Any(
                loader => loader != null &&
                          loader.GetType().FullName == PicoLoaderType);
        if (!hasPicoLoader)
        {
            throw new InvalidOperationException("PXR Loader is not assigned for Android.");
        }

        PXR_Settings picoSettings = PXR_Settings.GetSettings();
        if (picoSettings == null)
        {
            throw new InvalidOperationException(
                "PXR Settings are not registered in EditorBuildSettings.");
        }

        RenderStreamingSettings renderStreamingSettings =
            AssetDatabase.LoadAssetAtPath<RenderStreamingSettings>(
                RenderStreamingSettingsPath);
        SerializedObject renderStreamingSerialized =
            renderStreamingSettings != null
                ? new SerializedObject(renderStreamingSettings)
                : null;
        SerializedProperty signaling =
            renderStreamingSerialized?.FindProperty("signalingSettings");
        SerializedProperty url =
            signaling != null ? signaling.FindPropertyRelative("m_url") : null;
        if (url == null || url.stringValue != SignalingUrl)
        {
            throw new InvalidOperationException(
                $"Render Streaming signaling URL is not {SignalingUrl}.");
        }
    }

    private static string GetHierarchyPath(Transform transform)
    {
        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }
        return path;
    }
}
