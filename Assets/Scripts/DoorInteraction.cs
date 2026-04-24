using UnityEngine;
using System.Collections;

/// <summary>
/// General-purpose hinged door. Press E while looking at the door to open/close it.
///
/// Controls (shown as HUD hint when looking at the door):
///   E — open or close the door
/// </summary>
[RequireComponent(typeof(Collider))]
public class DoorInteraction : MonoBehaviour
{
    [Header("Pivot & Rotation")]
    [Tooltip("The Transform that acts as the hinge pivot. The door panel must be a child of this pivot.")]
    public Transform doorPivot;
    [Tooltip("Degrees the door swings open around the pivot's local Y axis.")]
    public float openAngle = 90f;
    [Tooltip("Speed of the open/close animation.")]
    public float animSpeed = 2f;

    [Header("Interaction")]
    [Tooltip("Maximum distance from which the player can interact.")]
    public float interactDistance = 2.5f;

    [Header("Audio")]
    public AudioClip doorSound;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isOpen = false;
    private bool _isAnimating = false;
    private AudioSource _audio;
    private Quaternion _closedRot;
    private Quaternion _openRot;
    private bool _showHint = false;
    private GUIStyle _hintStyle;

    private void Start()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 1f;
        _audio.playOnAwake = false;

        if (doorPivot == null)
        {
            Debug.LogError("[DoorInteraction] doorPivot is not assigned.", this);
            enabled = false;
            return;
        }

        _closedRot = doorPivot.localRotation;
        _openRot   = _closedRot * Quaternion.AngleAxis(openAngle, Vector3.up);
    }

    private void Update()
    {
        _showHint = false;
        if (_isAnimating) return;

        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        bool looking = Physics.Raycast(ray, out RaycastHit hit, interactDistance)
                       && (hit.collider.transform.IsChildOf(transform)
                           || hit.collider.gameObject == gameObject);

        if (!looking) return;
        _showHint = true;

        if (Input.GetKeyDown(KeyCode.E))
            StartCoroutine(ToggleDoor());
    }

    private IEnumerator ToggleDoor()
    {
        _isAnimating = true;

        if (_audio != null && doorSound != null)
            _audio.PlayOneShot(doorSound);

        Quaternion from   = doorPivot.localRotation;
        Quaternion target = _isOpen ? _closedRot : _openRot;

        for (float t = 0f; t < 1f; )
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * animSpeed);
            doorPivot.localRotation = Quaternion.Slerp(from, target, EaseInOutQuad(t));
            yield return null;
        }

        doorPivot.localRotation = target;
        _isOpen      = !_isOpen;
        _isAnimating = false;
    }

    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        string action = _isOpen ? "Close Door" : "Open Door";
        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.72f, 360f, 56f),
            $"<b>Door</b>\n[E] {action}", _hintStyle);
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
        => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
}
