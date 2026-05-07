using UnityEngine;

/// <summary>
/// Pick up any Pickable-tagged Rigidbody with F.
/// When dropped inside a trigger with a PlacementZone component, the item snaps to the
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
            Vector3 targetPos = holdPosition.position;
            Quaternion targetRot = holdPosition.rotation;

            // Optional per-item offsets (useful for phones/books so they sit nicely in view)
            HeldItemOffsets offsets = _heldObject.GetComponent<HeldItemOffsets>();
            if (offsets != null && offsets.enabled)
            {
                targetPos = holdPosition.TransformPoint(offsets.localPositionOffset);
                Quaternion baseRot = holdPosition.rotation;
                if (offsets.faceCameraWhileHeld && Camera.main != null)
                {
                    Vector3 desiredForward = -Camera.main.transform.forward;
                    Vector3 desiredUp = Camera.main.transform.up;

                    Vector3 localF = offsets.faceCameraLocalForwardAxis.sqrMagnitude < 0.0001f
                        ? Vector3.forward
                        : offsets.faceCameraLocalForwardAxis.normalized;
                    Vector3 localU = offsets.faceCameraLocalUpAxis.sqrMagnitude < 0.0001f
                        ? Vector3.up
                        : offsets.faceCameraLocalUpAxis.normalized;

                    // Rotate so (itemLocalForward,itemLocalUp) maps to (desiredForward,desiredUp).
                    Quaternion localBasis = Quaternion.LookRotation(localF, localU);
                    Quaternion desiredBasis = Quaternion.LookRotation(desiredForward, desiredUp);
                    baseRot = desiredBasis * Quaternion.Inverse(localBasis);
                }

                targetRot = baseRot * Quaternion.Euler(offsets.localEulerOffset);
            }

            _heldObject.transform.position = Vector3.Lerp(
                _heldObject.transform.position, targetPos, Time.deltaTime * 15f);
            _heldObject.transform.rotation = targetRot;

            if (Input.GetKeyDown(pickupKey))
                DropObject();

            return;
        }

        // ── Hover hint ────────────────────────────────────────────────────
        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        // Use a small spherecast so slight collider/model offsets still feel accurate.
        if (Physics.SphereCast(ray, 0.12f, out RaycastHit hit, pickupDistance))
        {
            GameObject pickable = ResolvePickable(hit.collider);
            if (pickable != null)
            {
                _showPickHint = true;
                if (Input.GetKeyDown(pickupKey))
                    TryPickup(pickable);
            }
        }
    }

    private static GameObject ResolvePickable(Collider col)
    {
        if (col == null) return null;

        // Raycasts often hit a child collider; allow picking the parent object
        // as long as SOME ancestor is tagged Pickable.
        Transform t = col.transform;
        while (t != null)
        {
            if (t.CompareTag("Pickable"))
                return t.gameObject;
            t = t.parent;
        }
        return null;
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
            var placed = zone.GetPlacedItem();
            if (placed == null) continue;
            if (placed == target || target.transform.IsChildOf(placed.transform))
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
                _heldRb.useGravity       = true;
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
