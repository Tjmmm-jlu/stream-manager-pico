using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public enum GuidanceMode
{
    None,
    EllipseOutline,
    SpatialArrow,
    Highlight
}

public sealed class GuidanceManager : MonoBehaviour
{
    [Header("Common")]
    [SerializeField] private Camera guidanceCamera;
    [SerializeField] private Material overlayMaterial;
    [SerializeField] private GuidanceMode initialMode = GuidanceMode.EllipseOutline;
    [SerializeField] private Color guidanceColor = new Color(1f, 0.78f, 0.05f, 1f);

    [Header("Ellipse Outline")]
    [SerializeField, Range(32, 192)] private int ellipseSegments = 96;
    [SerializeField, Min(1f)] private float ellipseLineWidthPixels = 5f;
    [SerializeField, Range(0f, 0.5f)] private float ellipsePaddingRatio = 0.12f;
    [SerializeField, Min(0f)] private float ellipseMinimumPaddingPixels = 12f;

    [Header("Spatial Arrow")]
    [SerializeField, Min(0.2f)] private float arrowDistanceFromCamera = 0.8f;
    [SerializeField] private Vector2 arrowViewportOffset = new Vector2(0f, -0.18f);
    [SerializeField, Min(0.01f)] private float arrowScale = 0.22f;

    [Header("Highlight")]
    [SerializeField] private Color highlightColor = Color.yellow;

    private GameObject _currentTarget;
    private GuidanceMode _activeMode;
    private LineRenderer _ellipse;
    private Transform _arrow;
    private Material _runtimeMaterial;
    private GameObject _highlightedTarget;
    private readonly Dictionary<Renderer, MaterialPropertyBlock>
        _originalPropertyBlocks =
            new Dictionary<Renderer, MaterialPropertyBlock>();

    private static readonly int ColorProperty =
        Shader.PropertyToID("_Color");
    private static readonly int BaseColorProperty =
        Shader.PropertyToID("_BaseColor");

    public event Action<GameObject, GuidanceMode> TargetShown;
    public event Action TargetCleared;

    public Camera GuidanceCamera
    {
        get => guidanceCamera;
        set => guidanceCamera = value;
    }

    public Material OverlayMaterial
    {
        get => overlayMaterial;
        set => overlayMaterial = value;
    }

    public GuidanceMode ActiveMode => _activeMode;
    public GameObject CurrentTarget => _currentTarget;

    private void Awake()
    {
        _activeMode = initialMode;
        EnsureVisuals();
        RefreshVisibility();
    }

    private void LateUpdate()
    {
        if (_currentTarget == null || guidanceCamera == null)
        {
            HideAll();
            return;
        }

        if (_activeMode == GuidanceMode.Highlight)
        {
            SetEllipseVisible(false);
            SetArrowVisible(false);
            ApplyHighlight(_currentTarget);
            return;
        }

        ClearHighlight();

        if (!TryCalculateBounds(_currentTarget, out Bounds bounds))
        {
            HideAll();
            return;
        }

        switch (_activeMode)
        {
            case GuidanceMode.EllipseOutline:
                UpdateEllipse(bounds);
                SetArrowVisible(false);
                break;
            case GuidanceMode.SpatialArrow:
                SetEllipseVisible(false);
                UpdateArrow(bounds);
                break;
            default:
                HideAll();
                break;
        }
    }

    private void OnDisable()
    {
        HideAll();
    }

    private void OnDestroy()
    {
        if (_runtimeMaterial != null)
        {
            Destroy(_runtimeMaterial);
        }
    }

    public void SetMode(GuidanceMode mode)
    {
        _activeMode = mode;
        RefreshVisibility();
        Debug.Log($"[Guidance] Mode changed to {GetModeName(mode)}.");
    }

    public bool TrySetMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
        {
            return false;
        }

