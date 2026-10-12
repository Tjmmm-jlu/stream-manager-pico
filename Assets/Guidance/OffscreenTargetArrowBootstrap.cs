using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 视野外目标箭头运行时自举（2026-10-12）：
/// 场景加载后若存在 GuidanceManager（实验场景），自动附加 OffscreenTargetArrow
/// 并解析操作者相机（头显渲染相机）。无需在场景中手动挂载组件。
/// 解析顺序：Main Camera -> 名称含 eye -> 名称含 hmd -> 名称含 stream -> Camera.main。
/// </summary>
public static class OffscreenTargetArrowBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RegisterSceneCallback()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        Configure(scene);
    }

    public static bool Configure(Scene scene)
    {
        GuidanceManager manager = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<GuidanceManager>(true))
            .FirstOrDefault();
        if (manager == null)
        {
            // 非实验场景：静默跳过。
            return false;
        }

        Camera camera = ResolveOperatorCamera();
        if (camera == null)
        {
            Debug.LogWarning(
                "[OffscreenArrow] 场景中有 GuidanceManager，但未找到操作者相机。");
            return false;
        }

        OffscreenTargetArrow arrow = manager.GetComponent<OffscreenTargetArrow>();
        if (arrow == null)
        {
            arrow = manager.gameObject.AddComponent<OffscreenTargetArrow>();
        }
        arrow.Configure(manager, camera);
        Debug.Log(
            $"[OffscreenArrow] 已安装：camera={camera.name} " +
            $"target={manager.CurrentTarget?.name ?? "none"}");
        return true;
    }

    private static Camera ResolveOperatorCamera()
    {
        Camera[] cameras = Object.FindObjectsOfType<Camera>(true);
        return cameras.FirstOrDefault(c =>
                   c != null && c.isActiveAndEnabled && c.name == "Main Camera")
               ?? cameras.FirstOrDefault(c =>
                   c != null && c.isActiveAndEnabled &&
                   c.name.IndexOf("eye", System.StringComparison.OrdinalIgnoreCase) >= 0)
               ?? cameras.FirstOrDefault(c =>
                   c != null && c.isActiveAndEnabled &&
                   c.name.IndexOf("hmd", System.StringComparison.OrdinalIgnoreCase) >= 0)
               ?? cameras.FirstOrDefault(c =>
                   c != null && c.isActiveAndEnabled &&
                   c.name.IndexOf("stream", System.StringComparison.OrdinalIgnoreCase) >= 0)
               ?? Camera.main;
    }
}
