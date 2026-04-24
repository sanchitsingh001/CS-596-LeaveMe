using UnityEngine;

/// <summary>
/// Pick up any Pickable-tagged Rigidbody with F.
/// When dropped inside a trigger zone tagged "PlacementZone", the item snaps to the
/// zone centre, becomes kinematic, and stays put — simulating placing it on a shelf.
/// Press F while looking at a placed item to pick it back up.
///
/// Controls:
///   F — pick up the object you are looking at (must be tagged Pickable)
///   F (while holding) — release / place the object
/// </summary>
[DefaultExecutionOrder(10)]   // runs after FridgeInteraction so F-consumption is respected
public class PickupAndPlace : MonoBehaviour
{
    public float pickupDistance = 2.5f;
    public Transform holdPosition;
    public KeyCode pickupKey = KeyCode.F;

    private GameObject _heldObject = null;
    private Rigidbody  _heldRb;

    // ── Legacy public aliases so existing scene references keep working ──────
    private GameObject heldObject  { get => _heldObject; set => _heldObject = value; }
    private Rigidbody  heldRb      { get => _heldRb;     set => _heldRb     = value; }

    private GUIStyle _hintStyle;
    private bool     _showPickHint = false;

    private void Update()
    {
        _showPickHint = false;

        // If FridgeInteraction already consumed F this frame, skip entirely
        if (FridgeInteraction.FKeyConsumedThisFrame) return;

        if (_heldObject != null)
        {
            // Smoothly carry the object to the hold position
            _heldObject.transform.position = Vector3.Lerp(
                _heldObject.transform.position, holdPosition.position, Time.deltaTime * 15f);
            _heldObject.transform.rotation = holdPosition.rotation;

            if (Input.GetKeyDown(pickupKey))
                DropObject();

            return;
        }

        // ── Hover hint ────────────────────────────────────────────────────
        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        if (Physics.Raycast(ray, out RaycastHit hit, pickupDistance))
        {
            if (hit.collider.CompareTag("Pickable"))
            {
                _showPickHint = true;
                if (Input.GetKeyDown(pickupKey))
                    TryPickup(hit.collider.gameObject);
            }
        }
    }

    /// <summary>Returns the currently held object, or null.</summary>
    public GameObject GetHeldObject() => _heldObject;

    /// <summary>
    /// Releases the held object without dropping physics — used by FridgeInteraction
    /// to take ownership of the object before snapping it to a shelf.
    /// </summary>
    public void ForceRelease()
    {
        _heldObject = null;
        _heldRb     = null;
    }

    private void TryPickup(GameObject target)
    {
        // If this item is placed in a zone, eject it from the zone first
        PlacementZone[] zones = FindObjectsByType<PlacementZone>(FindObjectsSortMode.None);
        foreach (var zone in zones)
        {
            if (zone.GetPlacedItem() == target)
            {
                zone.EjectPlacedItem();
                break;
            }
        }

        // If this item is stored inside a fridge section, release it from that section
        // so it is no longer hidden when the door closes while the player holds it.
        FridgeInteraction[] fridgeSections = FindObjectsByType<FridgeInteraction>(FindObjectsSortMode.None);
        foreach (var section in fridgeSections)
            section.ReleaseStoredItem(target);

        // Unparent from any scene hierarchy parent (e.g. fridge root) before holding
        target.transform.SetParent(null, worldPositionStays: true);

        _heldObject = target;
        _heldRb     = target.GetComponent<Rigidbody>();

        if (_heldRb != null)
        {
            _heldRb.isKinematic       = true;
            _heldRb.detectCollisions  = false;
        }
    }

    private void DropObject()
    {
        if (_heldObject == null) return;

        // Check whether we're dropping inside a PlacementZone trigger
        Collider[] overlaps = Physics.OverlapBox(
            holdPosition.position,
            Vector3.one * 0.15f,
            Quaternion.identity,
            ~0,
            QueryTriggerInteraction.Collide);

        PlacementZone zone = null;
        foreach (var col in overlaps)
        {
            zone = col.GetComponent<PlacementZone>();
            if (zone != null) break;
        }

        if (zone != null)
        {
            // Snap to zone and lock in place
            zone.PlaceItem(_heldObject);
        }
        else
        {
            // Regular drop — restore physics
            if (_heldRb != null)
            {
                _heldRb.detectCollisions = true;
                _heldRb.isKinematic      = false;
            }
        }

        _heldObject = null;
        _heldRb     = null;
    }

    private void OnGUI()
    {
        if (!_showPickHint) return;
        if (_hintStyle == null)
        {
            _hintStyle = new GUIStyle(GUI.skin.label);
            _hintStyle.fontSize  = 22;
            _hintStyle.alignment = TextAnchor.MiddleCenter;
            _hintStyle.normal.textColor = Color.white;
            _hintStyle.richText  = true;
        }

        string msg = _heldObject != null
            ? "[F] Place object"
            : "[F] Pick up";

        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.78f, 360f, 44f),
            msg, _hintStyle);
    }
}
