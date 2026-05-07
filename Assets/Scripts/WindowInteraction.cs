using UnityEngine;
using System.Collections;

/// <summary>
/// Slide-sideways window panel. Press [E] to open/close.
///
/// The panel slides along <see cref="localSlideAxis"/> in the window's OWN local space,
/// revealing an opening. Set localSlideAxis to (1,0,0) or (-1,0,0) per window
/// depending on which side it should slide toward.
///
/// A sky-blue "outside" quad is revealed behind the opening when the window is open.
/// </summary>
[RequireComponent(typeof(Collider))]
public class WindowInteraction : MonoBehaviour
{
    [Header("Animation — Slide")]
    [Tooltip("Direction in LOCAL space the panel slides when opening. Use (1,0,0) or (-1,0,0).")]
    public Vector3 localSlideAxis = Vector3.right;

    [Tooltip("How far (units) the panel slides when opened.")]
    public float openSlide    = 1.8f;
    public float animSpeed    = 2.5f;

    [Header("Interaction")]
    public float interactDistance = 3f;

    [Header("Audio")]
    [Tooltip("Looping wind audio. Plays only while the window is open, fades in/out on open/close.")]
    public AudioClip windLoop;
    [Range(0f, 1f)] public float windVolume = 0.45f;
    [Tooltip("Seconds it takes for the wind to fade in/out when opening/closing.")]
    [Range(0.1f, 5f)] public float windFadeDuration = 1.0f;

    [Header("Outside View (auto-created if left empty)")]
    [Tooltip("Leave empty — a sky-blue quad is auto-created behind the window opening.")]
    public GameObject outsidePlane;

    // ── State ─────────────────────────────────────────────────────────────────
    private bool        _isOpen      = false;
    private bool        _isAnimating = false;
    private bool        _showHint    = false;
    private Vector3     _closedPos;
    private Vector3     _openPos;
    private AudioSource _windSource;
    private GUIStyle    _hintStyle;
    private Coroutine   _fadeRoutine;

    private void Start()
    {
        _closedPos = transform.localPosition;
        // Slide in LOCAL space along the configured axis — works regardless of world rotation
        _openPos   = _closedPos + localSlideAxis.normalized * openSlide;

        _windSource             = gameObject.AddComponent<AudioSource>();
        _windSource.spatialBlend = 0.5f;
        _windSource.playOnAwake  = false;
        _windSource.loop         = true;
        _windSource.volume       = 0f;
        _windSource.clip         = windLoop;

        if (outsidePlane == null)
            CreateOutsidePlane();

        outsidePlane.SetActive(false);
    }

    private void CreateOutsidePlane()
    {
        outsidePlane      = GameObject.CreatePrimitive(PrimitiveType.Quad);
        outsidePlane.name = "WindowOutsideView";
        Destroy(outsidePlane.GetComponent<Collider>());

        // Parent under scene root so it never moves with the sliding panel
        outsidePlane.transform.SetParent(transform.parent, worldPositionStays: false);

        // Push the quad 0.45 units OUTSIDE the room (past wall thickness of 0.3)
        // so it clears the surround cubes (which span ±0.15 around the wall centre)
        // and is visible from inside when the window panel slides away.
        // Both windows face inward: transform.forward points INTO the room → use -forward.
        outsidePlane.transform.position   = transform.position - transform.forward * 0.45f;
        outsidePlane.transform.rotation   = transform.rotation;

        // Large enough to fill the opening (window opening ≈ 2.46 wide × 3.69 tall)
        outsidePlane.transform.localScale = new Vector3(2.5f, 3.8f, 1f);

        var mat   = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        mat.color = new Color(0.55f, 0.80f, 1.0f);   // bright daytime sky
        outsidePlane.GetComponent<Renderer>().material = mat;
    }

    private void Update()
    {
        _showHint = false;

        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        bool looking = Physics.Raycast(ray, out RaycastHit hit, interactDistance)
                       && (hit.collider.transform.IsChildOf(transform)
                           || hit.collider.gameObject == gameObject);

        if (!looking) return;
        _showHint = true;

        if (!_isAnimating && Input.GetKeyDown(KeyCode.E))
            StartCoroutine(ToggleWindow());
    }

    private IEnumerator ToggleWindow()
    {
        _isAnimating = true;

        bool    opening = !_isOpen;

        Vector3 from    = transform.localPosition;
        Vector3 target  = opening ? _openPos : _closedPos;

        if (opening && outsidePlane != null) outsidePlane.SetActive(true);

        // Audio fade
        if (opening)
        {
            // Ensure the assigned clip is used (in case it was changed in Inspector at runtime)
            _windSource.clip = windLoop;
            if (windLoop != null && !_windSource.isPlaying) _windSource.Play();
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeWind(windVolume));
        }
        else
        {
            if (_fadeRoutine != null) StopCoroutine(_fadeRoutine);
            _fadeRoutine = StartCoroutine(FadeWind(0f, stopOnFinish: true));
        }

        // Slide
        for (float t = 0f; t < 1f; )
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * animSpeed);
            transform.localPosition = Vector3.Lerp(from, target, EaseInOutQuad(t));
            yield return null;
        }
        transform.localPosition = target;
        _isOpen = !_isOpen;

        LogbookStoryManager.Instance?.NotifyWindowToggled(_isOpen);

        if (!_isOpen && outsidePlane != null) outsidePlane.SetActive(false);
        _isAnimating = false;
    }

    private IEnumerator FadeWind(float targetVol, bool stopOnFinish = false)
    {
        float start = _windSource.volume, elapsed = 0f;
        float duration = Mathf.Max(0.01f, windFadeDuration);
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _windSource.volume = Mathf.Lerp(start, targetVol, elapsed / duration);
            yield return null;
        }
        _windSource.volume = targetVol;
        if (stopOnFinish) _windSource.Stop();
    }

    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null) _hintStyle = BuildHintStyle();
        string action = _isOpen ? "Close Window" : "Open Window";
        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.72f, 360f, 56f),
            $"<b>Window</b>\n[E] {action}", _hintStyle);
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
