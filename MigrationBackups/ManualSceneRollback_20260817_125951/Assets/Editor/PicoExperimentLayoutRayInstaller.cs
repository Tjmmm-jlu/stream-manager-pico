using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PicoExperimentLayoutRayInstaller
{
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/PicoExperimentLayoutRayInstaller.request";
    private const string ReportPath = "Temp/PicoExperimentLayoutRayInstaller.txt";
    private const string RootName = "PICO Experiment Layout Ray";

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

    [MenuItem("Tools/Stream Manager/Distractors/Install PICO Hand Layout Ray")]
    public static void Install()
    {
        Scene scene = string.Equals(SceneManager.GetActiveScene().path, ScenePath, StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        Transform handRight = FindByName(scene, "HandRight");
        if (handRight == null) throw new InvalidOperationException("PICO HandRight was not found.");
        Transform rayPose = FindDescendant(handRight, "RayPose");
        if (rayPose == null) throw new InvalidOperationException("PICO HandRight/RayPose was not found.");
        MonoBehaviour picoHand = handRight.GetComponents<MonoBehaviour>()
            .FirstOrDefault(component => component != null && component.GetType().Name == "PXR_Hand");
        if (picoHand == null) throw new InvalidOperationException("Official PXR_Hand component was not found.");
        ExperimentVrLayoutSelector layoutSelector = UnityEngine.Object
            .FindObjectOfType<ExperimentVrLayoutSelector>(true);
        if (layoutSelector == null) throw new InvalidOperationException("VR layout selector was not found.");

        GameObject previous = scene.GetRootGameObjects().FirstOrDefault(root => root.name == RootName);
        if (previous != null) UnityEngine.Object.DestroyImmediate(previous);
        var root = new GameObject(RootName);

        GameObject beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
        beam.name = "Ray Visual";
        beam.transform.SetParent(root.transform, false);
        UnityEngine.Object.DestroyImmediate(beam.GetComponent<Collider>());
        Renderer beamRenderer = beam.GetComponent<Renderer>();
        beamRenderer.shadowCastingMode =
            UnityEngine.Rendering.ShadowCastingMode.Off;
        beamRenderer.receiveShadows = false;
        SetRendererColor(
            beamRenderer,
            new Color(0.15f, 0.75f, 1f, 0.85f));
        beamRenderer.enabled = false;

        PicoExperimentLayoutRaySelector selector = root.AddComponent<PicoExperimentLayoutRaySelector>();
        selector.Configure(rayPose, picoHand, layoutSelector, null, 5f);
        selector.ConfigureBeam(beamRenderer);

        string[] requiredButtonNames =
            { "Button Low", "Button Medium", "Button High", "Button Clear" };
        Transform[] sceneTransforms = scene.GetRootGameObjects()
            .SelectMany(sceneRoot => sceneRoot.GetComponentsInChildren<Transform>(true))
            .ToArray();
        foreach (string buttonName in requiredButtonNames)
        {
            Transform button = sceneTransforms.FirstOrDefault(item => item.name == buttonName);
            if (button == null || button.GetComponent<Collider>() == null)
                throw new InvalidOperationException($"Required ray button '{buttonName}' or its Collider is missing.");
        }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        File.WriteAllLines(Path.GetFullPath(ReportPath), new[]
        {
            $"Scene: {ScenePath}",
            $"Hand: {PathOf(handRight)}",
            $"Ray pose: {PathOf(rayPose)}",
            $"Hand source: {picoHand.GetType().FullName}",
            "Selection: PXR_Hand.Pinch rising edge or right-controller trigger",
            "Ray length: 5 m (prebuilt mesh beam, no runtime material)",
            $"Layout buttons: {requiredButtonNames.Length}",
            "Fallback: collider object name maps to Low/Medium/High/Clear",
            "Existing grab system modified: false"
        });
        Debug.Log("[PicoLayoutRayInstaller] Official PICO HandRight RayPose selector installed.");
    }

    private static void SetRendererColor(Renderer renderer, Color color)
    {
        var block = new MaterialPropertyBlock();
        renderer.GetPropertyBlock(block);
        block.SetColor("_Color", color);
        block.SetColor("_BaseColor", color);
        renderer.SetPropertyBlock(block);
    }

    private static Transform FindByName(Scene scene, string name)
    {
        return scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .FirstOrDefault(item => item.name == name);
    }

    private static Transform FindDescendant(Transform parent, string name)
    {
        return parent.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == name);
    }

    private static string PathOf(Transform transform)
    {
        var names = new Stack<string>();
        while (transform != null) { names.Push(transform.name); transform = transform.parent; }
        return string.Join("/", names);
    }
}
