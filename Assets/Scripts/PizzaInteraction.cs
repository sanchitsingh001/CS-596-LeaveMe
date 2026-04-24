using UnityEngine;
using System.Collections;

/// <summary>
/// Interactive pizza box: player can open/close the box and "eat" a slice.
///
/// Controls (shown when looking at the pizza):
///   E — open / close the pizza box
///   Q — eat a slice (while box is open; plays a chomp sound)
///
/// Visually: the pizza GameObject itself scales down slightly each time a slice
/// is "eaten", simulating slices disappearing. Maximum 8 slices.
/// </summary>
[RequireComponent(typeof(Collider))]
public class PizzaInteraction : MonoBehaviour
{
    [Header("Box Animation")]
    [Tooltip("The lid / top part of the pizza box that rotates open.")]
    public Transform boxLid;
    [Tooltip("Degrees the lid rotates to open (around local X).")]
    public float lidOpenAngle = -100f;
    public float animSpeed = 3f;

    [Header("Pizza")]
    [Tooltip("The pizza mesh that represents all slices.")]
    public Transform pizzaMesh;
    [Tooltip("Total slices in the pizza.")]
    public int totalSlices = 8;

    [Header("Interaction")]
    public float interactDistance = 2.5f;

    [Header("Audio")]
    public AudioClip boxOpenSound;
    public AudioClip boxCloseSound;
    public AudioClip eatSound;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool _isOpen     = false;
    private bool _isAnimating = false;
    private int  _slicesLeft;

    private Quaternion _lidClosed;
    private Quaternion _lidOpen;
    private Vector3    _pizzaFullScale;

    private AudioSource _audio;
    private bool        _showHint = false;
    private GUIStyle    _hintStyle;

    private void Start()
    {
        _slicesLeft     = totalSlices;
        _pizzaFullScale = pizzaMesh != null ? pizzaMesh.localScale : Vector3.one;

        if (boxLid != null)
        {
            _lidClosed = boxLid.localRotation;
            _lidOpen   = _lidClosed * Quaternion.Euler(lidOpenAngle, 0f, 0f);
        }

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 1f;
        _audio.playOnAwake  = false;
    }

    private void Update()
    {
        _showHint = false;

        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        if (!Physics.Raycast(ray, out RaycastHit hit, interactDistance)) return;
        if (!hit.collider.transform.IsChildOf(transform) && hit.collider.gameObject != gameObject) return;

        _showHint = true;

        // E — open / close box
        if (!_isAnimating && Input.GetKeyDown(KeyCode.E))
        {
            if (boxLid != null)
                StartCoroutine(ToggleLid());
            else
                _isOpen = !_isOpen;
        }

        // Q — eat a slice
        if (_isOpen && _slicesLeft > 0 && Input.GetKeyDown(KeyCode.Q))
            EatSlice();
    }

    private IEnumerator ToggleLid()
    {
        _isAnimating = true;

        AudioClip clip = _isOpen ? boxCloseSound : boxOpenSound;
        if (clip != null) _audio.PlayOneShot(clip);

        Quaternion from   = boxLid.localRotation;
        Quaternion target = _isOpen ? _lidClosed : _lidOpen;

        for (float t = 0f; t < 1f; )
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * animSpeed);
            boxLid.localRotation = Quaternion.Slerp(from, target, EaseInOutQuad(t));
            yield return null;
        }

        boxLid.localRotation = target;
        _isOpen      = !_isOpen;
        _isAnimating = false;
    }

    private void EatSlice()
    {
        _slicesLeft--;
        float ratio = (float)_slicesLeft / totalSlices;

        if (pizzaMesh != null)
            pizzaMesh.localScale = _pizzaFullScale * Mathf.Clamp(ratio, 0.05f, 1f);

        if (_audio != null && eatSound != null)
            _audio.PlayOneShot(eatSound);
    }

    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        string status  = _isOpen ? "Open" : "Closed";
        string slices  = $"Slices left: {_slicesLeft}/{totalSlices}";
        string eatLine = (_isOpen && _slicesLeft > 0)
            ? "\n[Q] Eat a slice" : (_slicesLeft == 0 ? "\nAll slices eaten!" : "");
        string boxLine = $"[E] {(_isOpen ? "Close box" : "Open box")}";

        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.72f, 360f, 80f),
            $"<b>Pizza Box</b>  [{status}]  {slices}\n{boxLine}{eatLine}",
            _hintStyle);
    }

    private static GUIStyle BuildHintStyle()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize  = 16;
        s.alignment = TextAnchor.MiddleCenter;
        s.normal.textColor = Color.white;
        s.richText  = true;
        return s;
    }

    private static float EaseInOutQuad(float t)
        => t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
}
