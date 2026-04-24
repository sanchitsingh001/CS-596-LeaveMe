using UnityEngine;

/// <summary>
/// Attach to any trigger collider (e.g. inside the fridge) to create a snap-to shelf.
/// When PickupAndPlace drops an item while the HoldPosition overlaps this zone,
/// the item is snapped to this zone's centre and locked kinematic.
///
/// Press F while looking at a placed item to pick it back up.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PlacementZone : MonoBehaviour
{
    [Tooltip("Name label shown on the HUD when looking at the zone.")]
    public string zoneName = "Shelf";

    private GameObject _placedItem;

    private void Awake()
    {
        // Ensure the collider is a trigger
        GetComponent<Collider>().isTrigger = true;
    }

    /// <summary>Called by PickupAndPlace when the player releases an item inside this zone.</summary>
    public void PlaceItem(GameObject item)
    {
        // Remove any previously placed item (make it fall)
        if (_placedItem != null)
            EjectPlacedItem();

        _placedItem = item;

        // Snap to zone centre
        item.transform.position = transform.position;
        item.transform.rotation = Quaternion.identity;

        // Lock in place
        Rigidbody rb = item.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic      = true;
            rb.detectCollisions = false;
        }

        Debug.Log($"[PlacementZone] '{item.name}' placed in '{zoneName}'.");
    }

    /// <summary>Remove the currently placed item (called by PickupAndPlace on re-pickup).</summary>
    public void EjectPlacedItem()
    {
        if (_placedItem == null) return;

        Rigidbody rb = _placedItem.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.detectCollisions = true;
            rb.isKinematic      = false;
        }

        _placedItem = null;
    }

    /// <summary>True if an item is already placed in this zone.</summary>
    public bool HasItem => _placedItem != null;

    /// <returns>The currently placed item, or null.</returns>
    public GameObject GetPlacedItem() => _placedItem;
}
