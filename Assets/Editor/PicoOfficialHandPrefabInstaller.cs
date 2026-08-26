using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PicoOfficialHandPrefabInstaller
{
    private const string ScenePath = "Assets/Scenes/Preparation Experiment Scene_AStage.unity";
    private const string RequestPath = "Temp/PicoOfficialHandPrefabInstaller.request";
    private const string LeftPrefabGuid = "6abc76b40b133154a91d14528cc20ba5";
    private const string RightPrefabGuid = "e664d688bb65af54a9146781a2b6f500";

    [InitializeOnLoadMethod]
    private static void RunRequested()
    {
        string request = Path.GetFullPath(RequestPath);
        if (!File.Exists(request)) return;

        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                RunRequested();
                return;
            }

            Install();
            File.Delete(request);
            Debug.Log("[PicoOfficialHandPrefabInstaller] Requested hand installation completed.");
        };
    }

    public static void Install()
    {
        Scene scene = string.Equals(SceneManager.GetActiveScene().path, ScenePath,
                StringComparison.OrdinalIgnoreCase)
            ? SceneManager.GetActiveScene()
            : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        Transform origin = scene.GetRootGameObjects()
            .FirstOrDefault(item => item.name == "PICO XR Origin")?.transform;
        if (origin == null) throw new InvalidOperationException("PICO XR Origin was not found.");

        Transform cameraOffset = origin.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(item => item.name == "Camera Offset");
        if (cameraOffset == null)
            throw new InvalidOperationException("PICO XR Origin/Camera Offset was not found.");

        InstallHand(cameraOffset, LeftPrefabGuid, "HandLeft");
        InstallHand(cameraOffset, RightPrefabGuid, "HandRight");

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene))
            throw new InvalidOperationException("Could not save the scene after installing PICO hands.");
    }

    private static void InstallHand(Transform parent, string prefabGuid, string instanceName)
    {
        Transform existing = parent.Cast<Transform>()
            .FirstOrDefault(child => child.name == instanceName);
        if (existing != null) UnityEngine.Object.DestroyImmediate(existing.gameObject);

        string prefabPath = AssetDatabase.GUIDToAssetPath(prefabGuid);
        if (string.IsNullOrWhiteSpace(prefabPath))
            throw new FileNotFoundException($"PICO official {instanceName} prefab GUID is unresolved: {prefabGuid}");
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) throw new FileNotFoundException($"PICO official hand prefab is missing: {prefabPath}");

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (instance == null) throw new InvalidOperationException($"Could not instantiate {instanceName}.");
        instance.name = instanceName;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        bool hasOfficialHand = instance.GetComponents<MonoBehaviour>()
            .Any(component => component != null && component.GetType().Name == "PXR_Hand");
        if (!hasOfficialHand)
            throw new InvalidOperationException($"{instanceName} does not contain the official PXR_Hand component.");
    }
}
