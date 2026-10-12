using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

[DisallowMultipleComponent]
[RequireComponent(typeof(XRGrabInteractable))]
[DefaultExecutionOrder(1000)]
public sealed class ExperimentGrabReturn : MonoBehaviour
{
    [SerializeField] private Transform returnAnchor;
    public void ConfigureReturnAnchor(Transform anchor) => returnAnchor = anchor;
    private XRGrabInteractable _grab;
    private Rigidbody _body;
    private Transform _homeParent;
    private Vector3 _homePosition;
    private Quaternion _homeRotation;
    private Vector3 _homeScale;
    private bool _initialized;
    private bool _pending;

    public void Initialize()
    {
        if (_initialized) return;
        _grab = GetComponent<XRGrabInteractable>();
        _body = GetComponent<Rigidbody>();
        _homeParent = transform.parent;
        _homePosition = transform.localPosition;
        _homeRotation = transform.localRotation;
        _homeScale = transform.localScale;
        _grab.throwOnDetach = false;
        _initialized = true;
    }

    private void Awake() => Initialize();
    private void OnEnable()
    {
        Initialize();
        _grab.selectEntered.AddListener(OnSelected);
        _grab.selectExited.AddListener(OnReleased);
    }
    private void OnDisable()
    {
        if (_grab == null) return;
        _grab.selectEntered.RemoveListener(OnSelected);
        _grab.selectExited.RemoveListener(OnReleased);
        _pending = false;
    }
    private void OnSelected(SelectEnterEventArgs args) => _pending = false;
    private void OnReleased(SelectExitEventArgs args)
    {
        // Programmatic cancellation mounts the funnel or resets the experiment.
        _pending = !args.isCanceled && !_grab.isSelected;
    }
    private void LateUpdate()
    {
        if (!_pending) return;
        _pending = false;
        if (_grab.isSelected || !_grab.enabled) return;
        // XRI finishes detaching in its Late phase, before this pose-only return.
        transform.SetParent(_homeParent, false);
        transform.localPosition = _homePosition;
        transform.localRotation = _homeRotation;
        transform.localScale = _homeScale;
        if (returnAnchor != null) transform.SetPositionAndRotation(returnAnchor.position, returnAnchor.rotation);
        if (_body == null) return;
        if (!_body.isKinematic) { _body.velocity = Vector3.zero; _body.angularVelocity = Vector3.zero; }
        _body.position = transform.position;
        _body.rotation = transform.rotation;
        _body.Sleep();
    }
}
