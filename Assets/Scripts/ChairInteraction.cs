using UnityEngine;
using System.Collections;

/// <summary>
/// Sit/stand interaction for the gaming chair.
///
/// Controls (shown as HUD hint):
///   E          — sit down when looking at the chair / stand up when seated
///   WASD       — roll the chair while seated (moves both chair and player together)
///   Mouse      — look around while seated (via FPSController which is re-enabled for look only)
///
/// Strategy: we move the Player capsule so its camera child lands at the SitPoint.
/// While seated, WASD moves the chair AND the player together.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ChairInteraction : MonoBehaviour
{
    [Header("Sit Configuration")]
    [Tooltip("Empty child of the chair. Position at seated eye-level; rotation = direction the seated player faces.")]
    public Transform sitPoint;
    [Tooltip("Max raycast distance from the camera to the chair for E to register.")]
    public float interactDistance = 2.5f;
    [Tooltip("Sit-down lerp speed.")]
    public float sitLerpSpeed = 3f;
    [Tooltip("Stand-up lerp speed.")]
    public float standLerpSpeed = 4f;
    [Tooltip("Speed the chair rolls on wheels while seated (m/s).")]
    public float chairRollSpeed = 1.8f;

    [Header("References")]
    public FPSController playerController;

    [Header("Audio")]
    public AudioClip sitSound;
    public AudioClip standSound;
    [Tooltip("Looping sound of chair rolling on the floor.")]
    public AudioClip rollSound;

    // ── Runtime State ────────────────────────────────────────────────────────
    private bool _isSeated       = false;
    private bool _isTransitioning = false;
    private bool _showHint        = false;
    private AudioSource _audio;
    private AudioSource _rollAudio;

    private Transform _playerTransform;
    private Transform _cameraTransform;
    private CharacterController _characterController;

    // Pre-sit snapshot
    private Vector3    _savedPlayerPosition;
    private Quaternion _savedPlayerYaw;
    private float      _savedCameraPitch;

    private float _eyeHeight;

    private GUIStyle _hintStyle;

    private void Start()
    {
        GameObject player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[ChairInteraction] No GameObject tagged 'Player' found.", this);
            enabled = false;
            return;
        }

        _playerTransform   = player.transform;
        _characterController = player.GetComponent<CharacterController>();
        _cameraTransform   = Camera.main.transform;
        _eyeHeight         = _cameraTransform.localPosition.y;

        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 1f;
        _audio.playOnAwake  = false;

        if (rollSound != null)
        {
            _rollAudio            = gameObject.AddComponent<AudioSource>();
            _rollAudio.clip       = rollSound;
            _rollAudio.loop       = true;
            _rollAudio.spatialBlend = 0.5f;
            _rollAudio.volume     = 0.35f;
            _rollAudio.playOnAwake = false;
        }

        if (sitPoint == null)
            Debug.LogError("[ChairInteraction] SitPoint is not assigned.", this);
        if (playerController == null)
            Debug.LogWarning("[ChairInteraction] FPSController reference not set.", this);
    }

    private void Update()
    {
        if (_isTransitioning) return;

        // ── Hint ──────────────────────────────────────────────────────────────
        _showHint = false;
        if (!_isSeated)
        {
            Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
            if (Physics.Raycast(ray, out RaycastHit hit, interactDistance)
                && (hit.collider.transform.IsChildOf(transform) || hit.collider.gameObject == gameObject))
            {
                _showHint = true;
                if (Input.GetKeyDown(KeyCode.E))
                    StartCoroutine(SitDown());
            }
        }
        else
        {
            _showHint = true;
            if (Input.GetKeyDown(KeyCode.E))
            {
                StartCoroutine(StandUp());
                return;
            }

            // ── Chair rolling on wheels ───────────────────────────────────────
            float h = Input.GetAxis("Horizontal");
            float v = Input.GetAxis("Vertical");
            bool rolling = (Mathf.Abs(h) > 0.05f || Mathf.Abs(v) > 0.05f);

            if (rolling)
            {
                // Move in the direction the PLAYER is currently facing (flat)
                Vector3 forward = Vector3.ProjectOnPlane(_playerTransform.forward, Vector3.up).normalized;
                Vector3 right   = Vector3.ProjectOnPlane(_playerTransform.right, Vector3.up).normalized;
                Vector3 delta   = (forward * v + right * h) * chairRollSpeed * Time.deltaTime;

                // Move chair
                transform.position += delta;

                // Keep player eye at sitPoint (parent-child position is maintained automatically
                // because player is NOT a child of the chair — we must sync manually)
                Vector3 targetPlayerPos = sitPoint.position - Vector3.up * _eyeHeight;
                _playerTransform.position = targetPlayerPos;
            }

            // Roll sound
            if (_rollAudio != null)
            {
                if (rolling && !_rollAudio.isPlaying) _rollAudio.Play();
                else if (!rolling && _rollAudio.isPlaying) _rollAudio.Stop();
            }
        }
    }

    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        string msg = _isSeated
            ? "<b>Gaming Chair</b>  [Seated]\n[E] Stand Up    [WASD] Roll Chair"
            : "<b>Gaming Chair</b>\n[E] Sit Down";

        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.72f, 360f, 60f),
            msg, _hintStyle);
    }

    private IEnumerator SitDown()
    {
        _isTransitioning = true;

        _savedPlayerPosition = _playerTransform.position;
        playerController?.SaveLookState(out _savedCameraPitch, out _savedPlayerYaw);

        playerController?.SetMovementAndLookEnabled(false);
        if (_characterController != null)
            _characterController.enabled = false;

        Vector3 targetPlayerPos = sitPoint.position - Vector3.up * _eyeHeight;

        Vector3 sitForwardFlat = Vector3.ProjectOnPlane(sitPoint.forward, Vector3.up);
        Quaternion targetYaw = sitForwardFlat.sqrMagnitude > 0.001f
            ? Quaternion.LookRotation(sitForwardFlat, Vector3.up)
            : _playerTransform.rotation;

        _playerTransform.rotation    = targetYaw;
        _cameraTransform.localRotation = Quaternion.identity;

        if (_audio != null && sitSound != null) _audio.PlayOneShot(sitSound);

        Vector3 startPos = _playerTransform.position;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * sitLerpSpeed);
            _playerTransform.position = Vector3.Lerp(startPos, targetPlayerPos, EaseInOutQuad(t));
            yield return null;
        }

        _playerTransform.position = targetPlayerPos;

        // Re-enable look-only: we keep movement disabled (WASD handled above for rolling)
        playerController?.SetMovementAndLookEnabled(true);

        _isSeated        = true;
        _isTransitioning = false;
    }

    private IEnumerator StandUp()
    {
        _isTransitioning = true;

        if (_rollAudio != null && _rollAudio.isPlaying)
            _rollAudio.Stop();

        // Disable look while transitioning
        playerController?.SetMovementAndLookEnabled(false);

        Vector3 startPos = _playerTransform.position;
        float t = 0f;
        while (t < 1f)
        {
            t = Mathf.MoveTowards(t, 1f, Time.deltaTime * standLerpSpeed);
            _playerTransform.position = Vector3.Lerp(startPos, _savedPlayerPosition, EaseInOutQuad(t));
            yield return null;
        }

        _playerTransform.position = _savedPlayerPosition;

        if (_characterController != null)
            _characterController.enabled = true;

        if (_audio != null && standSound != null) _audio.PlayOneShot(standSound);

        playerController?.RestoreLookState(_savedCameraPitch, _savedPlayerYaw);
        playerController?.SetMovementAndLookEnabled(true);

        _isSeated        = false;
        _isTransitioning = false;
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
