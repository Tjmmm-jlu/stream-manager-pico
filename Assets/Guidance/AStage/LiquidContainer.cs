using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
public sealed class LiquidContainer : MonoBehaviour
{
    [SerializeField] private float capacityMl = 250f;
    [SerializeField] private float initialVolumeMl;
    [SerializeField] private Vector3 bottomCenter;
    [SerializeField] private float innerRadius = 0.05f;
    [SerializeField] private float innerHeight = 0.15f;
    [SerializeField, Range(0f, 1f)] private float bottomRadiusRatio = 1f;
    [SerializeField] private MeshFilter liquidMesh;
    [SerializeField] private MeshRenderer liquidRenderer;
    [SerializeField] private Color waterColor = new Color(0.3f, 0.73f, 0.83f, 0.65f);
    private float _volume;
    private bool _initialized;
    private Mesh _mesh;
    private MaterialPropertyBlock _properties;
    private readonly List<Vector3> _vertices = new List<Vector3>(256);
    private readonly List<int> _triangles = new List<int>(512);
    private readonly List<Vector3> _cut = new List<Vector3>(64);
    private readonly List<Vector3> _polygon = new List<Vector3>(40);
    private Vector3 _normal;
    private float _level;
    private Quaternion _lastRotation;
    private float _lastVolume = -1f;
    private Color _color;
    private const int Sides = 32;
    private static readonly Vector2[] Samples = CreateSamples();
    private readonly Vector3[] _quad = new Vector3[4];
    private readonly Vector3[] _bottomRing = new Vector3[Sides];
    private readonly Vector3[] _topRing = new Vector3[Sides];

    private static Vector2[] CreateSamples()
    {
        var samples = new Vector2[128];
        for (int ring = 0; ring < 8; ring++)
        for (int angle = 0; angle < 16; angle++)
        {
            float r = Mathf.Sqrt((ring + 0.5f) / 8f), a = (angle + 0.5f) * Mathf.PI / 8f;
            samples[ring * 16 + angle] = new Vector2(r * Mathf.Cos(a), r * Mathf.Sin(a));
        }
        return samples;
    }

    public float VolumeMl { get { Initialize(); return _volume; } }
    public float CapacityMl => capacityMl;
    public float InitialVolumeMl => initialVolumeMl;
    public float Height => innerHeight;
    public float Radius => innerRadius;
    public Vector3 Bottom => bottomCenter;
    public Vector3 Mouth => transform.TransformPoint(bottomCenter + Vector3.up * innerHeight);
    public float WorldRadius => transform.TransformVector(Vector3.right * innerRadius).magnitude;
    public XRGrabInteractable Grab => GetComponent<XRGrabInteractable>();
    public bool Upright => Vector3.Dot(transform.up, Vector3.up) >= 0.94f;
    public float SurfaceWorldY { get { RefreshVisual(); return transform.TransformPoint(bottomCenter).y + _level; } }

    public void Configure(float capacity, float initial, Vector3 bottom, float radius, float height,
        MeshFilter mesh, MeshRenderer renderer, float baseRadiusRatio = 1f)
    {
        capacityMl = capacity;
        initialVolumeMl = initial;
        bottomCenter = bottom;
        innerRadius = radius;
        innerHeight = height;
        bottomRadiusRatio = Mathf.Clamp01(baseRadiusRatio);
        liquidMesh = mesh;
        liquidRenderer = renderer;
        ResetLiquid();
    }

