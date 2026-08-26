using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class OpenPicoMainSceneOnLoad
{
    private const string MainScenePath =
        "Assets/Scenes/Preparation Experiment Scene.unity";

    private static double _openAt;

    static OpenPicoMainSceneOnLoad()
    {
        if (Application.isBatchMode)
        {
            return;
        }

        _openAt = EditorApplication.timeSinceStartup + 2.5d;
        EditorApplication.update -= OpenWhenEditorIsStable;
        EditorApplication.update += OpenWhenEditorIsStable;
    }

    [MenuItem("PICO/Open Preparation Experiment Scene")]
    public static void OpenMainScene()
    {
        EditorApplication.update -= OpenWhenEditorIsStable;
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path == MainScenePath)
        {
            return;
        }

        SceneAsset mainScene =
            AssetDatabase.LoadAssetAtPath<SceneAsset>(MainScenePath);
        if (mainScene == null)
        {
            return;
        }

        Scene activeScene = SceneManager.GetActiveScene();
        if (activeScene.IsValid() && activeScene.isDirty)
        {
            EditorSceneManager.SaveScene(activeScene);
        }

        EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
        Debug.Log(
            $"[Preparation Builder] Opened stable default scene: {MainScenePath}");
    }

    private static void OpenWhenEditorIsStable()
    {
        if (EditorApplication.timeSinceStartup < _openAt ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating)
        {
            return;
        }

        OpenMainScene();
    }
}
