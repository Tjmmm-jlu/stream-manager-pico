using Unity.RenderStreaming;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Selection")]
    [SerializeField] private GuidanceManager guidanceManager;
    [SerializeField, Min(0)] private int selectionAncestorLevels = 1;
    [SerializeField, Min(0.1f)] private float maxSelectionDistance = 100f;
    [SerializeField] private LayerMask selectionLayers = ~0;

    [Header("Render Streaming")]
    public Camera renderStreamingCamera;
    public int streamWidth = 1280;
    public int streamHeight = 720;
    public VideoStreamSender videoStreamSender;

    public GameObject SelectedObject { get; private set; }

    public GuidanceManager GuidanceManager
    {
        get => guidanceManager;
        set => guidanceManager = value;
    }

    private void Awake()
    {
        if (guidanceManager == null)
        {
            guidanceManager = GetComponent<GuidanceManager>();
        }
    }

    public void Look(InputAction.CallbackContext value)
    {
    }

    public void Zoom(InputAction.CallbackContext value)
    {
    }

    public void Move(InputAction.CallbackContext value)
    {
    }

    public void Rotate(InputAction.CallbackContext value)
    {
    }

    public void Deselect(InputAction.CallbackContext value)
    {
        if (!value.performed)
        {
            return;
        }

        SelectedObject = null;
        guidanceManager?.ClearTarget();
    }

    public void Click(InputAction.CallbackContext value)
    {
        if (!value.performed)
        {
            return;
        }

        if (renderStreamingCamera == null)
        {
            Debug.LogError("[Selection] renderStreamingCamera is not assigned.");
            return;
        }

        if (Mouse.current == null)
        {
            Debug.LogWarning("[Selection] No remote mouse device is available.");
            return;
        }

        Vector2 mousePosition = Mouse.current.position.ReadValue();
        GetInputResolution(out int actualWidth, out int actualHeight);

        float viewportX = mousePosition.x / actualWidth;
        float viewportY = 1f - mousePosition.y / actualHeight;
        Ray ray = renderStreamingCamera.ViewportPointToRay(
            new Vector3(viewportX, viewportY, 0f));
        Debug.DrawRay(
            ray.origin, ray.direction * maxSelectionDistance, Color.red, 2f);

        if (!Physics.Raycast(
                ray,
                out RaycastHit hit,
                maxSelectionDistance,
                selectionLayers,
                QueryTriggerInteraction.Ignore))
        {
            Debug.Log(
                $"[Selection] No collider hit at viewport " +
                $"({viewportX:F3}, {viewportY:F3}).");
            return;
        }

        SelectedObject = ResolveSelectionTarget(hit.collider);
        if (SelectedObject == null)
        {
            Debug.LogWarning(
                $"[Selection] Could not resolve a target from {hit.collider.name}.");
            return;
        }

        if (guidanceManager == null)
        {
            guidanceManager = GetComponent<GuidanceManager>();
        }

        if (guidanceManager == null)
        {
            Debug.LogError("[Selection] GuidanceManager is not assigned.");
            return;
        }

        guidanceManager.ShowTarget(SelectedObject);
        Debug.Log(
            $"[Selection] target={SelectedObject.name} " +
            $"viewport=({viewportX:F3}, {viewportY:F3}) " +
            $"resolution={actualWidth}x{actualHeight}");
    }

    private void GetInputResolution(out int width, out int height)
    {
        width = Mathf.Max(1, streamWidth);
        height = Mathf.Max(1, streamHeight);

        if (videoStreamSender == null)
        {
            return;
        }

        float scale = Mathf.Max(0.0001f, videoStreamSender.scaleResolutionDown);
        width = Mathf.Max(1, Mathf.RoundToInt(videoStreamSender.width / scale));
        height = Mathf.Max(1, Mathf.RoundToInt(videoStreamSender.height / scale));
    }

    private GameObject ResolveSelectionTarget(Collider hitCollider)
    {
        SemanticObject semanticObject =
            hitCollider.GetComponentInParent<SemanticObject>();
        if (semanticObject != null && semanticObject.Selectable)
        {
            return semanticObject.gameObject;
        }

        if (hitCollider.attachedRigidbody != null)
        {
            return hitCollider.attachedRigidbody.gameObject;
        }

        Transform target = hitCollider.transform;
        for (int level = 0;
             level < selectionAncestorLevels && target.parent != null;
             level++)
        {
            target = target.parent;
        }

        return target.gameObject;
    }
}
