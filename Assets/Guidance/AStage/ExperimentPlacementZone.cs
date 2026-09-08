using System;
using UnityEngine;

/// <summary>
/// Stores the semantic object accepted by one experiment placement zone.
/// Runtime interaction and sequence advancement are handled separately.
/// </summary>
[DisallowMultipleComponent]
public sealed class ExperimentPlacementZone : MonoBehaviour
{
    [Header("Placement mapping")]
    [SerializeField] private string zoneId;
    [SerializeField] private string acceptedObjectId;

    public string ZoneId => zoneId;
    public string AcceptedObjectId => acceptedObjectId;

    public bool Accepts(string stableId)
    {
        return !string.IsNullOrWhiteSpace(stableId) &&
               string.Equals(
                   acceptedObjectId,
                   stableId.Trim(),
                   StringComparison.OrdinalIgnoreCase);
    }

    public void Configure(string id, string objectId)
    {
        zoneId = NormalizeId(id);
        acceptedObjectId = NormalizeId(objectId);
    }

    private void OnValidate()
    {
        zoneId = NormalizeId(zoneId);
        acceptedObjectId = NormalizeId(acceptedObjectId);
    }

    private static string NormalizeId(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().ToLowerInvariant().Replace(' ', '_');
    }
}
