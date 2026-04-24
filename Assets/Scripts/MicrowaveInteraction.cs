using UnityEngine;
using System.Collections;

/// <summary>
/// Microwave with door open/close interaction and optional cooking cycle.
///
/// Controls (shown as HUD hint when looking at the microwave):
///   E          — open or close the door
///   M          — start / stop cooking (door must be closed)
///
/// Cooking plays a looping cooking sound and stops automatically after
/// cookingDuration seconds. The door CANNOT be opened while cooking.
/// </summary>
[RequireComponent(typeof(Collider))]
[RequireComponent(typeof(AudioSource))]
public class MicrowaveInteraction : MonoBehaviour
{
    [Header("Door")]
    [Tooltip("Pivot placed at the left hinge edge of the door (as seen from the front).")]
    public Transform doorPivot;
    [Tooltip("Axis in LOCAL pivot space around which the door swings. Y = side-hinge (real microwave). X = drop-down.")]
    public Vector3 openAxis = new Vector3(0f, 1f, 0f);
    [Tooltip("Degrees the door swings open. Negative = swings left (when facing the door from front).")]
    public float openAngle = -110f;
    public float animSpeed = 2.5f;

    [Header("Cooking")]
    [Tooltip("How long (seconds) a single cook cycle runs before auto-stopping.")]
    public float cookingDuration = 5f;

    [Header("Interaction")]
    public float interactDistance = 2.5f;

    [Header("Audio")]
    [Tooltip("Short click / creak played when the door opens.")]
    public AudioClip openSound;
    [Tooltip("Short click played when the door closes.")]
    public AudioClip closeSound;
    [Tooltip("Looping hum played during a cooking cycle.")]
    public AudioClip cookingSound;
    [Tooltip("Ding played when cooking finishes.")]
    public AudioClip doneSound;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isOpen      = false;
    private bool _isCooking   = false;
    private bool _isAnimating = false;
    private AudioSource  _audio;
    private Quaternion   _closedRot;
    private Quaternion   _openRot;
    private Coroutine    _cookRoutine;

    // ── Hint visibility ───────────────────────────────────────────────────────
    private bool _showHint = false;
    private GUIStyle _hintStyle;

    private void Awake()
    {
        _audio = GetComponent<AudioSource>();
        _audio.spatialBlend = 1f;
        _audio.playOnAwake  = false;
    }

    private void Start()
    {
        if (doorPivot == null)
        {
            Debug.LogError("[MicrowaveInteraction] doorPivot not assigned.", this);
            enabled = false;
            return;
        }

        _closedRot = doorPivot.localRotation;
        _openRot   = _closedRot * Quaternion.AngleAxis(openAngle, openAxis);
    }

    private void Update()
    {
        _showHint = false;

        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        bool looking = Physics.Raycast(ray, out RaycastHit hit, interactDistance)
                       && (hit.collider.transform.IsChildOf(transform) || hit.collider.gameObject == gameObject);

        if (!looking) return;
        _showHint = true;

        // ── Door toggle ────────────────────────────────────────────────────
        if (!_isAnimating && !_isCooking && Input.GetKeyDown(KeyCode.E))
            StartCoroutine(ToggleDoor());

        // ── Cook toggle (M key) ────────────────────────────────────────────
        if (!_isOpen && Input.GetKeyDown(KeyCode.M))
        {
            if (_isCooking)
                StopCooking(playDone: false);
            else
                StartCooking();
        }
    }

    // ── Door animation ─────────────────────────────────────────────────────────
    private IEnumerator ToggleDoor()
    {
        _isAnimating = true;

        // Play the appropriate door sound (short click, NOT the cooking hum)
        AudioClip clip = _isOpen ? closeSound : openSound;
        if (clip != null) _audio.PlayOneShot(clip);

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

    // ── Cooking cycle ──────────────────────────────────────────────────────────
    private void StartCooking()
    {
        _isCooking = true;

        if (cookingSound != null)
        {
            _audio.clip = cookingSound;
            _audio.loop = true;
            _audio.Play();
        }

        if (_cookRoutine != null)
            StopCoroutine(_cookRoutine);
        _cookRoutine = StartCoroutine(CookingTimer());
    }

    private void StopCooking(bool playDone)
    {
        if (_cookRoutine != null)
        {
            StopCoroutine(_cookRoutine);
            _cookRoutine = null;
        }

        _isCooking = false;
        _audio.loop = false;
        _audio.Stop();

        if (playDone && doneSound != null)
            _audio.PlayOneShot(doneSound);
    }

    private IEnumerator CookingTimer()
    {
        yield return new WaitForSeconds(cookingDuration);
        StopCooking(playDone: true);
    }

    // ── On-screen hint ─────────────────────────────────────────────────────────
    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        string status = _isCooking ? "Cooking…" : (_isOpen ? "Door Open" : "Door Closed");
        string hint   = $"[E] {(_isOpen ? "Close Door" : (_isCooking ? "—" : "Open Door"))}";
        string mHint  = _isOpen ? "" : $"\n[M] {(_isCooking ? "Stop Cooking" : "Start Cooking")}";

        string msg    = $"<b>Microwave</b>  [{status}]\n{hint}{mHint}";
        float w = 320f, h = 80f;
        float x = Screen.width * 0.5f - w * 0.5f;
        float y = Screen.height * 0.72f;
        GUI.Label(new Rect(x, y, w, h), msg, _hintStyle);
    }

    private static GUIStyle BuildHintStyle()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize  = 22;
        s.alignment = TextAnchor.MiddleCenter;
        s.normal.textColor  = Color.white;
        s.richText  = true;
        return s;
    }

    private static float EaseInOutQuad(float t)
        => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
}
