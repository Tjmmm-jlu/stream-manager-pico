using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 视野外目标方向箭头（2026-10-12）：
/// 当 GuidanceManager 的当前目标不在操作者视锥内时，在相机前方渲染方向箭头
/// 指示目标方位；目标进入视野后自动隐藏，由高亮接管。
/// 由 OffscreenTargetArrowBootstrap 在运行时创建并注入 GuidanceManager。
/// </summary>
public sealed class OffscreenTargetArrow : MonoBehaviour
{
    [Header("几何")]
    [Tooltip("箭头浮于相机前方的距离（米）。")]
    public float distance = 0.85f;

    [Tooltip("箭头贴边位置相对视锥边缘的收缩比例（0-1），越小越靠视野中心。")]
    public float edgeFraction = 0.8f;

    [Tooltip("显示阈值：目标偏离比超过该值时显示箭头（接近/超出视锥边缘）。")]
    public float showFraction = 0.95f;

    [Tooltip("隐藏阈值：目标回到视野内、偏离比低于该值时隐藏箭头（滞回防抖）。")]
    public float hideFraction = 0.7f;

    [Header("外观")]
    public Color arrowColor = new Color(0.2f, 0.95f, 1f, 0.95f);
    public float arrowScale = 1f;
    public float pulsePeriod = 1.1f;
    public float pulseAmplitude = 0.08f;

    private GuidanceManager _manager;
    private Camera _camera;
    private Transform _arrow;
    private MeshRenderer _arrowRenderer;
    private Material _material;
    private Mesh _mesh;
    private GameObject _target;
    private bool _visible;
    private bool _configured;

    public GameObject CurrentTarget => _target;

    public void Configure(GuidanceManager manager, Camera operatorCamera)
    {
        Detach();
        _manager = manager;
        _camera = operatorCamera;
        _configured = _camera != null;
        if (_manager != null)
        {
            _manager.TargetShown += HandleTargetShown;
            _manager.TargetCleared += HandleTargetCleared;
            _target = _manager.CurrentTarget;
        }
        if (!_configured)
        {
            Debug.LogWarning("[OffscreenArrow] 未找到操作者相机，箭头未启用。");
        }
    }

    private void OnDestroy()
    {
        Detach();
        DestroyVisual();
    }

    private void OnDisable()
    {
        SetArrowVisible(false);
    }

    private void Detach()
    {
        if (_manager != null)
        {
            _manager.TargetShown -= HandleTargetShown;
            _manager.TargetCleared -= HandleTargetCleared;
        }
        _manager = null;
        _target = null;
    }

    private void HandleTargetShown(GameObject target, GuidanceMode mode)
    {
        _target = target;
    }

    private void HandleTargetCleared()
    {
        _target = null;
        SetArrowVisible(false);
    }

    private void LateUpdate()
    {
        if (!_configured || _camera == null)
        {
            return;
        }
        if (_target == null)
        {
            SetArrowVisible(false);
            return;
        }

        Transform camTransform = _camera.transform;
        Vector3 local = camTransform.InverseTransformPoint(_target.transform.position);
        if (local.magnitude < 0.05f)
        {
            SetArrowVisible(false);
            return;
        }

        float halfVertical = _camera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float halfHorizontal = Mathf.Atan(
            Mathf.Tan(halfVertical) * Mathf.Max(0.2f, _camera.aspect));

        float yaw = Mathf.Atan2(local.x, local.z);
        float planar = Mathf.Sqrt(local.x * local.x + local.z * local.z);
        float pitch = Mathf.Atan2(local.y, Mathf.Max(0.0001f, planar));

        // 目标偏离视轴的比率：>1 表示位于视锥之外。
        float yawRatio = Mathf.Abs(yaw) / Mathf.Max(0.001f, halfHorizontal);
        float pitchRatio = Mathf.Abs(pitch) / Mathf.Max(0.001f, halfVertical);
        float ratio = Mathf.Max(yawRatio, pitchRatio);
        bool inFront = local.z > 0.02f;

        bool shouldShow = _visible
            ? !(inFront && ratio < hideFraction)
            : (!inFront || ratio > showFraction);

        if (!shouldShow)
        {
            SetArrowVisible(false);
            return;
        }

        EnsureVisual();
        if (_arrow == null)
        {
            return;
        }

        // 把目标方向压缩到视锥内侧边缘：保持方位角，贴边显示。
        float maxYaw = halfHorizontal * Mathf.Clamp01(edgeFraction);
        float maxPitch = halfVertical * Mathf.Clamp01(edgeFraction);
        float clampedYaw = Mathf.Clamp(yaw, -maxYaw, maxYaw);
        float clampedPitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);

