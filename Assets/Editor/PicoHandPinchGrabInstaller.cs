using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public static class PicoHandPinchGrabInstaller
{
    private const string ScenePath =
        "Assets/Scenes/ContinuousExperiment_CopperSulfate_AStage.unity";
    private const string RequestPath =
        "Temp/PicoHandPinchGrabInstaller.request";
    private const string BackupDirectory =
        "MigrationBackups/BeforePicoHandPinchGrab_20260902";
    private const float PinchRadius = 0.04f;

    [InitializeOnLoadMethod]
    private static void RunRequestedInstall()
    {
        string requestPath = Path.GetFullPath(RequestPath);
        if (!File.Exists(requestPath))
            return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequestedInstall();
                return;
            }

            Install();
            File.Delete(requestPath);
        };
    }

    [MenuItem("PICO/Configure Hand Pinch Grab (Controllers Off)", priority = 23)]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException(
                "Exit Play Mode before configuring hand pinch grab.");

        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        CreateSceneBackup();

        Transform origin = FindSceneTransform(scene, "PICO XR Origin");
        if (origin == null)
            throw new InvalidOperationException("PICO XR Origin was not found.");

        Transform cameraOffset = origin.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "Camera Offset");
        if (cameraOffset == null)
            throw new InvalidOperationException(
                "PICO XR Origin/Camera Offset was not found.");

        SetControllerActive(cameraOffset, "Left Controller", false);
        SetControllerActive(cameraOffset, "Right Controller", false);
        RemoveGeneratedInteractionManager(cameraOffset);

        ConfigureHandInteractor(
            cameraOffset,
            "Left Hand Pinch Grab",
            "LeftHand",
            InteractorHandedness.Left);
        ConfigureHandInteractor(
            cameraOffset,
            "Right Hand Pinch Grab",
            "RightHand",
            InteractorHandedness.Right);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new InvalidOperationException(
                "Could not save the hand pinch grab configuration.");

        AssetDatabase.SaveAssets();
        Debug.Log(
            "[PICO Hand Grab] Near-hand pinch grab configured; controller interactors disabled.");
    }

    [MenuItem("PICO/Restore Controller Grab (Hands Off)", priority = 24)]
    public static void RestoreControllers()
    {
        Scene scene = string.Equals(
                SceneManager.GetActiveScene().path,
                ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform origin = FindSceneTransform(scene, "PICO XR Origin");
        Transform cameraOffset = origin != null
            ? origin.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "Camera Offset")
            : null;
        if (cameraOffset == null)
            throw new InvalidOperationException(
                "PICO XR Origin/Camera Offset was not found.");

        RemoveGeneratedInteractionManager(cameraOffset);
        RemoveChild(cameraOffset, "Left Hand Pinch Grab");
        RemoveChild(cameraOffset, "Right Hand Pinch Grab");
        SetControllerActive(cameraOffset, "Left Controller", true);
        SetControllerActive(cameraOffset, "Right Controller", true);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[PICO Hand Grab] Controller grab restored; hand interactors removed.");
    }

    private static void ConfigureHandInteractor(
        Transform parent,
        string objectName,
        string usage,
        InteractorHandedness handedness)
    {
        RemoveChild(parent, objectName);

        GameObject handInteractorObject = new GameObject(objectName);
        handInteractorObject.SetActive(false);
        Transform handTransform = handInteractorObject.transform;
        handTransform.SetParent(parent, false);
        handTransform.localPosition = Vector3.zero;
        handTransform.localRotation = Quaternion.identity;
        handTransform.localScale = Vector3.one;

        SphereCollider collider = handInteractorObject.AddComponent<SphereCollider>();
        collider.isTrigger = true;
        collider.radius = PinchRadius;

        PicoStablePinchInputReader pinchInput =
            handInteractorObject.AddComponent<PicoStablePinchInputReader>();
        pinchInput.Configure(usage);

        XRDirectInteractor interactor =
            handInteractorObject.AddComponent<XRDirectInteractor>();
        interactor.interactionManager = null;
        interactor.handedness = handedness;
        interactor.attachTransform = handTransform;
        interactor.keepSelectedTargetValid = true;
        interactor.improveAccuracyWithSphereCollider = true;
        interactor.physicsLayerMask = 1 << 0;
        interactor.physicsTriggerInteraction = QueryTriggerInteraction.Ignore;
        interactor.selectActionTrigger =
            XRBaseInputInteractor.InputTriggerType.StateChange;
        interactor.selectInput = CreatePinchInput(objectName, pinchInput);
        interactor.activateInput.inputSourceMode =
            XRInputButtonReader.InputSourceMode.Unused;

        TrackedPoseDriver poseDriver =
            handInteractorObject.AddComponent<TrackedPoseDriver>();
        poseDriver.trackingType =
            TrackedPoseDriver.TrackingType.RotationAndPosition;
        poseDriver.updateType =
            TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        poseDriver.ignoreTrackingState = false;
        poseDriver.positionInput = CreateAction(
            $"{objectName} Position",
            InputActionType.Value,
            $"<XRHandDevice>{{{usage}}}/pinchPosition",
            "Vector3");
        poseDriver.rotationInput = CreateAction(
            $"{objectName} Rotation",
            InputActionType.Value,
            $"<XRHandDevice>{{{usage}}}/pinchRotation",
            "Quaternion");
        poseDriver.trackingStateInput = CreateAction(
            $"{objectName} Tracking State",
            InputActionType.Value,
            $"<XRHandDevice>{{{usage}}}/trackingState",
            "Integer");

        handInteractorObject.SetActive(true);
        EditorUtility.SetDirty(handInteractorObject);
        EditorUtility.SetDirty(interactor);
        EditorUtility.SetDirty(poseDriver);
    }

    private static XRInputButtonReader CreatePinchInput(
        string objectName,
        PicoStablePinchInputReader pinchInput)
    {
        XRInputButtonReader reader = new XRInputButtonReader(
            $"{objectName} Select",
            $"{objectName} Select Value",
            true,
            XRInputButtonReader.InputSourceMode.ObjectReference);
        reader.SetObjectReference(pinchInput);
        return reader;
    }

    private static InputActionProperty CreateAction(
        string name,
        InputActionType type,
        string binding,
        string expectedControlType)
    {
        return new InputActionProperty(new InputAction(
            name,
            type,
            binding,
            expectedControlType: expectedControlType));
    }

    private static Transform FindSceneTransform(Scene scene, string objectName)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == objectName);
    }

    private static void RemoveGeneratedInteractionManager(Transform parent)
    {
        string[] handObjectNames =
        {
            "Left Hand Pinch Grab",
            "Right Hand Pinch Grab"
        };

        foreach (string objectName in handObjectNames)
        {
            Transform hand = parent.Cast<Transform>()
                .FirstOrDefault(child => child.name == objectName);
            XRDirectInteractor interactor =
                hand != null ? hand.GetComponent<XRDirectInteractor>() : null;
            if (interactor == null || interactor.interactionManager == null)
                continue;

            UnityEngine.Object generatedManager = interactor.interactionManager;
            interactor.interactionManager = null;
            UnityEngine.Object.DestroyImmediate(generatedManager);
        }
    }

    private static void SetControllerActive(
        Transform parent,
        string objectName,
        bool active)
    {
        Transform controller = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == objectName);
        if (controller == null)
            throw new InvalidOperationException($"{objectName} was not found.");

        controller.gameObject.SetActive(active);
        EditorUtility.SetDirty(controller.gameObject);
    }

    private static void RemoveChild(Transform parent, string objectName)
    {
        Transform existing = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == objectName);
        if (existing != null)
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
    }

    private static void CreateSceneBackup()
    {
        string sceneAbsolutePath = Path.GetFullPath(ScenePath);
        string backupDirectory = Path.GetFullPath(BackupDirectory);
        Directory.CreateDirectory(backupDirectory);
        string backupPath = Path.Combine(
            backupDirectory,
            Path.GetFileName(ScenePath));

        if (!File.Exists(backupPath))
            File.Copy(sceneAbsolutePath, backupPath);
    }
}
