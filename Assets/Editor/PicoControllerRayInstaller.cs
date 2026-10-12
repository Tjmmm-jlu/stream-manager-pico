using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[InitializeOnLoad]
public static class PicoControllerRayInstaller
{
    public const string ScenePath = "Assets/Scenes/ContinuousExperiment_CopperSulfate_LargeTables.unity";
    public const string Output = "Temp/ControllerRay";
    private static bool _busy;

    static PicoControllerRayInstaller() => EditorApplication.update += ProcessRequest;

    private static void ProcessRequest()
    {
        const string request = "Temp/ControllerRay.request";
        if (_busy || EditorApplication.isCompiling || EditorApplication.isUpdating ||
            EditorApplication.isPlayingOrWillChangePlaymode || !File.Exists(request)) return;
        _busy = true;
        try
        {
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (command == "install") Install();
            else if (command == "verify") PicoControllerRayVerification.Run();
            else if (command == "build") BuildApk();
            else throw new InvalidOperationException("Unknown controller ray command: " + command);
        }
        catch (Exception ex)
        {
            Directory.CreateDirectory(Output);
            File.WriteAllText(Output + "/result.txt", ex.ToString());
            Debug.LogException(ex);
        }
        finally { _busy = false; }
    }

    [MenuItem("Tools/Stream Manager/Controller Rays/Install In LargeTables")]
    public static void Install()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.path != ScenePath)
            throw new InvalidOperationException("Open the LargeTables scene in Edit Mode first.");
        string backup = "MigrationBackups/BeforeRayScene_" + DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        Directory.CreateDirectory(backup);
        File.Copy(ScenePath, backup + "/SavedScene.unity");
        if (scene.isDirty && !EditorSceneManager.SaveScene(scene, backup + "/UnsavedScene.unity", true))
            throw new IOException("Could not preserve the unsaved scene.");

        var poses = All<SemanticObject>(scene).ToDictionary(s => s.transform,
            s => (s.transform.position, s.transform.rotation, s.transform.localScale));
        const string materialPath = "Assets/Resources/PicoControllerRay.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
        {
            var shader = Shader.Find("Sprites/Default");
            if (shader == null) throw new InvalidOperationException("Ray shader is unavailable.");
            AssetDatabase.CreateAsset(new Material(shader) { name = "PicoControllerRay" }, materialPath);
            AssetDatabase.SaveAssets();
        }
        if (!PicoControllerGrabBootstrap.Configure(scene)) throw new InvalidOperationException("Ray configuration failed.");
        foreach (var entry in poses)
            if (entry.Key.position != entry.Value.position || entry.Key.rotation != entry.Value.rotation ||
                entry.Key.localScale != entry.Value.localScale)
                throw new InvalidOperationException("An apparatus pose changed: " + entry.Key.name);

        Validate(scene);
        foreach (var behaviour in All<MonoBehaviour>(scene).Where(b => b != null && b.GetType().Name == "PXR_Hand"))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour);
            PrefabUtility.RecordPrefabInstancePropertyModifications(behaviour.gameObject);
        }
        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Could not save controller rays.");
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/result.txt", "PASS: scene saved, two rays, no active hands/direct interactors, apparatus poses unchanged.\n");
    }

    public static void Validate(Scene scene)
    {
        var origin = scene.GetRootGameObjects().Single(r => r.name == "PICO XR Origin");
        var rays = origin.GetComponentsInChildren<XRRayInteractor>(true);
        if (rays.Length != 2 || rays.Count(r => r.handedness == InteractorHandedness.Left) != 1 ||
            rays.Count(r => r.handedness == InteractorHandedness.Right) != 1)
            throw new InvalidOperationException("Expected one left ray and one right ray.");
        if (origin.GetComponentsInChildren<XRDirectInteractor>().Any(d => d.enabled))
            throw new InvalidOperationException("An active direct interactor remains.");
        if (origin.GetComponentsInChildren<MonoBehaviour>().Any(b => b != null && b.GetType().Name == "PXR_Hand"))
            throw new InvalidOperationException("An active PICO hand root remains.");
        foreach (var ray in rays)
            if (ray.GetComponent<LineRenderer>()?.sharedMaterial == null || ray.selectInput.inputActionPerformed.bindings.Count != 1)
                throw new InvalidOperationException("Missing ray material or select binding.");
    }

    [MenuItem("PICO/Build LargeTables Controller-Ray APK", priority = 11)]
    public static void BuildApk()
    {
        var scene = SceneManager.GetActiveScene();
        if (EditorApplication.isPlayingOrWillChangePlaymode || scene.isDirty || scene.path != ScenePath)
            throw new InvalidOperationException("Save the LargeTables scene and exit Play Mode before building.");
        Validate(scene);
        Directory.CreateDirectory("Builds");
        string output = Path.GetFullPath("Builds/stream-manager-pico-large-tables-ray-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".apk");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath }, locationPathName = output,
            target = BuildTarget.Android, targetGroup = BuildTargetGroup.Android, options = BuildOptions.None
        });
        Directory.CreateDirectory(Output);
        File.WriteAllText(Output + "/build.txt", report.summary.result + "\n" + output +
            "\nErrors: " + report.summary.totalErrors + "\nWarnings: " + report.summary.totalWarnings + "\n");
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException("Controller-ray APK build failed: " + report.summary.result);
        Debug.Log("[PICO Controller Ray] Built " + output);
    }

    private static T[] All<T>(Scene scene) where T : Component => scene.GetRootGameObjects()
        .SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
}