        Vector3 direction = new Vector3(
            Mathf.Sin(clampedYaw) * Mathf.Cos(clampedPitch),
            Mathf.Sin(clampedPitch),
            Mathf.Cos(clampedYaw) * Mathf.Cos(clampedPitch));

        Vector3 position = camTransform.position +
            camTransform.rotation * (direction * distance);

        // 箭头平面面向相机；箭头尖（mesh 局部 +Y）指向"从视野中心向外"。
        Vector3 outward = new Vector3(direction.x, direction.y, 0f);
        if (outward.sqrMagnitude < 1e-6f)
        {
            outward = Vector3.up;
        }
        Vector3 tangent = outward - Vector3.Project(outward, direction);
        if (tangent.sqrMagnitude < 1e-6f)
        {
            tangent = Vector3.up;
        }
        Quaternion localRotation =
            Quaternion.LookRotation(direction, tangent.normalized);

        _arrow.SetPositionAndRotation(
            position, camTransform.rotation * localRotation);

        float pulse = 1f + pulseAmplitude * Mathf.Sin(
            Time.unscaledTime * Mathf.PI * 2f / Mathf.Max(0.1f, pulsePeriod));
        _arrow.localScale = Vector3.one * Mathf.Max(0.01f, arrowScale) * pulse;

        if (_material != null)
        {
            _material.color = arrowColor;
        }
        SetArrowVisible(true);
    }

    private void SetArrowVisible(bool visible)
    {
        _visible = visible;
        if (_arrowRenderer != null)
        {
            _arrowRenderer.enabled = visible;
        }
    }

    private void EnsureVisual()
    {
        if (_arrow != null)
        {
            return;
        }

        GameObject arrowObject = new GameObject("OffscreenTargetArrowVisual");
        arrowObject.transform.SetParent(transform, false);

        MeshFilter filter = arrowObject.AddComponent<MeshFilter>();
        _mesh = BuildArrowMesh();
        filter.sharedMesh = _mesh;

        _arrowRenderer = arrowObject.AddComponent<MeshRenderer>();
        _arrowRenderer.shadowCastingMode = ShadowCastingMode.Off;
        _arrowRenderer.receiveShadows = false;
        _arrowRenderer.lightProbeUsage = LightProbeUsage.Off;
        _arrowRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

        // 与 GazeDataChannelReceiver.CreateGreenMaterial 一致的回退链：
        // Unlit/Color -> Sprites/Default -> Standard。
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }
        _material = new Material(shader) { name = "OffscreenArrowMaterial" };
        _material.color = arrowColor;
        _arrowRenderer.sharedMaterial = _material;

        _arrow = arrowObject.transform;
    }

    private void DestroyVisual()
    {
        if (_material != null)
        {
            Destroy(_material);
            _material = null;
        }
        if (_mesh != null)
        {
            Destroy(_mesh);
            _mesh = null;
        }
        if (_arrow != null)
        {
            Destroy(_arrow.gameObject);
            _arrow = null;
            _arrowRenderer = null;
        }
    }

    private static Mesh BuildArrowMesh()
    {
        // 正反两个三角形，保证单面/双面材质下都可见；尖朝局部 +Y。
        const float tipY = 0.052f;
        const float baseY = -0.048f;
        const float halfWidth = 0.042f;

        Vector3 tip = new Vector3(0f, tipY, 0f);
        Vector3 left = new Vector3(-halfWidth, baseY, 0f);
        Vector3 right = new Vector3(halfWidth, baseY, 0f);

        Mesh mesh = new Mesh { name = "OffscreenArrowMesh" };
        mesh.vertices = new[] { tip, left, right, tip, left, right };
        mesh.triangles = new[] { 0, 1, 2, 3, 5, 4 };
        mesh.normals = new[]
        {
            Vector3.back, Vector3.back, Vector3.back,
            Vector3.forward, Vector3.forward, Vector3.forward
        };
        mesh.uv = new[]
        {
            new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f),
            new Vector2(0.5f, 1f), new Vector2(0f, 0f), new Vector2(1f, 0f)
        };
        mesh.RecalculateBounds();
        return mesh;
    }
}
