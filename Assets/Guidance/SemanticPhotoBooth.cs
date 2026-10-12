using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 为语义物体生成"参考照"：离屏相机对着物体拍摄一帧，输出 JPG 的 base64 data URL。
/// 供网页端在物体从未出现在实时流画面中时兜底显示（代替空白占位）。
/// 纯静态工具：会话级缓存 + TTL（到期后下次请求重拍，兼顾液体颜色等状态变化）。
/// </summary>
public static class SemanticPhotoBooth
{
    private const int PhotoWidth = 320;
    private const int PhotoHeight = 180;
    private const float PhotoTtlSeconds = 120f;
    private const float FieldOfView = 40f;
    private const float FrameFill = 1.12f;
    private const int JpegQuality = 78;
    private const float ViewTiltDegrees = 25f;

    // 水平环绕的四个观察方向（俯角 25°），取第一个未被遮挡的。
    private static readonly float[] ViewAngles = { 45f, 135f, 225f, 315f };
    private static readonly Color BackgroundColor =
        new Color(0.16f, 0.2f, 0.24f);

    private sealed class CachedPhoto
    {
        public string dataUrl;
        public double renderedAt;
    }

    private static readonly Dictionary<string, CachedPhoto> Cache =
        new Dictionary<string, CachedPhoto>();

    // 进入 Play 时清空缓存，避免跨会话拿到陈旧照片。
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Cache.Clear();
    }

    /// <summary>获取参考照 data URL（image/jpeg;base64）。失败返回 null。</summary>
    public static string GetPhotoDataUrl(SemanticObject target)
    {
        if (target == null || string.IsNullOrWhiteSpace(target.StableId))
        {
            return null;
        }

        string id = target.StableId.Trim();
        double now = Time.realtimeSinceStartupAsDouble;
        if (Cache.TryGetValue(id, out CachedPhoto cached) &&
            !string.IsNullOrEmpty(cached.dataUrl) &&
            now - cached.renderedAt < PhotoTtlSeconds)
        {
            return cached.dataUrl;
        }

        byte[] jpg = RenderPhoto(target);
        if (jpg == null || jpg.Length == 0)
        {
            return null;
        }

        string dataUrl = "data:image/jpeg;base64," +
                         System.Convert.ToBase64String(jpg);
        Cache[id] = new CachedPhoto { dataUrl = dataUrl, renderedAt = now };
        return dataUrl;
    }

    private static byte[] RenderPhoto(SemanticObject target)
    {
        GameObject go = target.gameObject;
        if (!go.activeInHierarchy || !TryComputeBounds(go, out Bounds bounds))
        {
            return null;
        }

        float radius = bounds.extents.magnitude;
        if (radius <= 0.0001f)
        {
            return null;
        }

        Vector3 center = bounds.center;
        float distance = Mathf.Max(
            0.15f,
            radius / Mathf.Sin(FieldOfView * 0.5f * Mathf.Deg2Rad) *
            FrameFill);
        Vector3 cameraPosition = PickViewPosition(center, distance, target);

        GameObject cameraObject = new GameObject("~SemanticPhotoBoothCamera");
        cameraObject.hideFlags = HideFlags.HideAndDontSave;
        Camera photoCamera = cameraObject.AddComponent<Camera>();
        photoCamera.enabled = false; // 仅手动 Render() 一次
        photoCamera.stereoTargetEye = StereoTargetEyeMask.None; // XR 下强制单目
        photoCamera.fieldOfView = FieldOfView;
        photoCamera.nearClipPlane = Mathf.Max(0.02f, distance - radius * 1.5f);
        photoCamera.farClipPlane = distance + radius * 4f;
        photoCamera.clearFlags = CameraClearFlags.SolidColor;
        photoCamera.backgroundColor = BackgroundColor;
        cameraObject.transform.SetPositionAndRotation(
            cameraPosition,
            Quaternion.LookRotation(center - cameraPosition, Vector3.up));

        RenderTexture renderTexture = RenderTexture.GetTemporary(
            PhotoWidth, PhotoHeight, 24,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture previousActive = RenderTexture.active;
        photoCamera.targetTexture = renderTexture;
        photoCamera.Render();

        RenderTexture.active = renderTexture;
        Texture2D texture = new Texture2D(
            PhotoWidth, PhotoHeight, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, PhotoWidth, PhotoHeight), 0, 0);
        texture.Apply(false, false);

        RenderTexture.active = previousActive;
        photoCamera.targetTexture = null;
        RenderTexture.ReleaseTemporary(renderTexture);
        Object.Destroy(cameraObject);

        byte[] jpg = texture.EncodeToJPG(JpegQuality);
        Object.Destroy(texture);
        return jpg;
    }

    private static Vector3 PickViewPosition(
        Vector3 center, float distance, SemanticObject target)
    {
        float tilt = ViewTiltDegrees * Mathf.Deg2Rad;
        float horizontal = Mathf.Cos(tilt);
        float vertical = Mathf.Sin(tilt);
        Vector3 fallback = center + Vector3.forward * distance;

        for (int i = 0; i < ViewAngles.Length; i++)
        {
            float angle = ViewAngles[i] * Mathf.Deg2Rad;
            Vector3 direction = new Vector3(
                Mathf.Cos(angle) * horizontal,
                vertical,
                Mathf.Sin(angle) * horizontal).normalized;
            Vector3 position = center + direction * distance;
            if (i == 0)
            {
                fallback = position;
            }
            if (!IsOccluded(position, center, target))
            {
                return position;
            }
        }

        // 四个方向都被遮挡时仍渲染第一个方向，保证有图可用。
        return fallback;
    }

    private static bool TryComputeBounds(GameObject go, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in go.GetComponentsInChildren<Renderer>())
        {
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }
            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }
        return hasBounds;
    }

    private static bool IsOccluded(
        Vector3 cameraPosition, Vector3 center, SemanticObject target)
    {
        Vector3 delta = center - cameraPosition;
        float distance = delta.magnitude;
        if (distance <= 0.0001f)
        {
            return false;
        }
        if (!Physics.Raycast(
                cameraPosition,
                delta / distance,
                out RaycastHit hit,
                distance * 1.05f,
                ~0,
                QueryTriggerInteraction.Ignore))
        {
            return false; // 无碰撞体或射线全空：视为可见
        }
        return !hit.transform.IsChildOf(target.transform);
    }
}