        switch (mode.Trim().ToLowerInvariant())
        {
            case "none":
            case "off":
                SetMode(GuidanceMode.None);
                return true;
            case "outline":
            case "ellipse":
            case "ellipse_outline":
                SetMode(GuidanceMode.EllipseOutline);
                return true;
            case "arrow":
            case "spatial_arrow":
                SetMode(GuidanceMode.SpatialArrow);
                return true;
            case "highlight":
            case "highlighting":
                SetMode(GuidanceMode.Highlight);
                return true;
            default:
                return false;
        }
    }

    public void ShowTarget(GameObject target)
    {
        if (target == null)
        {
            ClearTarget();
            return;
        }

        if (_currentTarget != target)
        {
            ClearHighlight();
            _currentTarget = target;
        }
        EnsureVisuals();
        RefreshVisibility();
        TargetShown?.Invoke(target, _activeMode);
        Debug.Log(
            $"[Guidance] Target={target.name} mode={GetModeName(_activeMode)} " +
            $"time={Time.realtimeSinceStartupAsDouble:F6}");
    }

    public void ClearTarget()
    {
        if (_currentTarget == null)
        {
            HideAll();
            return;
        }

        ClearHighlight();
        _currentTarget = null;
        HideAll();
        TargetCleared?.Invoke();
        Debug.Log(
            $"[Guidance] Target cleared at {Time.realtimeSinceStartupAsDouble:F6}.");
    }

    public static string GetModeName(GuidanceMode mode)
    {
        switch (mode)
        {
            case GuidanceMode.EllipseOutline:
                return "outline";
            case GuidanceMode.SpatialArrow:
                return "arrow";
            case GuidanceMode.Highlight:
                return "highlight";
            default:
                return "none";
        }
    }

    private void EnsureVisuals()
    {
        Material material = GetRuntimeMaterial();

        if (_ellipse == null)
        {
            GameObject ellipseObject = new GameObject("Guidance Ellipse");
            ellipseObject.transform.SetParent(transform, false);
            _ellipse = ellipseObject.AddComponent<LineRenderer>();
            _ellipse.useWorldSpace = true;
            _ellipse.loop = false;
            _ellipse.textureMode = LineTextureMode.Stretch;
            _ellipse.alignment = LineAlignment.View;
            _ellipse.numCornerVertices = 3;
            _ellipse.numCapVertices = 3;
            _ellipse.shadowCastingMode = ShadowCastingMode.Off;
            _ellipse.receiveShadows = false;
            _ellipse.sortingOrder = 32760;
            _ellipse.sharedMaterial = material;
            _ellipse.startColor = guidanceColor;
            _ellipse.endColor = guidanceColor;
        }

        if (_arrow == null)
        {
            GameObject arrowObject = new GameObject("Guidance Spatial Arrow");
            arrowObject.transform.SetParent(transform, false);
            MeshFilter filter = arrowObject.AddComponent<MeshFilter>();
            filter.sharedMesh = CreateArrowMesh();
            MeshRenderer renderer = arrowObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortingOrder = 32760;
            _arrow = arrowObject.transform;
        }
    }

    private Material GetRuntimeMaterial()
    {
        if (_runtimeMaterial != null)
        {
            return _runtimeMaterial;
        }

        Material source = overlayMaterial;
        if (source == null)
        {
            Shader shader = Shader.Find("Hidden/StreamManager/GuidanceOverlay");
            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            source = new Material(shader);
        }

        _runtimeMaterial = new Material(source)
        {
            name = "Guidance Overlay Runtime Material",
            color = guidanceColor
        };
        return _runtimeMaterial;
    }

    private void UpdateEllipse(Bounds bounds)
    {
        EnsureVisuals();

        Vector3 minViewport = new Vector3(float.PositiveInfinity, float.PositiveInfinity, 0f);
        Vector3 maxViewport = new Vector3(float.NegativeInfinity, float.NegativeInfinity, 0f);
        float nearestDepth = float.PositiveInfinity;
        bool hasPointInFront = false;

        foreach (Vector3 corner in GetBoundsCorners(bounds))
        {
            Vector3 viewport = guidanceCamera.WorldToViewportPoint(corner);
            if (viewport.z <= guidanceCamera.nearClipPlane)
            {
                continue;
            }

            hasPointInFront = true;
            minViewport.x = Mathf.Min(minViewport.x, viewport.x);
            minViewport.y = Mathf.Min(minViewport.y, viewport.y);
            maxViewport.x = Mathf.Max(maxViewport.x, viewport.x);
            maxViewport.y = Mathf.Max(maxViewport.y, viewport.y);
            nearestDepth = Mathf.Min(nearestDepth, viewport.z);
        }

        if (!hasPointInFront ||
            maxViewport.x < 0f || minViewport.x > 1f ||
            maxViewport.y < 0f || minViewport.y > 1f)
        {
            SetEllipseVisible(false);
            return;
        }

        float pixelWidth = Mathf.Max(1f, guidanceCamera.pixelWidth);
        float pixelHeight = Mathf.Max(1f, guidanceCamera.pixelHeight);
        Vector2 size = new Vector2(
            Mathf.Max(0.001f, maxViewport.x - minViewport.x),
            Mathf.Max(0.001f, maxViewport.y - minViewport.y));
        Vector2 minimumPadding = new Vector2(
            ellipseMinimumPaddingPixels / pixelWidth,
            ellipseMinimumPaddingPixels / pixelHeight);
        Vector2 padding = new Vector2(
            Mathf.Max(minimumPadding.x, size.x * ellipsePaddingRatio),
            Mathf.Max(minimumPadding.y, size.y * ellipsePaddingRatio));
        Vector2 center = new Vector2(
            (minViewport.x + maxViewport.x) * 0.5f,
            (minViewport.y + maxViewport.y) * 0.5f);
        Vector2 radius = size * 0.5f + padding;
        float depth = Mathf.Max(
            guidanceCamera.nearClipPlane + 0.03f,
            nearestDepth - 0.03f);

        int segmentCount = Mathf.Clamp(ellipseSegments, 32, 192);
        _ellipse.positionCount = segmentCount + 1;
        for (int index = 0; index <= segmentCount; index++)
        {
            float angle = index * Mathf.PI * 2f / segmentCount;
            Vector3 viewportPoint = new Vector3(
                center.x + Mathf.Cos(angle) * radius.x,
                center.y + Mathf.Sin(angle) * radius.y,
                depth);
            _ellipse.SetPosition(
                index, guidanceCamera.ViewportToWorldPoint(viewportPoint));
        }

        float worldViewportHeight =
            2f * depth * Mathf.Tan(guidanceCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float worldLineWidth =
            worldViewportHeight * ellipseLineWidthPixels / pixelHeight;
        _ellipse.startWidth = worldLineWidth;
        _ellipse.endWidth = worldLineWidth;
        SetEllipseVisible(true);
    }

    private void UpdateArrow(Bounds bounds)
    {
        EnsureVisuals();

        Vector3 cameraPosition = guidanceCamera.transform.position;
        Vector3 anchor =
            cameraPosition +
            guidanceCamera.transform.forward * arrowDistanceFromCamera +
            guidanceCamera.transform.right * arrowViewportOffset.x +
            guidanceCamera.transform.up * arrowViewportOffset.y;
        Vector3 direction = bounds.center - anchor;
        if (direction.sqrMagnitude < 0.0001f)
        {
            direction = guidanceCamera.transform.forward;
        }

        _arrow.position = anchor;
        _arrow.rotation = Quaternion.LookRotation(
            direction.normalized, guidanceCamera.transform.up);
        _arrow.localScale = Vector3.one * arrowScale;
        SetArrowVisible(true);
    }

    private void RefreshVisibility()
    {
        if (_currentTarget == null)
        {
            HideAll();
            return;
        }

        SetEllipseVisible(_activeMode == GuidanceMode.EllipseOutline);
        SetArrowVisible(_activeMode == GuidanceMode.SpatialArrow);
        if (_activeMode == GuidanceMode.Highlight)
        {
            ApplyHighlight(_currentTarget);
        }
        else
        {
            ClearHighlight();
        }
    }

    private void HideAll()
    {
        SetEllipseVisible(false);
        SetArrowVisible(false);
        ClearHighlight();
    }

    private void ApplyHighlight(GameObject target)
    {
        if (target == null || _highlightedTarget == target)
        {
            return;
        }

        ClearHighlight();
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer)
            {
                continue;
            }

            var originalBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(originalBlock);
            _originalPropertyBlocks[renderer] = originalBlock;

            var highlightBlock = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(highlightBlock);
            highlightBlock.SetColor(ColorProperty, highlightColor);
            highlightBlock.SetColor(BaseColorProperty, highlightColor);
            renderer.SetPropertyBlock(highlightBlock);
        }

        _highlightedTarget = target;
    }

    private void ClearHighlight()
    {
        foreach (KeyValuePair<Renderer, MaterialPropertyBlock> entry
                 in _originalPropertyBlocks)
        {
            if (entry.Key != null)
            {
                entry.Key.SetPropertyBlock(entry.Value);
            }
        }

        _originalPropertyBlocks.Clear();
        _highlightedTarget = null;
    }

    private void SetEllipseVisible(bool visible)
    {
        if (_ellipse != null)
        {
            _ellipse.gameObject.SetActive(visible);
        }
    }

    private void SetArrowVisible(bool visible)
    {
        if (_arrow != null)
        {
            _arrow.gameObject.SetActive(visible);
        }
    }

    private static bool TryCalculateBounds(GameObject target, out Bounds bounds)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        bool initialized = false;
        bounds = default;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null || renderer is LineRenderer)
            {
                continue;
            }

            if (!initialized)
            {
                bounds = renderer.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (initialized)
        {
            return true;
        }

        Collider[] colliders = target.GetComponentsInChildren<Collider>(true);
        foreach (Collider collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            if (!initialized)
            {
                bounds = collider.bounds;
                initialized = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return initialized;
    }

    private static IEnumerable<Vector3> GetBoundsCorners(Bounds bounds)
    {
        Vector3 min = bounds.min;
        Vector3 max = bounds.max;

        yield return new Vector3(min.x, min.y, min.z);
        yield return new Vector3(min.x, min.y, max.z);
        yield return new Vector3(min.x, max.y, min.z);
        yield return new Vector3(min.x, max.y, max.z);
        yield return new Vector3(max.x, min.y, min.z);
        yield return new Vector3(max.x, min.y, max.z);
        yield return new Vector3(max.x, max.y, min.z);
        yield return new Vector3(max.x, max.y, max.z);
    }

    private static Mesh CreateArrowMesh()
    {
        const float shaftHalfWidth = 0.16f;
        const float shaftEnd = 0.62f;
        const float headHalfWidth = 0.34f;
        const float headBase = 0.52f;
        const float tip = 1f;

        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        AddBox(
            vertices, triangles,
            new Vector3(-shaftHalfWidth, -shaftHalfWidth, 0f),
            new Vector3(shaftHalfWidth, shaftHalfWidth, shaftEnd));

        int baseIndex = vertices.Count;
        vertices.Add(new Vector3(-headHalfWidth, -headHalfWidth, headBase));
        vertices.Add(new Vector3(headHalfWidth, -headHalfWidth, headBase));
        vertices.Add(new Vector3(headHalfWidth, headHalfWidth, headBase));
        vertices.Add(new Vector3(-headHalfWidth, headHalfWidth, headBase));
        vertices.Add(new Vector3(0f, 0f, tip));

        AddTriangle(triangles, baseIndex + 0, baseIndex + 1, baseIndex + 4);
        AddTriangle(triangles, baseIndex + 1, baseIndex + 2, baseIndex + 4);
        AddTriangle(triangles, baseIndex + 2, baseIndex + 3, baseIndex + 4);
        AddTriangle(triangles, baseIndex + 3, baseIndex + 0, baseIndex + 4);
        AddQuad(
            triangles,
            baseIndex + 0, baseIndex + 3, baseIndex + 2, baseIndex + 1);

        Mesh mesh = new Mesh
        {
            name = "Guidance Spatial Arrow Mesh"
        };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static void AddBox(
        List<Vector3> vertices,
        List<int> triangles,
        Vector3 min,
        Vector3 max)
    {
        int start = vertices.Count;
        vertices.Add(new Vector3(min.x, min.y, min.z));
        vertices.Add(new Vector3(max.x, min.y, min.z));
        vertices.Add(new Vector3(max.x, max.y, min.z));
        vertices.Add(new Vector3(min.x, max.y, min.z));
        vertices.Add(new Vector3(min.x, min.y, max.z));
        vertices.Add(new Vector3(max.x, min.y, max.z));
        vertices.Add(new Vector3(max.x, max.y, max.z));
        vertices.Add(new Vector3(min.x, max.y, max.z));

        AddQuad(triangles, start + 0, start + 3, start + 2, start + 1);
        AddQuad(triangles, start + 4, start + 5, start + 6, start + 7);
        AddQuad(triangles, start + 0, start + 1, start + 5, start + 4);
        AddQuad(triangles, start + 3, start + 7, start + 6, start + 2);
        AddQuad(triangles, start + 0, start + 4, start + 7, start + 3);
        AddQuad(triangles, start + 1, start + 2, start + 6, start + 5);
    }

    private static void AddQuad(
        List<int> triangles, int a, int b, int c, int d)
    {
        AddTriangle(triangles, a, b, c);
        AddTriangle(triangles, a, c, d);
    }

    private static void AddTriangle(
        List<int> triangles, int a, int b, int c)
    {
        triangles.Add(a);
        triangles.Add(b);
        triangles.Add(c);
    }
}
