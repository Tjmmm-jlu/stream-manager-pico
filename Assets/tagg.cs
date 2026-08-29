using UnityEngine;

public class StreamCameraFollow : MonoBehaviour
{
    public Transform vrCamera;
    [SerializeField]
    [Tooltip("Keep this capture camera disabled until Render Streaming assigns a RenderTexture.")]
    private bool gateCameraByTargetTexture = true;

    private Camera _streamCamera;
    private Camera _vrCameraComponent;

    void Awake()
    {
        _streamCamera = GetComponent<Camera>();
        if (_streamCamera == null)
        {
            Debug.LogError("[StreamCamera] A Camera component is required.");
            enabled = false;
            return;
        }

        if (gateCameraByTargetTexture && _streamCamera.targetTexture == null)
            _streamCamera.enabled = false;
    }

    void Start()
    {
        if (vrCamera != null)
            _vrCameraComponent = vrCamera.GetComponent<Camera>();

        SyncFOV();
    }

    void LateUpdate()
    {
        if (_streamCamera == null)
            return;

        if (gateCameraByTargetTexture)
        {
            bool hasCaptureTarget = _streamCamera.targetTexture != null;
            _streamCamera.enabled = hasCaptureTarget;
            if (!hasCaptureTarget)
                return;
        }

        if (vrCamera == null) return;
        transform.SetPositionAndRotation(vrCamera.position, vrCamera.rotation);
        SyncFOV();
    }

    void SyncFOV()
    {
        if (_streamCamera != null && _vrCameraComponent != null)
            _streamCamera.fieldOfView = _vrCameraComponent.fieldOfView;
    }
}
