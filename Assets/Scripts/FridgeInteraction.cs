using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Handles the French-door fridge: top two doors swing open on their hinges,
/// bottom freezer drawer slides out. Each section has its own interaction
/// collider so E only activates the part the player looks at.
///
/// Controls (shown as HUD hint when looking at the fridge):
///   E — open / close the door or drawer being looked at
///   F — while holding an item and close to the open fridge, place it on a shelf
///       (item stays inside when door closes, hidden while door is shut)
/// </summary>
[DefaultExecutionOrder(-10)]   // run before PickupAndPlace so we can consume F first
public class FridgeInteraction : MonoBehaviour
{
    public enum InteractionMode { SwingDoor, Drawer }

    [Header("Mode")]
    public InteractionMode mode = InteractionMode.SwingDoor;

    [Header("Swing Door (top section)")]
    [Tooltip("Pivot for the left door.")]
    public Transform leftDoorPivot;
    [Tooltip("Pivot for the right door.")]
    public Transform rightDoorPivot;
    [Tooltip("Degrees each door swings. Left swings +Y, right swings -Y in local space.")]
    public float swingAngle = 85f;

    [Header("Drawer (bottom section)")]
    [Tooltip("The drawer mesh Transform that slides out.")]
    public Transform drawerTransform;
    [Tooltip("How far the drawer slides out (local Z units).")]
    public float drawerSlideDistance = 0.55f;

    [Header("Placement Zones")]
    [Tooltip("Assign the PlacementZone GameObjects that belong to THIS section " +
             "(top doors OR drawer). If left empty, the script auto-discovers zones " +
             "that are children of the fridge root whose name contains the section keyword.")]
    public PlacementZone[] assignedZones = new PlacementZone[0];

    [Header("Shared")]
    public float animSpeed = 2.5f;
    public float interactDistance = 2.5f;
    public AudioClip openSound;
    public AudioClip closeSound;

    // ── State ─────────────────────────────────────────────────────────────────

    // Each FridgeInteraction component tracks its OWN open state independently,
    // so opening the top doors does NOT affect the bottom drawer and vice versa.
    private bool _isOpen = false;
    private bool _isAnimating = false;
    private AudioSource _audio;
    private bool _showHint = false;
    private GUIStyle _hintStyle;

    /// <summary>
    /// Set to true this frame by FridgeInteraction when it consumes [F].
    /// PickupAndPlace reads this to skip its own F handling.
    /// Cleared at end of each FridgeInteraction.Update().
    /// </summary>
    public static bool FKeyConsumedThisFrame = false;

    // Door saved rotations
    private Quaternion _leftClosed, _leftOpen;
    private Quaternion _rightClosed, _rightOpen;

    // Drawer saved positions
    private Vector3 _drawerClosed, _drawerOpen;

    // Items stored inside the fridge (hidden when door is closed)
    private readonly List<GameObject> _storedItems = new List<GameObject>();

    // Reference to player PickupAndPlace
    private PickupAndPlace _pickup;

    private void Start()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 1f;
        _audio.playOnAwake = false;

        _pickup = FindAnyObjectByType<PickupAndPlace>();

