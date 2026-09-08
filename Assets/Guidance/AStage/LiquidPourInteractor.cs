using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
public sealed class LiquidPourInteractor : MonoBehaviour
{
    [SerializeField] private LineRenderer stream;
    [SerializeField] private float maxFlowMlPerSecond = 40f;
    public float LastReceivedMl { get; private set; }
    public float LastSpilledMl { get; private set; }
    public bool IsPouring { get; private set; }

    public void Configure(LineRenderer visual, float flowMlPerSecond = 40f)
    {
        stream = visual;
        maxFlowMlPerSecond = flowMlPerSecond;
    }

    public static bool HeldByHand(XRGrabInteractable item)
    {
        if (item == null || !item.isActiveAndEnabled || !item.isSelected) return false;
        foreach (var selecting in item.interactorsSelecting)
            if (selecting is XRBaseInteractor hand && hand.handedness != InteractorHandedness.None) return true;
        return false;
    }

    public static bool OppositeHands(XRGrabInteractable a, XRGrabInteractable b)
    {
        if (a == null || b == null || !a.isActiveAndEnabled || !b.isActiveAndEnabled || !a.isSelected || !b.isSelected) return false;
        foreach (var first in a.interactorsSelecting)
        foreach (var second in b.interactorsSelecting)
            if (first is XRBaseInteractor left && second is XRBaseInteractor right && left != right &&
                left.handedness != InteractorHandedness.None && right.handedness != InteractorHandedness.None &&
                left.handedness != right.handedness) return true;
        return false;
    }

    public void Stop()
    {
        IsPouring = false;
        LastReceivedMl = LastSpilledMl = 0f;
        if (stream != null) stream.enabled = false;
    }

    public void Tick(LiquidContainer source, LiquidContainer destination, float elapsed)
        => TickIntoOpening(source, destination, null, 0f, elapsed);

    public void TickIntoOpening(LiquidContainer source, LiquidContainer destination,
        Transform opening, float radius, float elapsed)
    {
        Stop();
        if (!OppositeHands(source.Grab, destination.Grab) || source.VolumeMl <= 0.001f) return;
        float tilt = Vector3.Angle(source.transform.up, Vector3.up);
        Vector3 lip = source.LowestLip();
        if (tilt < 35f || source.SurfaceWorldY < lip.y - 0.004f) return;
        float removed = source.Remove(maxFlowMlPerSecond * Mathf.InverseLerp(35f, 100f, tilt) * Mathf.Clamp(elapsed, 0f, 0.1f));
        if (removed <= 0f) return;
        IsPouring = true;
        Vector3 end = lip + Vector3.down * 0.3f;
        Vector3 hit;
        bool receives = opening == null ? destination.TryReceive(lip, out hit) :
            TryReceiveOpening(destination, opening, radius, lip, out hit);
        if (receives)
        {
            end = hit;
            // Ignore source colliders; any other physical obstruction above the receiver catches the stream.
            bool blocked = false;
            foreach (var obstruction in Physics.RaycastAll(lip + Vector3.down * 0.001f, Vector3.down,
                         Mathf.Max(0f, lip.y - hit.y - 0.003f), ~0, QueryTriggerInteraction.Ignore))
                if (!obstruction.collider.transform.IsChildOf(source.transform) && !obstruction.collider.transform.IsChildOf(destination.transform))
                    blocked = true;
            if (!blocked) LastReceivedMl = destination.Add(removed);
        }
        LastSpilledMl = removed - LastReceivedMl;
        if (stream != null)
        {
            stream.enabled = true;
            stream.SetPosition(0, lip);
            stream.SetPosition(1, end);
        }
    }

    private static bool TryReceiveOpening(LiquidContainer destination, Transform opening, float radius,
        Vector3 lip, out Vector3 hit)
    {
        hit = default;
        if (!destination.Upright || !opening.gameObject.activeInHierarchy) return false;
        var plane = new Plane(opening.up, opening.position);
        if (!plane.Raycast(new Ray(lip, Vector3.down), out float distance) || distance > 0.45f) return false;
        hit = lip + Vector3.down * distance;
        return Vector3.Distance(hit, opening.position) <= radius;
    }

    private void OnDisable() => Stop();
}
