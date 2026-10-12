using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

[DisallowMultipleComponent]
public sealed class LiquidPourInteractor : MonoBehaviour
{
    [SerializeField] private LineRenderer stream;
    [SerializeField] private float maxFlowMlPerSecond = 40f;
    [Header("Assisted Filtration")]
    [SerializeField, Min(0f)] private float guideTipHorizontalTolerance = 0.08f;
    [SerializeField, Min(0f)] private float guideLipTolerance = 0.1f;
    public float LastReceivedMl { get; private set; }
    public float LastSpilledMl { get; private set; }
    public bool IsPouring { get; private set; }
    public bool LastGuideValid { get; private set; }

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
        LastGuideValid = false;
        if (stream != null) stream.enabled = false;
    }

    public void Tick(LiquidContainer source, LiquidContainer destination, float elapsed)
        => TickIntoOpening(source, destination, null, 0f, elapsed);

    public void TickIntoOpening(LiquidContainer source, LiquidContainer destination,
        Transform opening, float radius, float elapsed)
    {
        Stop();
        if (!OppositeHands(source.Grab, destination.Grab) || source.VolumeMl <= 0.001f) return;
        float removed = BeginPour(source, elapsed, out Vector3 lip);
        if (removed <= 0f) return;
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
            stream.positionCount = 2;
            stream.enabled = true;
            stream.SetPosition(0, lip);
            stream.SetPosition(1, end);
        }
    }

    public void TickGuidedIntoOpening(LiquidContainer source, LiquidContainer destination,
        XRGrabInteractable rod, Transform rodTip, Transform opening, Transform funnel,
        Transform outlet, bool assemblyReady, float elapsed)
    {
        Stop();
        if (!assemblyReady || rodTip == null || opening == null || outlet == null ||
            !OppositeHands(source.Grab, rod) || source.VolumeMl <= 0.001f) return;
        Vector3 lip = source.LowestLip();
        Vector3 tip = rodTip.position;
        // The existing stirring rod is centered at its origin, with symmetric end points.
        Vector3 top = rod.transform.TransformPoint(-rodTip.localPosition);
        if ((top - opening.position).sqrMagnitude < (tip - opening.position).sqrMagnitude)
        { Vector3 other = tip; tip = top; top = other; }
        Vector3 shaft = top - tip;
        float along = Mathf.Clamp01(Vector3.Dot(lip - tip, shaft) / Mathf.Max(shaft.sqrMagnitude, 0.000001f));
        Vector3 contact = tip + shaft * along;
        Vector3 inletOffset = tip - opening.position;
        LastGuideValid = Vector3.ProjectOnPlane(inletOffset, opening.up).magnitude <= guideTipHorizontalTolerance &&
            Vector3.Dot(inletOffset, opening.up) >= -0.06f && Vector3.Dot(inletOffset, opening.up) <= 0.1f &&
            Vector3.Distance(lip, contact) <= guideLipTolerance && lip.y >= tip.y + 0.01f;
        Vector3 localOutlet = destination.transform.InverseTransformPoint(outlet.position) - destination.Bottom;
        bool outletInside = destination.Upright && localOutlet.y > 0f && localOutlet.y < destination.Height + 0.02f &&
            new Vector2(localOutlet.x, localOutlet.z).magnitude < destination.Radius;
        // Allow pose adjustment without consuming the finite solution supply.
        if (!LastGuideValid || !outletInside || Blocked(lip, contact) || Blocked(contact, tip))
        { LastGuideValid = false; return; }
        float removed = BeginPour(source, elapsed, out lip);
        if (removed <= 0f) return;
        LastReceivedMl = destination.Add(removed);
        LastSpilledMl = removed - LastReceivedMl;
        if (stream != null)
        {
            stream.positionCount = 3; stream.enabled = true;
            stream.SetPosition(0, lip);
            stream.SetPosition(1, contact);
            stream.SetPosition(2, tip);
        }

        bool Blocked(Vector3 start, Vector3 end)
        {
            Vector3 delta = end - start;
            if (delta.magnitude < 0.003f) return false;
            foreach (var hit in Physics.RaycastAll(start, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(source.transform) && !hit.transform.IsChildOf(destination.transform) &&
                    !hit.transform.IsChildOf(rod.transform) && !hit.transform.IsChildOf(funnel) &&
                    !IsHoldingRig(hit.transform, source.Grab) && !IsHoldingRig(hit.transform, rod)) return true;
            return false;
        }
    }

    private static bool IsHoldingRig(Transform obstacle, XRGrabInteractable item)
    {
        foreach (var selecting in item.interactorsSelecting)
        {
            if (!(selecting is XRBaseInteractor hand)) continue;
            var origin = hand.GetComponentInParent<XROrigin>();
            if (origin != null && obstacle.IsChildOf(origin.transform)) return true;
        }
        return false;
    }

    private float BeginPour(LiquidContainer source, float elapsed, out Vector3 lip)
    {
        float tilt = Vector3.Angle(source.transform.up, Vector3.up);
        lip = source.LowestLip();
        if (tilt < 35f || source.SurfaceWorldY < lip.y - 0.004f) return 0f;
        float removed = source.Remove(maxFlowMlPerSecond * Mathf.InverseLerp(35f, 100f, tilt) * Mathf.Clamp(elapsed, 0f, 0.1f));
        IsPouring = removed > 0f;
        return removed;
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