    private void Awake() => Initialize();
    private void LateUpdate() => RefreshVisual();
    private void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        _volume = Mathf.Clamp(initialVolumeMl, 0f, capacityMl);
        _color = waterColor;
    }

    public float Add(float amount)
    {
        Initialize();
        float accepted = Mathf.Clamp(amount, 0f, capacityMl - _volume);
        _volume += accepted;
        return accepted;
    }

    public float Remove(float amount)
    {
        Initialize();
        float removed = Mathf.Clamp(amount, 0f, _volume);
        _volume -= removed;
        return removed;
    }

    public void SetVolume(float amount) { Initialize(); _volume = Mathf.Clamp(amount, 0f, capacityMl); }
    public void ResetLiquid() { _initialized = false; Initialize(); _lastVolume = -1f; RefreshVisual(); }
    public void SetDissolvedFraction(float fraction)
    {
        _color = Color.Lerp(waterColor, new Color(0.05f, 0.3f, 0.82f, 0.85f), Mathf.Clamp01(fraction));
        UpdateColor();
    }

    public Vector3 LowestLip()
    {
        Vector3 localDown = transform.InverseTransformDirection(Vector3.down);
        Vector3 radial = new Vector3(localDown.x, 0f, localDown.z).normalized;
        return transform.TransformPoint(bottomCenter + Vector3.up * innerHeight + radial * innerRadius);
    }

    public bool ContainsLiquidPoint(Vector3 worldPoint)
    {
        if (VolumeMl < 0.01f) return false;
        Vector3 p = transform.InverseTransformPoint(worldPoint) - bottomCenter;
        float radius = innerRadius * Mathf.Lerp(bottomRadiusRatio, 1f, p.y / innerHeight);
        return p.y >= 0f && p.y <= innerHeight && p.x * p.x + p.z * p.z < radius * radius &&
            worldPoint.y < SurfaceWorldY - 0.002f;
    }

    public bool TryReceive(Vector3 streamOrigin, out Vector3 hit)
    {
        hit = default;
        if (!Upright || !gameObject.activeInHierarchy) return false;
        var plane = new Plane(transform.up, Mouth);
        if (!plane.Raycast(new Ray(streamOrigin, Vector3.down), out float distance) || distance > 0.45f) return false;
        hit = streamOrigin + Vector3.down * distance;
        Vector3 local = transform.InverseTransformPoint(hit) - bottomCenter;
        return new Vector2(local.x, local.z).magnitude <= innerRadius * 0.94f;
    }

    public void RefreshVisual()
    {
        Initialize();
        if (liquidMesh == null || liquidRenderer == null) return;
        liquidRenderer.enabled = _volume > 0.001f;
        if (!liquidRenderer.enabled) return;
        if (Mathf.Abs(_lastVolume - _volume) < 0.001f && Quaternion.Angle(_lastRotation, transform.rotation) < 0.1f) return;
        _lastVolume = _volume;
        _lastRotation = transform.rotation;
        _normal = new Vector3(transform.TransformVector(Vector3.right).y,
            transform.TransformVector(Vector3.up).y, transform.TransformVector(Vector3.forward).y);
        float radial = new Vector2(_normal.x, _normal.z).magnitude * innerRadius;
        float low = Mathf.Min(0f, _normal.y * innerHeight) - radial;
        float high = Mathf.Max(0f, _normal.y * innerHeight) + radial;
        // Integrate equal-area columns to keep the fill fraction stable while the vessel tilts.
        for (int iteration = 0; iteration < 18; iteration++)
        {
            float middle = (low + high) * 0.5f;
            float fraction = 0f;
            float totalHeight = 0f;
            foreach (var sample in Samples)
            {
                // A tapered bowl has shorter liquid columns near its upper rim.
                float startY = bottomRadiusRatio >= 0.9999f ? 0f :
                    Mathf.Clamp01((sample.magnitude - bottomRadiusRatio) / (1f - bottomRadiusRatio)) * innerHeight;
                float height = innerHeight - startY;
                float baseY = innerRadius * (_normal.x * sample.x + _normal.z * sample.y) + _normal.y * startY;
                float aY = Mathf.Min(baseY, baseY + _normal.y * height);
                float bY = Mathf.Max(baseY, baseY + _normal.y * height);
                fraction += height * (bY - aY < 0.000001f ? (middle >= aY ? 1f : 0f) : Mathf.Clamp01((middle - aY) / (bY - aY)));
                totalHeight += height;
            }
            if (fraction / totalHeight < _volume / capacityMl) low = middle; else high = middle;
        }
        _level = (low + high) * 0.5f;
        _vertices.Clear(); _triangles.Clear(); _cut.Clear();
        for (int i = 0; i < Sides; i++)
        {
            _quad[0] = Ring(i, 0f); _quad[1] = Ring(i, innerHeight);
            _quad[2] = Ring(i + 1, innerHeight); _quad[3] = Ring(i + 1, 0f);
            ClipFace(_quad);
            _bottomRing[i] = _quad[0]; _topRing[Sides - i - 1] = _quad[1];
        }
        ClipFace(_bottomRing); ClipFace(_topRing);
        if (_cut.Count > 2)
        {
            Vector3 center = Vector3.zero;
            foreach (var p in _cut) center += p;
            center /= _cut.Count;
            Vector3 normal = _normal.normalized;
            Vector3 u = Vector3.Cross(normal, Mathf.Abs(normal.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            Vector3 v = Vector3.Cross(normal, u);
            _cut.Sort((a, b) => Mathf.Atan2(Vector3.Dot(a - center, v), Vector3.Dot(a - center, u))
                .CompareTo(Mathf.Atan2(Vector3.Dot(b - center, v), Vector3.Dot(b - center, u))));
            AddFace(_cut);
        }
        if (_mesh == null) { _mesh = new Mesh { name = "Runtime Liquid Surface" }; _mesh.MarkDynamic(); }
        _mesh.Clear();
        _mesh.SetVertices(_vertices); _mesh.SetTriangles(_triangles, 0); _mesh.RecalculateNormals(); _mesh.RecalculateBounds();
        liquidMesh.sharedMesh = _mesh;
        UpdateColor();
    }

    private Vector3 Ring(int index, float y)
    {
        float angle = index * Mathf.PI * 2f / Sides;
        float radius = innerRadius * Mathf.Lerp(bottomRadiusRatio, 1f, y / innerHeight);
        return new Vector3(Mathf.Cos(angle) * radius, y, Mathf.Sin(angle) * radius);
    }

    private void ClipFace(Vector3[] points)
    {
        _polygon.Clear();
        for (int i = 0; i < points.Length; i++)
        {
            Vector3 a = points[i], b = points[(i + 1) % points.Length];
            float da = Vector3.Dot(_normal, a) - _level, db = Vector3.Dot(_normal, b) - _level;
            if (da <= 0f) _polygon.Add(a);
            if ((da < 0f) != (db < 0f))
            {
                Vector3 cut = Vector3.Lerp(a, b, da / (da - db));
                _polygon.Add(cut);
                if (!_cut.Exists(p => (p - cut).sqrMagnitude < 1e-12f)) _cut.Add(cut);
            }
        }
        AddFace(_polygon);
    }

    private void AddFace(List<Vector3> points)
    {
        int start = _vertices.Count;
        foreach (var p in points) _vertices.Add(bottomCenter + p);
        for (int i = 1; i < points.Count - 1; i++) { _triangles.Add(start); _triangles.Add(start + i); _triangles.Add(start + i + 1); }
    }

    private void UpdateColor()
    {
        if (liquidRenderer == null) return;
        if (_properties == null) _properties = new MaterialPropertyBlock();
        _properties.SetColor("_Color", _color);
        liquidRenderer.SetPropertyBlock(_properties);
    }

    private void OnDestroy()
    {
        if (_mesh == null) return;
        if (Application.isPlaying) Destroy(_mesh); else DestroyImmediate(_mesh);
    }
}
