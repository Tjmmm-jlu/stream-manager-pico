using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>Controller-only ray interaction for the LargeTables experiment.</summary>
public static class PicoControllerGrabBootstrap
{
    private const string SceneName = "ContinuousExperiment_CopperSulfate_LargeTables";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (scene.name == SceneName) Configure(scene);
    }

    public static bool Configure(Scene scene)
    {
        Transform origin = Find(scene, "PICO XR Origin");
        Transform offset = origin?.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(t => t.name == "Camera Offset");
        if (offset == null)
        {
            Debug.LogError("[PICO Controller Ray] PICO XR Origin/Camera Offset is missing.");
            return false;
        }

        XRInteractionManager manager = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<XRInteractionManager>(true))
            .FirstOrDefault();
        bool left = ConfigureController(offset, "Left Controller", InteractorHandedness.Left, manager);
        bool right = ConfigureController(offset, "Right Controller", InteractorHandedness.Right, manager);
        DisableHandObjects(origin);

        var runner = origin.GetComponent<PicoControllerRayTracking>();
        if (runner == null) runner = origin.gameObject.AddComponent<PicoControllerRayTracking>();
        runner.RefreshControllers();

        Debug.Log(left && right
            ? "[PICO Controller Ray] Controller ray mode enabled: hold Grip to grab/release."
            : "[PICO Controller Ray] One or both controller objects are missing.");
        return left && right;
    }

    private static bool ConfigureController(Transform parent, string name,
        InteractorHandedness handedness, XRInteractionManager manager)
    {
        Transform target = parent.Cast<Transform>().FirstOrDefault(t => t.name == name);
        if (target == null) return false;
        target.gameObject.SetActive(true);

        var controller = target.GetComponent<ActionBasedController>();
        if (controller == null) return false;
        controller.enabled = true;
        controller.enableInputTracking = true;
        controller.enableInputActions = true;

        foreach (var direct in target.GetComponentsInChildren<XRDirectInteractor>(true)) direct.enabled = false;
        foreach (var collider in target.GetComponentsInChildren<Collider>(true)) collider.enabled = false;

        Transform rayRoot = target.Find("Controller Ray");
        if (rayRoot == null)
        {
            rayRoot = new GameObject("Controller Ray").transform;
            rayRoot.SetParent(target, false);
        }
        var ray = rayRoot.GetComponent<XRRayInteractor>();
        if (ray == null) ray = rayRoot.gameObject.AddComponent<XRRayInteractor>();
        ray.enabled = false;
        ray.handedness = handedness;
        ray.interactionManager = manager;
        // Explicit readers avoid the legacy controller silently overriding the XRI 3 bindings.
#pragma warning disable CS0618
        ray.inputCompatibilityMode = XRBaseInputInteractor.InputCompatibilityMode.ForceInputReaders;
#pragma warning restore CS0618
        ray.selectActionTrigger = XRBaseInputInteractor.InputTriggerType.StateChange;
        ray.keepSelectedTargetValid = true;
        ray.hoverToSelect = false;
        ray.autoDeselect = false;
        ray.lineType = XRRayInteractor.LineType.StraightLine;
        ray.hitDetectionType = XRRayInteractor.HitDetectionType.Raycast;
        ray.maxRaycastDistance = 5f;
        ray.hitClosestOnly = true;
        ray.raycastMask = Physics.DefaultRaycastLayers;
        ray.raycastTriggerInteraction = QueryTriggerInteraction.Ignore;
        ray.useForceGrab = true;
        ray.manipulateAttachTransform = true;
        ray.translateSpeed = 0.6f;
        ray.rotateSpeed = 60f;
        ray.enableUIInteraction = true;
        ray.rayOriginTransform = rayRoot;
        Transform attach = rayRoot.Find("Ray Hold Point");
        if (attach == null)
        {
            attach = new GameObject("Ray Hold Point").transform;
            attach.SetParent(rayRoot, false);
        }
        attach.localPosition = new Vector3(0f, 0f, 0.18f);
        attach.localRotation = Quaternion.identity;
        ray.attachTransform = attach;

        string device = handedness == InteractorHandedness.Left ? "<XRController>{LeftHand}/" : "<XRController>{RightHand}/";
        BindButton(ray.selectInput, device, "gripPressed", "grip");
        BindButton(ray.activateInput, device, "triggerPressed", "trigger");
        BindButton(ray.uiPressInput, device, "triggerPressed", "trigger");
        BindAxis(ray.translateManipulationInput, device + "primary2DAxis");
        BindAxis(ray.rotateManipulationInput, device + "primary2DAxis");

        var renderer = rayRoot.GetComponent<LineRenderer>();
        if (renderer == null) renderer = rayRoot.gameObject.AddComponent<LineRenderer>();
        renderer.sharedMaterial = Resources.Load<Material>("PicoControllerRay");
        if (renderer.sharedMaterial == null)
            Debug.LogError("[PICO Controller Ray] Missing Resources/PicoControllerRay material. Run the ray installer.");
        renderer.useWorldSpace = true;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.allowOcclusionWhenDynamic = false;
        renderer.alignment = LineAlignment.View;
        var line = rayRoot.GetComponent<XRInteractorLineVisual>();
        if (line == null) line = rayRoot.gameObject.AddComponent<XRInteractorLineVisual>();
        line.enabled = true;
        line.lineWidth = 0.003f;
        line.overrideInteractorLineOrigin = false;
        line.overrideInteractorLineLength = true;
        line.lineLength = 5f;
        line.smoothMovement = true;
        line.stopLineAtSelection = true;
        line.treatSelectionAsValidState = true;
        line.validColorGradient = Solid(new Color(0.15f, 0.95f, 0.45f));
        line.invalidColorGradient = Solid(handedness == InteractorHandedness.Left
            ? new Color(0.2f, 0.75f, 1f) : new Color(1f, 0.7f, 0.2f));
        ray.enabled = true;
        return true;
    }

    private static void BindButton(XRInputButtonReader reader, string device, string button, string value)
    {
        reader.inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction;
        if (reader.inputActionPerformed == null || reader.inputActionPerformed.bindings.Count != 1 ||
            reader.inputActionPerformed.bindings[0].path != device + button)
            reader.inputActionPerformed = new InputAction(button, InputActionType.Button, device + button);
        if (reader.inputActionValue == null || reader.inputActionValue.bindings.Count != 1 ||
            reader.inputActionValue.bindings[0].path != device + value)
            reader.inputActionValue = new InputAction(value, InputActionType.Value, device + value, expectedControlType: "Axis");
    }

    private static void BindAxis(XRInputValueReader<Vector2> reader, string path)
    {
        reader.inputSourceMode = XRInputValueReader.InputSourceMode.InputAction;
        if (reader.inputAction == null || reader.inputAction.bindings.Count != 1 || reader.inputAction.bindings[0].path != path)
            reader.inputAction = new InputAction("Manipulate", InputActionType.Value, path,
                processors: "stickDeadzone(min=0.25,max=0.95)", expectedControlType: "Vector2");
    }

    private static Gradient Solid(Color color) => new Gradient
    {
        colorKeys = new[] { new GradientColorKey(color, 0), new GradientColorKey(color, 1) },
        alphaKeys = new[] { new GradientAlphaKey(1f, 0), new GradientAlphaKey(1f, 1) }
    };

    public static void DisableHandObjects(Transform origin)
    {
        foreach (var t in origin.GetComponentsInChildren<Transform>(true).ToArray())
        {
            if (t == origin) continue;
            string n = t.name.ToLowerInvariant();
            if (n.StartsWith("handleft") || n.StartsWith("handright") || n.Contains("hand pinch grab") ||
                n.Contains("pico hand")) t.gameObject.SetActive(false);
        }
        foreach (var hand in origin.GetComponentsInChildren<MonoBehaviour>(true)
                     .Where(c => c != null && c.GetType().Name == "PXR_Hand"))
        {
            hand.enabled = false;
            hand.gameObject.SetActive(false);
        }
    }

    private static Transform Find(Scene scene, string name) => scene.GetRootGameObjects()
        .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
        .FirstOrDefault(t => t.name == name);

}
