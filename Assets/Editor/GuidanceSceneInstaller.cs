using System.Linq;
using Unity.RenderStreaming;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class GuidanceSceneInstaller
{
    private const string MainScenePath =
        "Assets/3D Laboratory Environment with Appratus/Scenes/Laboratory Scene.unity";
    private const string GuidanceFolder = "Assets/Guidance";
    private const string GuidanceMaterialPath =
        GuidanceFolder + "/GuidanceOverlay.mat";

    static GuidanceSceneInstaller()
    {
        if (!Application.isBatchMode)
        {
            EditorApplication.delayCall += InstallIfMainSceneIsOpen;
        }
    }

    [MenuItem("PICO/Install Guidance Baselines")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            RegisterInstallAfterPlayMode();
            Debug.Log(
                "[Guidance Installer] Waiting for Edit Mode before modifying the scene.");
            return;
        }

        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainScenePath)
        {
            if (scene.IsValid() && scene.isDirty &&
                !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                return;
            }

            scene = EditorSceneManager.OpenScene(
                MainScenePath, OpenSceneMode.Single);
        }

        GameObject rig = GameObject.Find("PICO XR Origin");
        GameObject streamCameraObject = GameObject.Find("streamcamera");
        Camera streamCamera =
            streamCameraObject != null
                ? streamCameraObject.GetComponent<Camera>()
                : null;
        if (rig == null || streamCamera == null)
        {
            Debug.LogError(
                "[Guidance Installer] PICO XR Origin or streamcamera is missing.");
            return;
        }

        Material overlayMaterial = GetOrCreateOverlayMaterial();
        if (overlayMaterial == null)
        {
            return;
        }

        GuidanceManager manager = rig.GetComponent<GuidanceManager>();
        if (manager == null)
        {
            manager = rig.AddComponent<GuidanceManager>();
        }
        manager.GuidanceCamera = streamCamera;
        manager.OverlayMaterial = overlayMaterial;

        GuidanceDataChannelReceiver receiver =
            rig.GetComponent<GuidanceDataChannelReceiver>();
        if (receiver == null)
        {
            receiver = rig.AddComponent<GuidanceDataChannelReceiver>();
        }
        receiver.GuidanceManager = manager;
        ConfigureReceiverSerialization(receiver);

        PlayerController controller = rig.GetComponent<PlayerController>();
        if (controller == null)
        {
            Debug.LogError(
                "[Guidance Installer] PlayerController is missing.");
            return;
        }
        controller.GuidanceManager = manager;

        Broadcast broadcast =
            Object.FindObjectOfType<Broadcast>(true);
        if (broadcast == null)
        {
            Debug.LogError("[Guidance Installer] Broadcast is missing.");
            return;
        }
        if (!broadcast.Streams.Contains(receiver))
        {
            broadcast.AddComponent(receiver);
        }

        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(receiver);
        EditorUtility.SetDirty(controller);
        EditorUtility.SetDirty(broadcast);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, MainScenePath);
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[Guidance Installer] Installed ellipse-outline and spatial-arrow " +
            "baselines. The guidance DataChannel is registered after input and gaze.");
    }

    private static void InstallIfMainSceneIsOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            RegisterInstallAfterPlayMode();
            return;
        }

        if (SceneManager.GetActiveScene().path != MainScenePath)
        {
            return;
        }

        GuidanceManager manager =
            Object.FindObjectOfType<GuidanceManager>(true);
        GuidanceDataChannelReceiver receiver =
            Object.FindObjectOfType<GuidanceDataChannelReceiver>(true);
        PlayerController controller =
            Object.FindObjectOfType<PlayerController>(true);
        Broadcast broadcast =
            Object.FindObjectOfType<Broadcast>(true);

        bool alreadyInstalled =
            manager != null &&
            receiver != null &&
            controller != null &&
            controller.GuidanceManager == manager &&
            broadcast != null &&
            broadcast.Streams.Contains(receiver);

        if (!alreadyInstalled)
        {
            Install();
        }
    }

    private static void RegisterInstallAfterPlayMode()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.delayCall += InstallIfMainSceneIsOpen;
    }

    private static void ConfigureReceiverSerialization(
        GuidanceDataChannelReceiver receiver)
    {
        SerializedObject serialized = new SerializedObject(receiver);
        SerializedProperty localProperty = serialized.FindProperty("local");
        SerializedProperty labelProperty = serialized.FindProperty("label");
        if (localProperty != null)
        {
            localProperty.boolValue = false;
        }
        if (labelProperty != null)
        {
            labelProperty.stringValue = "guidance";
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static Material GetOrCreateOverlayMaterial()
    {
        Material material =
            AssetDatabase.LoadAssetAtPath<Material>(GuidanceMaterialPath);
        if (material != null)
        {
            return material;
        }

        EnsureFolder(GuidanceFolder);
        Shader shader =
            Shader.Find("Hidden/StreamManager/GuidanceOverlay");
        if (shader == null)
        {
            Debug.LogError(
                "[Guidance Installer] GuidanceOverlay shader is missing.");
            return null;
        }

        material = new Material(shader)
        {
            name = "GuidanceOverlay",
            color = new Color(1f, 0.78f, 0.05f, 1f)
        };
        AssetDatabase.CreateAsset(material, GuidanceMaterialPath);
        return material;
    }

    private static void EnsureFolder(string path)
    {
        string[] segments = path.Split('/');
        string current = segments[0];
        for (int index = 1; index < segments.Length; index++)
        {
            string next = current + "/" + segments[index];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, segments[index]);
            }
            current = next;
        }
    }
}
