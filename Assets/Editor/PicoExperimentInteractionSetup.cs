using System;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;

public static class PicoExperimentInteractionSetup
{
    private const string ExperimentScenePath =
        "Assets/Scenes/Preparation Experiment Scene.unity";

    private static readonly Vector3 ParticipantPosition =
        new Vector3(5.35f, 0.15f, 6.10f);
    private static readonly Vector3 WorkstationCenter =
        new Vector3(5.35f, 0f, 5.45f);

    private static readonly string[] GrabbableTargetNames =
    {
        "china dish (1)",
        "Graduated_Cylinder (1)",
        "Beaker water",
        "Glass_medical_dropper",
        "florence_flask with water",
        "Spirit_Lamp with water",
        "Test_tube_rack",
        "glass_funnel (1)"
    };

    [MenuItem("PICO/Configure Experiment View And Objects", priority = 22)]
    public static void ApplyFromMenu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[PICO Interaction] Exit Play Mode before applying setup.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ExperimentScenePath)
        {
            scene = EditorSceneManager.OpenScene(
                ExperimentScenePath,
                OpenSceneMode.Single);
        }

        ApplyToOpenScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ExperimentScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[PICO Interaction] Viewpoint and object setup saved.");
    }

    public static void ApplyToOpenScene()
    {
        GameObject rig = GameObject.Find("PICO XR Origin");
        if (rig == null)
        {
            throw new InvalidOperationException("PICO XR Origin was not found.");
        }

        ConfigureParticipantStance(rig);
        ConfigureCamerasForHeadsetAndStreaming(rig);

        foreach (string targetName in GrabbableTargetNames)
        {
            GameObject target = FindTarget(targetName);
            if (target == null)
            {
                throw new InvalidOperationException(
                    $"Experiment grab target was not found: {targetName}");
            }

            EnsureGrabbable(target);
        }

        EditorUtility.SetDirty(rig);
    }

    private static void ConfigureParticipantStance(GameObject rig)
    {
        rig.transform.position = ParticipantPosition;
        Vector3 forward = WorkstationCenter - ParticipantPosition;
        forward.y = 0f;
        rig.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

        XROrigin origin = rig.GetComponent<XROrigin>();
        if (origin != null)
        {
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;
            EditorUtility.SetDirty(origin);
        }

        Camera camera = origin != null ? origin.Camera : Camera.main;
        TrackedPoseDriver driver = camera != null
            ? camera.GetComponent<TrackedPoseDriver>()
            : null;
        if (driver != null)
        {
            // Match PICO's official rig: reject invalid pose samples and
            // explicitly provide the HMD tracking-state control.
            driver.ignoreTrackingState = false;
            driver.trackingStateInput = CreateAction(
                "PICO HMD Tracking State",
                InputActionType.Value,
                "<XRHMD>/trackingState",
                "Integer");
            EditorUtility.SetDirty(driver);
        }
    }

    private static void ConfigureCamerasForHeadsetAndStreaming(GameObject rig)
    {
        XROrigin origin = rig.GetComponent<XROrigin>();
        Camera headsetCamera = origin != null ? origin.Camera : Camera.main;
        if (headsetCamera != null)
        {
            headsetCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            headsetCamera.allowHDR = false;
            headsetCamera.farClipPlane = 40f;
            EditorUtility.SetDirty(headsetCamera);
        }

        GameObject streamCameraObject = GameObject.Find("streamcamera");
        Camera streamCamera = streamCameraObject != null
            ? streamCameraObject.GetComponent<Camera>()
            : null;
        if (streamCamera != null)
        {
            // This camera is rendered into the WebRTC texture. It must not
            // also draw to the headset eyes, otherwise two slightly
            // different head poses are composited and appear as shaking.
            streamCamera.stereoTargetEye = StereoTargetEyeMask.None;
            streamCamera.allowHDR = false;
            streamCamera.farClipPlane = 40f;
            EditorUtility.SetDirty(streamCamera);
        }
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

    private static GameObject FindTarget(string targetName)
    {
        return UnityEngine.Object.FindObjectsOfType<Transform>(true)
            .Where(transform => transform.name == targetName)
            .Select(transform => transform.gameObject)
            .OrderBy(gameObject =>
                (GetCombinedBounds(gameObject).center - WorkstationCenter).sqrMagnitude)
            .FirstOrDefault();
    }

    private static void EnsureGrabbable(GameObject target)
    {
        ClearStaticFlags(target.transform);

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        if (!colliders.Any(collider => collider.enabled && !collider.isTrigger))
        {
            Bounds bounds = GetCombinedBounds(target);
            BoxCollider collider = target.GetComponent<BoxCollider>();
            if (collider == null)
            {
                collider = target.AddComponent<BoxCollider>();
            }

            collider.enabled = true;
            collider.isTrigger = false;
            collider.center = target.transform.InverseTransformPoint(bounds.center);
            Vector3 scale = target.transform.lossyScale;
            collider.size = new Vector3(
                bounds.size.x / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
                bounds.size.y / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
                bounds.size.z / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        }

        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body == null)
        {
            body = target.AddComponent<Rigidbody>();
        }
        body.useGravity = false;
        body.isKinematic = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable grab = target.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        if (grab == null)
        {
            grab = target.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
        }
        grab.movementType = UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable.MovementType.Kinematic;
        grab.throwOnDetach = false;
        grab.retainTransformParent = true;

        EditorUtility.SetDirty(target);
        EditorUtility.SetDirty(body);
        EditorUtility.SetDirty(grab);
    }

    private static void ClearStaticFlags(Transform root)
    {
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
        {
            GameObjectUtility.SetStaticEditorFlags(
                transform.gameObject,
                (StaticEditorFlags)0);
        }
    }

    private static Bounds GetCombinedBounds(GameObject target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return new Bounds(target.transform.position, Vector3.one * 0.15f);
        }

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
        {
            bounds.Encapsulate(renderers[index].bounds);
        }
        return bounds;
    }
}
