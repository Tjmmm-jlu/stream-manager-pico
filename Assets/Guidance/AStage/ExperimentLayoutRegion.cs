using UnityEngine;

/// <summary>
/// Defines the editable XZ region used by the experiment layout generator.
/// The optional BoxCollider is disabled by default so it cannot interfere with
/// ray-based object selection; the region is visualized with Gizmos instead.
/// </summary>
[DisallowMultipleComponent]
public sealed class ExperimentLayoutRegion : MonoBehaviour
{
    [Header("Placement surface")]
    [SerializeField] private Renderer placementSurface;
    [SerializeField] private Vector2 areaSize = new Vector2(1.6f, 0.9f);
    [Min(0f)] [SerializeField] private float edgePadding = 0.04f;

    [Header("Optional editor bounds")]
    [SerializeField] private BoxCollider boundsCollider;
    [SerializeField] private bool enableBoundsCollider;

    public Renderer PlacementSurface => placementSurface;
    public Vector2 AreaSize => new Vector2(
        Mathf.Max(0.1f, areaSize.x),
        Mathf.Max(0.1f, areaSize.y));
    public float EdgePadding => Mathf.Max(0f, edgePadding);
    public BoxCollider BoundsCollider => boundsCollider;

    public void Configure(Renderer surface, Vector2 size, float padding)
    {
        placementSurface = surface;
        areaSize = new Vector2(
            Mathf.Max(0.1f, size.x),
            Mathf.Max(0.1f, size.y));
        edgePadding = Mathf.Max(0f, padding);
        EnsureBoundsCollider();
        RefreshFromSurface();
    }

    public void RefreshFromSurface()
    {
        if (placementSurface != null)
        {
            Bounds surfaceBounds = placementSurface.bounds;
            transform.position = new Vector3(
                surfaceBounds.center.x,
                surfaceBounds.max.y,
                surfaceBounds.center.z);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            areaSize = new Vector2(
                Mathf.Max(0.1f, surfaceBounds.size.x),
                Mathf.Max(0.1f, surfaceBounds.size.z));
        }

        SyncBoundsCollider();
    }

    private void Reset()
    {
        EnsureBoundsCollider();
        SyncBoundsCollider();
    }

    private void OnValidate()
    {
        areaSize = AreaSize;
        edgePadding = EdgePadding;
        SyncBoundsCollider();
    }

    private void EnsureBoundsCollider()
    {
        if (boundsCollider == null)
        {
            boundsCollider = GetComponent<BoxCollider>();
        }

        if (boundsCollider == null)
        {
            boundsCollider = gameObject.AddComponent<BoxCollider>();
        }
    }

    private void SyncBoundsCollider()
    {
        if (boundsCollider == null)
        {
            return;
        }

        boundsCollider.isTrigger = true;
        boundsCollider.center = Vector3.zero;
        boundsCollider.size = new Vector3(AreaSize.x, 0.02f, AreaSize.y);
        boundsCollider.enabled = enableBoundsCollider;
    }

    private void OnDrawGizmosSelected()
    {
        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = Matrix4x4.TRS(
            transform.position,
            Quaternion.Euler(0f, transform.eulerAngles.y, 0f),
            Vector3.one);

        Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.85f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(AreaSize.x, 0.01f, AreaSize.y));

        float usableX = Mathf.Max(0f, AreaSize.x - 2f * EdgePadding);
        float usableZ = Mathf.Max(0f, AreaSize.y - 2f * EdgePadding);
        Gizmos.color = new Color(0.2f, 1f, 0.35f, 0.85f);
        Gizmos.DrawWireCube(Vector3.zero, new Vector3(usableX, 0.012f, usableZ));

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
