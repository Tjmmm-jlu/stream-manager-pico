using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public static class PicoHandPinchGrabBootstrap
{
    private const string ExperimentSceneName =
        "ContinuousExperiment_CopperSulfate_AStage";
    private const float PinchRadius = 0.04f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= ConfigureLoadedScene;
        SceneManager.sceneLoaded += ConfigureLoadedScene;
    }

    private static void ConfigureLoadedScene(Scene scene, LoadSceneMode mode)
    {
        if (scene.name != ExperimentSceneName && scene.name != "ContinuousExperiment_CopperSulfate_Stationary")
            return;

        Configure(scene);
    }

    public static bool Configure(Scene scene)
    {
        Transform origin = FindSceneTransform(scene, "PICO XR Origin");
        Transform cameraOffset = origin != null
            ? origin.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == "Camera Offset")
            : null;
        if (cameraOffset == null)
        {
            Debug.LogError(
                "[PICO Hand Grab] XR Origin or Camera Offset is missing.");
            return false;
        }

        if (!SetControllerActive(cameraOffset, "Left Controller", false) ||
            !SetControllerActive(cameraOffset, "Right Controller", false))
        {
            Debug.LogError(
                "[PICO Hand Grab] Existing controller interactors were not found.");
            return false;
        }

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

        Debug.Log(
            "[PICO Hand Grab] Hand pinch grab active; controller grab disabled.");
        return true;
    }

    private static void ConfigureHandInteractor(
        Transform parent,
        string objectName,
        string usage,
        InteractorHandedness handedness)
    {
        Transform existing = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == objectName);
        GameObject handObject = existing != null
            ? existing.gameObject
            : new GameObject(objectName);

        handObject.SetActive(false);
        Transform handTransform = handObject.transform;
        handTransform.SetParent(parent, false);
        handTransform.localPosition = Vector3.zero;
        handTransform.localRotation = Quaternion.identity;
        handTransform.localScale = Vector3.one;

        SphereCollider collider = GetOrAdd<SphereCollider>(handObject);
        collider.isTrigger = true;
        collider.radius = PinchRadius;

        PicoStablePinchInputReader pinchInput =
            GetOrAdd<PicoStablePinchInputReader>(handObject);
        pinchInput.Configure(usage);

        XRDirectInteractor interactor = GetOrAdd<XRDirectInteractor>(handObject);
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

        TrackedPoseDriver poseDriver = GetOrAdd<TrackedPoseDriver>(handObject);
        poseDriver.trackingType =
            TrackedPoseDriver.TrackingType.RotationAndPosition;
        poseDriver.updateType =
            TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        poseDriver.ignoreTrackingState = false;
        poseDriver.positionInput = CreatePoseAction(
            $"{objectName} Position",
            $"<XRHandDevice>{{{usage}}}/pinchPosition",
            "Vector3");
        poseDriver.rotationInput = CreatePoseAction(
            $"{objectName} Rotation",
            $"<XRHandDevice>{{{usage}}}/pinchRotation",
            "Quaternion");
        poseDriver.trackingStateInput = CreatePoseAction(
            $"{objectName} Tracking State",
            $"<XRHandDevice>{{{usage}}}/trackingState",
            "Integer");

        handObject.SetActive(true);
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

    private static InputActionProperty CreatePoseAction(
        string name,
        string binding,
        string expectedControlType)
    {
        return new InputActionProperty(new InputAction(
            name,
            InputActionType.Value,
            binding,
            expectedControlType: expectedControlType));
    }

    private static T GetOrAdd<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    private static bool SetControllerActive(
        Transform parent,
        string objectName,
        bool active)
    {
        Transform controller = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == objectName);
        if (controller == null)
            return false;

        controller.gameObject.SetActive(active);
        return true;
    }

    private static Transform FindSceneTransform(Scene scene, string objectName)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == objectName);
    }

}
