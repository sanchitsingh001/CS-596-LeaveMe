using UnityEngine;
using System.Collections;

/// <summary>
/// Interactive phone object: rings periodically, player can answer/hang up,
/// and shows a notification badge.
///
/// Controls (shown when looking at the phone):
///   E — answer a ringing call / hang up an active call
///   T — send a text (plays typing + notification sound)
/// </summary>
[RequireComponent(typeof(Collider))]
public class PhoneInteraction : MonoBehaviour
{
    [Header("Interaction")]
    public float interactDistance = 2.5f;

    [Header("Ringing")]
    [Tooltip("Seconds between automatic ring cycles.")]
    public float ringInterval = 120f;
    [Tooltip("How long each ring lasts before the call is missed.")]
    public float ringDuration = 2f;

    [Header("Audio")]
    public AudioClip ringingSound;
    [Tooltip("Leave empty to disable vibration buzz.")]
    public AudioClip vibrationSound;
    public AudioClip typingSound;
    public AudioClip notificationSound;
    public AudioClip dialingSound;

    [Header("Screen glow (optional)")]
    [Tooltip("Renderer whose emission toggles when the phone is active.")]
    public Renderer screenRenderer;

    // ── State ─────────────────────────────────────────────────────────────────
    private enum PhoneState { Idle, Ringing, InCall }
    private PhoneState _state = PhoneState.Idle;

    private AudioSource _audio;
    private Coroutine   _ringRoutine;
    private bool        _showHint = false;
    private GUIStyle    _hintStyle;
    private int         _missedCalls = 0;
    private int         _unreadTexts = 0;

    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
    private Material _screenMat;

    private void Start()
    {
        _audio = gameObject.AddComponent<AudioSource>();
        _audio.spatialBlend = 0.8f;
        _audio.playOnAwake  = false;

        if (screenRenderer != null)
            _screenMat = screenRenderer.material;

        // Start the ring cycle
        InvokeRepeating(nameof(TriggerRing), ringInterval * 0.5f, ringInterval);
    }

    private void TriggerRing()
    {
        if (_state != PhoneState.Idle) return;
        _state      = PhoneState.Ringing;
        _ringRoutine = StartCoroutine(RingCycle());
    }

    private IEnumerator RingCycle()
    {
        SetScreenGlow(true, new Color(0.6f, 0.8f, 1f));

        // Play ringing sound only — no vibration buzz
        if (ringingSound != null)
        {
            _audio.clip   = ringingSound;
            _audio.loop   = true;
            _audio.volume = 0.9f;
            _audio.Play();
        }

        yield return new WaitForSeconds(ringDuration);

        _audio.Stop();
        _audio.loop = false;

        if (_state == PhoneState.Ringing)
        {
            _missedCalls++;
            _state = PhoneState.Idle;
            SetScreenGlow(false, Color.black);
        }
    }

    private void Update()
    {
        _showHint = false;

        // Check distance first as a reliable fallback
        float dist = Vector3.Distance(Camera.main.transform.position, transform.position);
        bool inRange = dist <= interactDistance;
        if (!inRange) return;

        Ray ray = Camera.main.ScreenPointToRay(
            new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));

        // Use proximity OR raycast — phone collider may have an offset so proximity is reliable
        bool looking = Physics.Raycast(ray, out RaycastHit hit, interactDistance,
                           ~0, QueryTriggerInteraction.Collide)
                       && (hit.collider.transform.IsChildOf(transform)
                           || hit.collider.gameObject == gameObject);

        // Fallback: show hint when within close proximity even if raycast misses
        if (!looking && dist > 1.5f) return;
        _showHint = true;

        // E — answer / hang up
        if (Input.GetKeyDown(KeyCode.E))
        {
            switch (_state)
            {
                case PhoneState.Ringing:
                    AnswerCall();
                    break;
                case PhoneState.InCall:
                    HangUp();
                    break;
                case PhoneState.Idle:
                    // Initiate an outgoing call for flavour
                    StartCoroutine(OutgoingCall());
                    break;
            }
        }

        // T — send a text message
        if (Input.GetKeyDown(KeyCode.T) && _state != PhoneState.InCall)
            StartCoroutine(SendText());
    }

    private void AnswerCall()
    {
        if (_ringRoutine != null)
            StopCoroutine(_ringRoutine);
        _audio.Stop();
        _audio.loop = false;
        _state = PhoneState.InCall;
        SetScreenGlow(true, new Color(0.3f, 1f, 0.5f));
        if (dialingSound != null) _audio.PlayOneShot(dialingSound, 0.7f);
    }

    private void HangUp()
    {
        _state = PhoneState.Idle;
        _audio.Stop();
        SetScreenGlow(false, Color.black);
    }

    private IEnumerator OutgoingCall()
    {
        _state = PhoneState.InCall;
        SetScreenGlow(true, new Color(0.3f, 1f, 0.5f));
        if (dialingSound != null)
        {
            _audio.PlayOneShot(dialingSound, 0.8f);
            yield return new WaitForSeconds(dialingSound.length + 0.5f);
        }
        else
            yield return new WaitForSeconds(2f);

        // Auto hang up after a few seconds for this flavour call
        yield return new WaitForSeconds(4f);
        HangUp();
    }

    private IEnumerator SendText()
    {
        _unreadTexts = 0;
        SetScreenGlow(true, new Color(1f, 0.9f, 0.3f));
        if (typingSound != null)
        {
            _audio.PlayOneShot(typingSound, 0.8f);
            yield return new WaitForSeconds(Mathf.Min(typingSound.length, 1.5f));
        }
        else
            yield return new WaitForSeconds(1f);

        if (notificationSound != null)
            _audio.PlayOneShot(notificationSound, 0.9f);

        yield return new WaitForSeconds(0.5f);
        SetScreenGlow(false, Color.black);
    }

    private void SetScreenGlow(bool on, Color glowColor)
    {
        if (_screenMat == null) return;
        if (on)
        {
            _screenMat.EnableKeyword("_EMISSION");
            _screenMat.SetColor(EmissionColor, glowColor);
        }
        else
        {
            _screenMat.DisableKeyword("_EMISSION");
            _screenMat.SetColor(EmissionColor, Color.black);
        }
    }

    private void OnGUI()
    {
        if (!_showHint) return;
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        string badge = _missedCalls > 0 ? $"  <color=#FF6060>({_missedCalls} missed)</color>" : "";
        string statusLine = _state switch
        {
            PhoneState.Ringing => "<color=#88CCFF>Incoming call…</color>  [E] Answer",
            PhoneState.InCall  => "<color=#66FF88>In call</color>  [E] Hang Up",
            _                  => $"[E] Call  [T] Text{badge}"
        };

        GUI.Label(
            new Rect(Screen.width * 0.5f - 180f, Screen.height * 0.72f, 360f, 60f),
            $"<b>Phone</b>\n{statusLine}",
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

    private void OnDestroy()
    {
        if (_screenMat != null) Destroy(_screenMat);
    }
}
