using UnityEngine;

public class StreamCameraFollow : MonoBehaviour
{
    public Transform vrCamera;
    private Camera _streamCamera;
    private Camera _vrCameraComponent;

    void Start()
    {
        _streamCamera = GetComponent<Camera>();
        if (vrCamera != null)
            _vrCameraComponent = vrCamera.GetComponent<Camera>();

        SyncFOV();
    }

    void LateUpdate()
    {
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
