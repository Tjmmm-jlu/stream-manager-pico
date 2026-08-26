using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the experiment layout panel and the PICO hand ray after the scene has
/// loaded. Keeping these objects out of the serialized scene avoids the level0
/// corruption seen when XRI objects were written into the migrated scene.
/// </summary>
public sealed class ExperimentRuntimeUiBootstrap : MonoBehaviour
{
    private const string BootstrapName = "Runtime Experiment UI Bootstrap";
    private const string PanelName = "VR Experiment Scene Selector";
    private const string RayName = "PICO Experiment Layout Ray";

    // Intentionally not registered with RuntimeInitializeOnLoadMethod.
    // On the migrated PICO/GLES build, even running a scene-loaded bootstrap
    // callback immediately before the first scene frame is followed by a
    // native UnityGfxDeviceW null dereference. The stable build therefore
    // leaves this utility dormant until the UI is redesigned.
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (!scene.IsValid() || FindObjectOfType<ExperimentDistractorLayoutGenerator>(true) == null)
        {
            return;
        }

        if (GameObject.Find(PanelName) != null && GameObject.Find(RayName) != null)
        {
            Debug.Log(
                "[RuntimeExperimentUi] Prebuilt collision panel and PICO mesh ray are ready.");
            return;
        }

        // Do not create renderers at runtime on this migrated PICO scene. On
        // the target GLES driver that causes a null dereference on
        // UnityGfxDeviceW. The editor installer must prebuild both objects.
        Debug.LogError(
            "[RuntimeExperimentUi] Prebuilt panel/ray missing; runtime creation " +
            "is disabled for PICO graphics stability.");
    }

    private IEnumerator Start()
    {
        // Let the PICO hand prefabs and XR loader finish their first-frame setup.
        yield return null;

        ExperimentDistractorLayoutGenerator generator =
            FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        if (generator == null)
        {
            Debug.LogWarning("[RuntimeExperimentUi] Distractor generator was not found.");
            yield break;
        }

        ExperimentVrLayoutSelector selector = CreatePanel(generator);
        bool rayCreated = CreatePicoRay(selector);

        Debug.Log(
            $"[RuntimeExperimentUi] Created collision panel and mesh beam. " +
            $"PICO right-hand ray={(rayCreated ? "ready" : "unavailable")}.",
            this);
    }

    private static ExperimentVrLayoutSelector CreatePanel(
        ExperimentDistractorLayoutGenerator generator)
    {
        GameObject existing = GameObject.Find(PanelName);
        if (existing != null)
        {
            ExperimentVrLayoutSelector existingSelector =
                existing.GetComponent<ExperimentVrLayoutSelector>();
            if (existingSelector != null)
            {
                return existingSelector;
            }
            Destroy(existing);
        }

        var root = new GameObject(PanelName);
        root.transform.position = new Vector3(6.22f, 1.35f, 5.70f);
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        ExperimentVrLayoutSelector selector =
            root.AddComponent<ExperimentVrLayoutSelector>();

        GameObject backplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backplate.name = "Selector Backplate";
        backplate.transform.SetParent(root.transform, false);
        backplate.transform.localScale = new Vector3(0.88f, 0.42f, 0.025f);
        Collider backplateCollider = backplate.GetComponent<Collider>();
        if (backplateCollider != null) Destroy(backplateCollider);
        SetRendererColor(
            backplate.GetComponent<Renderer>(),
            new Color(0.035f, 0.05f, 0.08f, 1f));

        CreateText(
            root.transform,
            "Title",
            "EXPERIMENT LAYOUT",
            new Vector3(0f, 0.15f, -0.025f),
            0.012f,
            Color.white);
        TextMesh status = CreateText(
            root.transform,
            "Status",
            "Current: CLEAN (8 targets)",
            new Vector3(0f, -0.14f, -0.025f),
            0.009f,
            new Color(0.25f, 0.9f, 1f, 1f));
        selector.Configure(generator, status, generator.RandomSeed);

        CreateButton(
            root.transform,
            selector,
            "LOW\n4",
            -0.32f,
            ExperimentLayoutSelection.Low,
            new Color(0f, 0.52f, 1f, 1f));
        CreateButton(
            root.transform,
            selector,
            "MED\n8",
            -0.105f,
            ExperimentLayoutSelection.Medium,
            new Color(0f, 0.82f, 0.28f, 1f));
        CreateButton(
            root.transform,
            selector,
            "HIGH\n10",
            0.11f,
            ExperimentLayoutSelection.High,
            new Color(1f, 0.38f, 0f, 1f));
        CreateButton(
            root.transform,
            selector,
            "CLEAR",
            0.325f,
            ExperimentLayoutSelection.Clear,
            new Color(0.95f, 0.02f, 0.12f, 1f));

        generator.ClearGenerated();
        return selector;
    }

    private static void CreateButton(
        Transform parent,
        ExperimentVrLayoutSelector selector,
        string label,
        float x,
        ExperimentLayoutSelection selection,
        Color color)
    {
        GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = $"Button {selection}";
        button.transform.SetParent(parent, false);
        button.transform.localPosition = new Vector3(x, 0.015f, -0.04f);
        button.transform.localScale = new Vector3(0.18f, 0.16f, 0.055f);

        Renderer renderer = button.GetComponent<Renderer>();
        SetRendererColor(renderer, color);
        ExperimentVrLayoutButton action =
            button.AddComponent<ExperimentVrLayoutButton>();
        action.Configure(selector, selection, renderer, color);

        CreateText(
            parent,
            $"Label {selection}",
            label,
            new Vector3(x, 0.015f, -0.075f),
            0.0085f,
            Color.white);
    }

    private static TextMesh CreateText(
        Transform parent,
        string objectName,
        string content,
        Vector3 localPosition,
        float characterSize,
        Color color)
    {
        var textObject = new GameObject(objectName);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.text = content;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = characterSize;
        text.color = color;
        return text;
    }

    private static bool CreatePicoRay(ExperimentVrLayoutSelector selector)
    {
        if (selector == null || GameObject.Find(RayName) != null)
        {
            return selector != null;
        }

        Transform handRight = FindTransform("HandRight");
        Transform rayPose = FindDescendant(handRight, "RayPose");
        MonoBehaviour picoHand = FindComponentByTypeName(handRight, "PXR_Hand");
        if (handRight == null || rayPose == null || picoHand == null)
        {
            Debug.LogWarning(
                "[RuntimeExperimentUi] Official PICO HandRight/PXR_Hand/RayPose " +
                "was not found; panel remains available but the custom hand ray " +
                "could not be created.");
            return false;
        }

        var root = new GameObject(RayName);
        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Ray Visual";
        beam.transform.SetParent(root.transform, false);
        Collider beamCollider = beam.GetComponent<Collider>();
        if (beamCollider != null) Destroy(beamCollider);
        Renderer beamRenderer = beam.GetComponent<Renderer>();
        beamRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        beamRenderer.receiveShadows = false;
        SetRendererColor(
            beamRenderer,
            new Color(0.15f, 0.75f, 1f, 0.85f));

        PicoExperimentLayoutRaySelector raySelector =
            root.AddComponent<PicoExperimentLayoutRaySelector>();
        raySelector.Configure(rayPose, picoHand, selector, null, 5f);
        raySelector.ConfigureBeam(beamRenderer);
        return true;
    }

    private static Transform FindTransform(string objectName)
    {
        Transform[] transforms = FindObjectsOfType<Transform>(true);
        foreach (Transform candidate in transforms)
        {
            if (candidate != null && candidate.name == objectName)
            {
                return candidate;
            }
        }
        return null;
    }

    private static Transform FindDescendant(Transform parent, string objectName)
    {
        if (parent == null) return null;
        Transform[] transforms = parent.GetComponentsInChildren<Transform>(true);
        foreach (Transform candidate in transforms)
        {
            if (candidate != null && candidate.name == objectName)
            {
                return candidate;
            }
        }
        return null;
    }

    private static MonoBehaviour FindComponentByTypeName(
        Transform parent,
        string typeName)
    {
        if (parent == null) return null;
        MonoBehaviour[] components = parent.GetComponents<MonoBehaviour>();
        foreach (MonoBehaviour component in components)
        {
            if (component != null && component.GetType().Name == typeName)
            {
                return component;
            }
        }
        return null;
    }

    private static void SetRendererColor(Renderer renderer, Color color)
    {
        if (renderer == null) return;
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        block.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(block);
    }
}
