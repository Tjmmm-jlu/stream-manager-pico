using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ExperimentVrLayoutSelectorInstaller
{
    // Revision 4: serialize collision-only controls; no XRI objects are written to level0.
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/ExperimentVrLayoutSelectorInstaller.request";
    private const string ReportPath = "Temp/ExperimentVrLayoutSelectorInstaller.txt";
    private const string SelectorRootName = "VR Experiment Scene Selector";

    [InitializeOnLoadMethod]
    private static void RunRequested()
    {
        if (!File.Exists(Path.GetFullPath(RequestPath))) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequested();
                return;
            }
            Install();
            File.Delete(Path.GetFullPath(RequestPath));
        };
    }

    [MenuItem("Tools/Stream Manager/Distractors/Install PICO Ray Scene Selector")]
    public static void Install()
    {
        Scene scene = string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ExperimentDistractorLayoutGenerator generator =
            UnityEngine.Object.FindObjectOfType<ExperimentDistractorLayoutGenerator>(true);
        if (generator == null) throw new InvalidOperationException("Distractor generator is missing.");

        GameObject legacyInteractionManager = scene.GetRootGameObjects()
            .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<Transform>(true))
            .Where(item => item.name == "XR Interaction Manager")
            .Select(item => item.gameObject)
            .FirstOrDefault();
        if (legacyInteractionManager != null)
            UnityEngine.Object.DestroyImmediate(legacyInteractionManager);

        GameObject previous = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SelectorRootName);
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);

        var root = new GameObject(SelectorRootName);
        root.transform.position = new Vector3(6.22f, 1.35f, 5.70f);
        root.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        ExperimentVrLayoutSelector selector = root.AddComponent<ExperimentVrLayoutSelector>();

        GameObject backplate = GameObject.CreatePrimitive(PrimitiveType.Cube);
        backplate.name = "Selector Backplate";
        backplate.transform.SetParent(root.transform, false);
        backplate.transform.localScale = new Vector3(0.88f, 0.42f, 0.025f);
        UnityEngine.Object.DestroyImmediate(backplate.GetComponent<Collider>());
        SetRendererColor(backplate.GetComponent<Renderer>(), new Color(0.035f, 0.05f, 0.08f));

        CreateText(root.transform, "Title", "EXPERIMENT LAYOUT", new Vector3(0f, 0.15f, -0.025f), 0.012f);
        TextMesh status = CreateText(root.transform, "Status", "Current: CLEAN (8 targets)", new Vector3(0f, -0.14f, -0.025f), 0.009f);
        selector.Configure(generator, status, generator.RandomSeed);

        var specifications = new[]
        {
            new ButtonSpec("LOW\n4", -0.32f, ExperimentLayoutSelection.Low, new Color(0.12f, 0.48f, 0.92f)),
            new ButtonSpec("MED\n8", -0.105f, ExperimentLayoutSelection.Medium, new Color(0.15f, 0.72f, 0.42f)),
            new ButtonSpec("HIGH\n10", 0.11f, ExperimentLayoutSelection.High, new Color(0.95f, 0.55f, 0.12f)),
            new ButtonSpec("CLEAR", 0.325f, ExperimentLayoutSelection.Clear, new Color(0.82f, 0.18f, 0.20f))
        };
        foreach (ButtonSpec specification in specifications)
        {
            CreateButton(root.transform, selector, specification);
        }

        // Serialize the lightweight PICO ray and its built-in mesh beam in the
        // same pass. Runtime creation of renderers crashes this device's GLES
        // graphics worker, while these prebuilt objects are stable.
        PicoExperimentLayoutRayInstaller.Install();

        SceneObjectRegistry registry = UnityEngine.Object.FindObjectOfType<SceneObjectRegistry>(true);
        generator.ClearGenerated();
        registry?.Refresh();
        if (registry == null || registry.Count != 8 || generator.GeneratedCount != 0)
            throw new InvalidOperationException("Clean scene validation failed after selector installation.");

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllLines(Path.GetFullPath(ReportPath), new[]
        {
            $"Scene: {ScenePath}",
            $"Selector root: {SelectorRootName}",
            $"World position: {root.transform.position}",
            "Interaction: PICO hand/controller collision ray (no XRI)",
            "Legacy serialized XR Interaction Manager: removed",
            "XRI manager lifecycle: created before scene load by code",
            "Buttons: LOW 4, MED 8, HIGH 10, CLEAR",
            $"Placement surface: {generator.PlacementSurface?.name ?? "missing"}",
            $"Placement top Y: {generator.PlacementArea?.position.y:F4}",
            $"Final generated distractors: {generator.GeneratedCount}",
            $"Final registry count: {registry.Count}"
        });
        Debug.Log("[VrLayoutSelectorInstaller] PICO ray selector installed and clean base restored.");
    }

    private static void CreateButton(
        Transform parent,
        ExperimentVrLayoutSelector selector,
        ButtonSpec specification)
    {
        GameObject button = GameObject.CreatePrimitive(PrimitiveType.Cube);
        button.name = $"Button {specification.Selection}";
        button.transform.SetParent(parent, false);
        button.transform.localPosition = new Vector3(specification.X, 0.015f, -0.04f);
        button.transform.localScale = new Vector3(0.18f, 0.16f, 0.055f);
        Renderer renderer = button.GetComponent<Renderer>();
        SetRendererColor(renderer, specification.Color);
        ExperimentVrLayoutButton action = button.AddComponent<ExperimentVrLayoutButton>();
        action.Configure(selector, specification.Selection, renderer, specification.Color);
        CreateText(parent, $"Label {specification.Selection}", specification.Label,
            new Vector3(specification.X, 0.015f, -0.075f), 0.0085f);
    }

    private static TextMesh CreateText(
        Transform parent,
        string name,
        string content,
        Vector3 localPosition,
        float characterSize)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        textObject.transform.localPosition = localPosition;
        TextMesh text = textObject.AddComponent<TextMesh>();
        text.text = content;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 64;
        text.characterSize = characterSize;
        text.color = Color.white;
        return text;
    }

    private static void SetRendererColor(Renderer renderer, Color color)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        block.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(block);
    }

    private readonly struct ButtonSpec
    {
        public ButtonSpec(string label, float x, ExperimentLayoutSelection selection, Color color)
        {
            Label = label;
            X = x;
            Selection = selection;
            Color = color;
        }
        public string Label { get; }
        public float X { get; }
        public ExperimentLayoutSelection Selection { get; }
        public Color Color { get; }
    }
}
