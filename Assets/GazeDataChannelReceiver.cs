using System;
using System.Text;
using Unity.RenderStreaming;
using UnityEngine;

public class GazeDataChannelReceiver : DataChannelBase
{
    [Serializable]
    private class GazeMessage
    {
        public string type;
        public bool valid;
        public float x;
        public float y;
        public float videoX;
        public float videoY;
        public long timestamp_us;
        public double pc_time_unix;
    }

    [Header("Gaze Raycast")]
    [SerializeField] private Camera renderStreamingCamera;
    [SerializeField] private float maxDistance = 100f;
    [SerializeField] private LayerMask raycastLayers = ~0;

    [Header("Debug Visualization")]
    [SerializeField] private bool showDebugRay = true;
    [SerializeField] private bool showHitMarker = true;
    [SerializeField] private float markerSize = 0.2f;
    [SerializeField] private float markerSurfaceOffset = 0.03f;
    [SerializeField] private bool showRenderedRay = true;

    private GameObject _hitMarker;
    private LineRenderer _rayLine;
    private float _lastLogTime;
    private int _receivedCount;

    private void Reset()
    {
        local = false;
        label = "gaze";
    }

    private void Awake()
    {
        local = false;
        label = "gaze";
    }

    protected override void OnOpen(string connectionId)
    {
        base.OnOpen(connectionId);
        Debug.Log($"[Gaze] DataChannel opened. connection={connectionId} label={Label}");
    }

    protected override void OnClose(string connectionId)
    {
        base.OnClose(connectionId);
        HideMarker();
        HideRenderedRay();
        Debug.Log($"[Gaze] DataChannel closed. connection={connectionId}");
    }

    protected override void OnMessage(byte[] bytes)
    {
        _receivedCount++;
        string json = Encoding.UTF8.GetString(bytes);
        GazeMessage gaze;

        try
        {
            gaze = JsonUtility.FromJson<GazeMessage>(json);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[Gaze] Ignored malformed gaze JSON: {exception.Message}");
            return;
        }

        if (gaze == null || gaze.type != "gaze" || !gaze.valid)
        {
            HideMarker();
            HideRenderedRay();

            // Invalid packets still prove that the browser-to-Unity DataChannel
            // is alive; they just do not have usable coordinates for raycasting.
            if (Time.unscaledTime - _lastLogTime > 1f)
            {
                Debug.Log($"[Gaze] received={_receivedCount} invalid gaze packet.");
                _lastLogTime = Time.unscaledTime;
            }
            return;
        }

        ProcessGaze(gaze);
    }

    private void ProcessGaze(GazeMessage gaze)
    {
        if (renderStreamingCamera == null)
        {
            Debug.LogWarning("[Gaze] renderStreamingCamera is not assigned.");
            return;
        }

        // Browser sends Unity viewport coordinates: x grows left-to-right,
        // y grows bottom-to-top. That makes this ray match the streamed camera.
        Vector3 viewportPoint = new Vector3(Mathf.Clamp01(gaze.x), Mathf.Clamp01(gaze.y), 0f);
        Ray ray = renderStreamingCamera.ViewportPointToRay(viewportPoint);

        if (showDebugRay)
        {
            Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.green, 0.05f);
        }

        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, raycastLayers))
        {
            Vector3 markerPosition = hit.point + hit.normal * markerSurfaceOffset;
            ShowMarker(markerPosition);
            ShowRenderedRay(ray.origin, markerPosition);

            // Log at a low rate so gaze streaming does not flood the console.
            if (Time.unscaledTime - _lastLogTime > 0.25f)
            {
                Debug.Log($"[Gaze] received={_receivedCount} hit={hit.collider.name} viewport=({gaze.x:F3},{gaze.y:F3}) depth={hit.distance:F3}m");
                _lastLogTime = Time.unscaledTime;
            }
        }
        else
        {
            HideMarker();
            ShowRenderedRay(ray.origin, ray.origin + ray.direction * maxDistance);

            if (Time.unscaledTime - _lastLogTime > 0.5f)
            {
                Debug.Log($"[Gaze] received={_receivedCount} no hit viewport=({gaze.x:F3},{gaze.y:F3})");
                _lastLogTime = Time.unscaledTime;
            }
        }
    }

    private void ShowMarker(Vector3 position)
    {
        if (!showHitMarker)
        {
            return;
        }

        if (_hitMarker == null)
        {
            _hitMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _hitMarker.name = "Gaze Hit Marker";
            _hitMarker.transform.localScale = Vector3.one * markerSize;

            Collider markerCollider = _hitMarker.GetComponent<Collider>();
            if (markerCollider != null)
            {
                Destroy(markerCollider);
            }

            Renderer renderer = _hitMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.material = CreateGreenMaterial();
            }
        }

        _hitMarker.transform.position = position;
        _hitMarker.SetActive(true);
    }

    private void HideMarker()
    {
        if (_hitMarker != null)
        {
            _hitMarker.SetActive(false);
        }
    }

    private void ShowRenderedRay(Vector3 start, Vector3 end)
    {
        if (!showRenderedRay)
        {
            HideRenderedRay();
            return;
        }

        if (_rayLine == null)
        {
            GameObject lineObject = new GameObject("Gaze Rendered Ray");
            _rayLine = lineObject.AddComponent<LineRenderer>();
            _rayLine.material = CreateGreenMaterial();
            _rayLine.positionCount = 2;
            _rayLine.startWidth = 0.025f;
            _rayLine.endWidth = 0.025f;
            _rayLine.useWorldSpace = true;
        }

        _rayLine.SetPosition(0, start);
        _rayLine.SetPosition(1, end);
        _rayLine.gameObject.SetActive(true);
    }

    private void HideRenderedRay()
    {
        if (_rayLine != null)
        {
            _rayLine.gameObject.SetActive(false);
        }
    }

    private static Material CreateGreenMaterial()
    {
        Shader shader = Shader.Find("Unlit/Color");
        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        Material material = new Material(shader);
        material.color = Color.green;
        return material;
    }
}