        if (mode == InteractionMode.SwingDoor)
        {
            if (leftDoorPivot != null)
            {
                _leftClosed = leftDoorPivot.localRotation;
                _leftOpen   = Quaternion.Euler(0f, swingAngle, 0f);
            }
            if (rightDoorPivot != null)
            {
                _rightClosed = rightDoorPivot.localRotation;
                _rightOpen   = Quaternion.Euler(0f, -swingAngle, 0f);
            }
        }
        else
        {
            if (drawerTransform != null)
            {
                _drawerClosed = drawerTransform.localPosition;
                _drawerOpen   = _drawerClosed + new Vector3(0f, 0f, drawerSlideDistance);
            }
        }
    }

    private void Update()
    {
        _showHint = false;

        // Reset the static F-consumed flag once per frame (only the first component does it)
        // We piggyback on LateUpdate ordering — a simpler approach: clear at start of Update
        // since all FridgeInteraction instances share one Update pass.
        FKeyConsumedThisFrame = false;

        if (_isAnimating) return;

        float dist = Vector3.Distance(Camera.main.transform.position, transform.position);
        bool inRange = dist <= interactDistance;
        if (!inRange) return;

        // Raycast must hit THIS component's collider specifically.
        // This prevents E on the top door from also toggling the bottom drawer.
        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        Collider myCollider = GetComponent<Collider>();
        bool lookingAtMe = false;

        if (Physics.Raycast(ray, out RaycastHit hit, interactDistance + 1f))
        {
            lookingAtMe = (myCollider != null && hit.collider == myCollider)
                          || hit.collider.transform.IsChildOf(transform)
                          || hit.collider.gameObject == gameObject;
        }

        // Show hint only when directly looking at this section
        if (!lookingAtMe) return;
        _showHint = true;

        // E: open / close this section only
        if (Input.GetKeyDown(KeyCode.E))
        {
            PlaySound();
            if (mode == InteractionMode.SwingDoor)
                StartCoroutine(AnimateSwingDoors());
            else
                StartCoroutine(AnimateDrawer());
        }

        // F: place held item inside — only when this section is open
        if (_isOpen && _pickup != null && _pickup.GetHeldObject() != null
            && Input.GetKeyDown(KeyCode.F))
        {
            TryPlaceItemInFridge();
            FKeyConsumedThisFrame = true;
        }
    }

    /// <summary>
    /// Snaps the player's currently held item to the nearest empty PlacementZone
    /// belonging to this section, parents it to the fridge root, and registers it
    /// for hide-on-close / show-on-open toggling.
    /// Zone discovery: uses <see cref="assignedZones"/> when populated; otherwise
    /// searches all PlacementZones that are children of the fridge root Transform
    /// (the parent of this interaction trigger), which works regardless of whether
    /// the zones are children of this component or siblings under the fridge root.
    /// Falls back to the drawer transform centre when no zone is available (drawer mode).
    /// </summary>
    private void TryPlaceItemInFridge()
    {
        if (_pickup == null) return;

        GameObject held = _pickup.GetHeldObject();
        if (held == null) return;

        // Resolve which zones belong to this section.
        // Priority: explicitly assigned zones → auto-discover from fridge root.
        PlacementZone[] candidateZones;
        if (assignedZones != null && assignedZones.Length > 0)
        {
            candidateZones = assignedZones;
        }
        else
        {
            // The fridge root is this object's parent (FridgeTopDoorZone / FridgeDrawerZone
            // are direct children of the Fridge root GameObject).
            Transform fridgeRoot = transform.parent != null ? transform.parent : transform;
            candidateZones = fridgeRoot.GetComponentsInChildren<PlacementZone>();
        }

        // Find the nearest empty zone among the candidates
        PlacementZone bestZone = null;
        float bestDist = float.MaxValue;
        Vector3 camPos = Camera.main.transform.position;

        foreach (var zone in candidateZones)
        {
            if (zone == null || zone.HasItem) continue;
            float d = Vector3.Distance(zone.transform.position, camPos);
            if (d < bestDist) { bestDist = d; bestZone = zone; }
        }

        _pickup.ForceRelease();

        if (bestZone != null)
        {
            bestZone.PlaceItem(held);
        }
        else if (mode == InteractionMode.Drawer && drawerTransform != null)
        {
            // No zone: snap directly to drawer centre
            held.transform.position = drawerTransform.position;
            held.transform.rotation = Quaternion.identity;
            Rigidbody rb = held.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = true; rb.detectCollisions = false; }
        }
        else
        {
            // No valid placement target — return item to physics
            Rigidbody rb = held.GetComponent<Rigidbody>();
            if (rb != null) { rb.isKinematic = false; rb.detectCollisions = true; }
            return;
        }

        // Parent to fridge root so it moves with the fridge and is NOT accidentally
        // unparented when the interaction trigger itself is used as parent.
        Transform fridgeRootParent = transform.parent != null ? transform.parent : transform;
        held.transform.SetParent(fridgeRootParent, worldPositionStays: true);

        // Ensure item is always visible right after placement (door is open)
        SetAllRenderers(held, true);
        SetAllColliders(held, true);

        // Register so we can hide/show on door/drawer close/open
        if (!_storedItems.Contains(held))
            _storedItems.Add(held);
    }

    private IEnumerator AnimateSwingDoors()
    {
        _isAnimating = true;

        Quaternion leftTarget  = _isOpen ? _leftClosed  : _leftOpen;
        Quaternion rightTarget = _isOpen ? _rightClosed : _rightOpen;

        Quaternion leftStart  = leftDoorPivot  != null ? leftDoorPivot.localRotation  : Quaternion.identity;
        Quaternion rightStart = rightDoorPivot != null ? rightDoorPivot.localRotation : Quaternion.identity;

        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * animSpeed);
            float ease = EaseInOutQuad(t);
            if (leftDoorPivot  != null) leftDoorPivot.localRotation  = Quaternion.Slerp(leftStart,  leftTarget,  ease);
            if (rightDoorPivot != null) rightDoorPivot.localRotation = Quaternion.Slerp(rightStart, rightTarget, ease);
            yield return null;
        }

        if (leftDoorPivot  != null) leftDoorPivot.localRotation  = leftTarget;
        if (rightDoorPivot != null) rightDoorPivot.localRotation = rightTarget;

        _isOpen = !_isOpen;

        // Items should be VISIBLE when the door is open, HIDDEN when closed.
        // We only toggle renderers — never hide them right after placement.
        bool shouldBeVisible = _isOpen;
        foreach (var item in _storedItems)
        {
            if (item == null) continue;
            SetAllRenderers(item, shouldBeVisible);
            SetAllColliders(item, shouldBeVisible);
        }

        _isAnimating = false;
    }

    private IEnumerator AnimateDrawer()
    {
        _isAnimating = true;

        Vector3 target = _isOpen ? _drawerClosed : _drawerOpen;
        Vector3 start  = drawerTransform.localPosition;

        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * animSpeed);
            drawerTransform.localPosition = Vector3.Lerp(start, target, EaseInOutQuad(t));
            yield return null;
        }

        drawerTransform.localPosition = target;
        _isOpen = !_isOpen;

        // Items should be VISIBLE when the drawer is open, HIDDEN when closed.
        bool shouldBeVisible = _isOpen;
        foreach (var item in _storedItems)
        {
            if (item == null) continue;
            SetAllRenderers(item, shouldBeVisible);
            SetAllColliders(item, shouldBeVisible);
        }

        _isAnimating = false;
    }

    private static void SetAllRenderers(GameObject go, bool visible)
    {
        foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            r.enabled = visible;
    }

    private static void SetAllColliders(GameObject go, bool active)
    {
        foreach (var c in go.GetComponentsInChildren<Collider>(true))
            c.enabled = active;
    }

    /// <summary>
    /// Called by PickupAndPlace when the player picks an item back out of the fridge.
    /// Removes the item from the stored-items list and unparents it from the fridge root
    /// so it is no longer hidden/shown with the door state.
    /// </summary>
    public void ReleaseStoredItem(GameObject item)
    {
        _storedItems.Remove(item);
        if (item != null && item.transform.parent != null)
        {
            Transform fridgeRoot = transform.parent != null ? transform.parent : transform;
            if (item.transform.IsChildOf(fridgeRoot))
                item.transform.SetParent(null, worldPositionStays: true);
        }
    }

    private void PlaySound()
    {
        AudioClip clip = _isOpen ? closeSound : openSound;
        if (clip == null) clip = openSound ?? closeSound;
        if (_audio != null && clip != null)
            _audio.PlayOneShot(clip);
    }

    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        string section = mode == InteractionMode.SwingDoor ? "Fridge Doors" : "Freezer Drawer";
        string action  = _isOpen ? "Close" : "Open";
        string fHint   = _isOpen
            ? "\n<size=13>[F] Place held item on shelf</size>"
            : "";

        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.72f, 360f, 80f),
            $"<b>{section}</b>\n[E] {action}{fHint}",
            _hintStyle);
    }

    private static GUIStyle BuildHintStyle()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize  = 22;
        s.alignment = TextAnchor.MiddleCenter;
        s.normal.textColor = Color.white;
        s.richText  = true;
        return s;
    }

    private static float EaseInOutQuad(float t)
    {
        return t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
    }
}
