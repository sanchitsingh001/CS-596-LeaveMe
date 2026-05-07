using System.Collections;
using UnityEngine;

public class FPSController : MonoBehaviour
{
    public float speed = 5f;
    public float mouseSensitivity = 2f;

    [Header("Gravity")]
    [Tooltip("Downward acceleration applied to the CharacterController each frame.")]
    public float gravity = 20f;
    [Tooltip("Small downward bias kept while grounded so isGrounded stays true and the player doesn't 'pop' on slopes.")]
    public float groundedStickForce = 2f;

    [Header("Footsteps")]
    public AudioClip footstepSound;
    [Tooltip("Approximate distance traveled (meters) between each footstep sound.")]
    public float footstepDistance = 1.6f;
    [Tooltip("Footstep loudness multiplier. Values > 1 can clip/distort if too high.")]
    [Range(0f, 2f)]
    public float footstepVolume = 1.4f;

    private float _xRotation = 0f;
    private CharacterController _controller;
    private Camera _cam;
    private bool _movementEnabled = true;
    private bool _lookEnabled = true;

    private AudioSource _footstepAudio;
    private float _footstepAccumDistance = 0f;
    private float _verticalVelocity = 0f;

    private Coroutine _scriptedLookRoutine;
    private float _fovBeforeScriptedLook = 60f;

    /// <summary>Enable or disable all player movement and mouse-look input.</summary>
    public void SetMovementAndLookEnabled(bool enabled)
    {
        _movementEnabled = enabled;
        _lookEnabled = enabled;
    }

    /// <summary>Enable or disable only WASD movement.</summary>
    public void SetMovementEnabled(bool enabled)
    {
        _movementEnabled = enabled;
        if (!enabled)
            StopFootsteps();
    }

    /// <summary>Enable or disable only mouse-look.</summary>
    public void SetLookEnabled(bool enabled)
    {
        _lookEnabled = enabled;
    }

    /// <summary>
    /// Saves the current camera pitch and player yaw into the provided out params.
    /// Call before disabling input so they can be restored later.
    /// </summary>
    public void SaveLookState(out float pitchDegrees, out Quaternion playerYaw)
    {
        pitchDegrees = _xRotation;
        playerYaw = transform.rotation;
    }

    /// <summary>
    /// Restores camera pitch and player yaw saved by <see cref="SaveLookState"/>.
    /// Call after re-enabling input to avoid a snap.
    /// </summary>
    public void RestoreLookState(float pitchDegrees, Quaternion playerYaw)
    {
        _xRotation = pitchDegrees;
        transform.rotation = playerYaw;
        if (_cam != null)
            _cam.transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
    }

    /// <summary>
    /// Smoothly turns the player yaw and camera pitch to face <paramref name="worldPoint"/>.
    /// While the move plays, mouse-look and movement are temporarily disabled and then
    /// restored to whatever they were before the call.
    /// </summary>
    public void LookAtPoint(Vector3 worldPoint, float duration = 1f, System.Action onComplete = null)
    {
        if (_scriptedLookRoutine != null)
            StopCoroutine(_scriptedLookRoutine);
        _scriptedLookRoutine = StartCoroutine(LookAtPointRoutine(worldPoint, duration, null, onComplete, false));
    }

    /// <summary>
    /// Same as <see cref="LookAtPoint"/> but narrows camera FOV over the duration for a "zoom in" feel.
    /// </summary>
    public void LookAtPointWithFovZoom(
        Vector3 worldPoint,
        float duration,
        float targetFov,
        System.Action onComplete = null,
        bool leaveControlsDisabledWhenDone = false)
    {
        if (_scriptedLookRoutine != null)
            StopCoroutine(_scriptedLookRoutine);
        _scriptedLookRoutine = StartCoroutine(LookAtPointRoutine(worldPoint, duration, targetFov, onComplete, leaveControlsDisabledWhenDone));
    }

    private IEnumerator LookAtPointRoutine(
        Vector3 target,
        float duration,
        float? zoomFov,
        System.Action onComplete,
        bool leaveControlsDisabledWhenDone)
    {
        if (_cam == null)
            _cam = Camera.main;

        bool prevLook = _lookEnabled;
        bool prevMove = _movementEnabled;
        _lookEnabled = false;
        _movementEnabled = false;
        StopFootsteps();

        if (_cam == null)
        {
            _lookEnabled = prevLook;
            _movementEnabled = prevMove;
            _scriptedLookRoutine = null;
            onComplete?.Invoke();
            yield break;
        }

        _fovBeforeScriptedLook = _cam.fieldOfView;

        Vector3 dir = target - _cam.transform.position;
        if (dir.sqrMagnitude < 1e-4f)
        {
            if (zoomFov.HasValue)
                _cam.fieldOfView = zoomFov.Value;
            if (!leaveControlsDisabledWhenDone)
            {
                _lookEnabled = prevLook;
                _movementEnabled = prevMove;
            }
            _scriptedLookRoutine = null;
            onComplete?.Invoke();
            yield break;
        }

        // Decompose desired look rotation into player yaw (around world up) and camera pitch.
        Quaternion lookRot = Quaternion.LookRotation(dir.normalized, Vector3.up);
        Vector3 e = lookRot.eulerAngles;
        float targetYaw = e.y;
        float targetPitch = e.x;
        if (targetPitch > 180f) targetPitch -= 360f;
        targetPitch = Mathf.Clamp(targetPitch, -80f, 80f);

        float startPitch = _xRotation;
        Quaternion startYawRot = transform.rotation;
        Quaternion endYawRot = Quaternion.Euler(0f, targetYaw, 0f);

        float startFov = _cam.fieldOfView;
        float endFov = zoomFov ?? startFov;

        duration = Mathf.Max(0.01f, duration);
        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float eased = u * u * (3f - 2f * u); // smoothstep
            _xRotation = Mathf.LerpAngle(startPitch, targetPitch, eased);
            transform.rotation = Quaternion.Slerp(startYawRot, endYawRot, eased);
            _cam.transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
            if (zoomFov.HasValue)
                _cam.fieldOfView = Mathf.Lerp(startFov, endFov, eased);
            yield return null;
        }

        _xRotation = targetPitch;
        transform.rotation = endYawRot;
        _cam.transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        if (zoomFov.HasValue)
            _cam.fieldOfView = endFov;

        if (!leaveControlsDisabledWhenDone)
        {
            _lookEnabled = prevLook;
            _movementEnabled = prevMove;
            if (zoomFov.HasValue)
                _cam.fieldOfView = _fovBeforeScriptedLook;
        }

        _scriptedLookRoutine = null;
        onComplete?.Invoke();
    }

    private void Start()
    {
        _controller = GetComponent<CharacterController>();
        _cam = Camera.main;
        Cursor.lockState = CursorLockMode.Locked;

        // Footstep audio source
        _footstepAudio = gameObject.AddComponent<AudioSource>();
        _footstepAudio.spatialBlend  = 0f;
        _footstepAudio.playOnAwake   = false;
        _footstepAudio.volume        = 1f;
        _footstepAudio.pitch         = 1f;

        // Avoid "late" playback (sounds like it triggers on key-up) by forcing the
        // clip data to be resident before the first step plays.
        if (footstepSound != null)
            footstepSound.LoadAudioData();
    }

    private void Update()
    {
        if (!_movementEnabled && !_lookEnabled)
            return;

        if (_movementEnabled)
        {
            // Smoothed movement feels nicer, but footsteps use raw input so they
            // stop immediately on key release.
            float xMove = Input.GetAxis("Horizontal");
            float zMove = Input.GetAxis("Vertical");
            float xRaw  = Input.GetAxisRaw("Horizontal");
            float zRaw  = Input.GetAxisRaw("Vertical");

            Vector3 horizontal = (transform.right * xMove + transform.forward * zMove) * speed;

            // Keep a small downward force while grounded so isGrounded remains stable.
            if (_controller.isGrounded && _verticalVelocity < 0f)
                _verticalVelocity = -groundedStickForce;
            else
                _verticalVelocity -= gravity * Time.deltaTime;

            Vector3 motion = horizontal + Vector3.up * _verticalVelocity;
            _controller.Move(motion * Time.deltaTime);

            bool hasMoveInput = Mathf.Abs(xRaw) > 0.1f || Mathf.Abs(zRaw) > 0.1f;
            Vector3 vel = _controller.velocity;
            float horizontalSpeed = new Vector3(vel.x, 0f, vel.z).magnitude;
            bool shouldFootstep = _controller.isGrounded && hasMoveInput && horizontalSpeed > 0.05f;

            bool footstepClipReady =
                footstepSound != null && footstepSound.loadState == AudioDataLoadState.Loaded;

            if (shouldFootstep && footstepClipReady && footstepDistance > 0.01f)
            {
                _footstepAccumDistance += horizontalSpeed * Time.deltaTime;
                if (_footstepAccumDistance >= footstepDistance)
                {
                    _footstepAccumDistance = 0f;
                    _footstepAudio.pitch = Random.Range(0.9f, 1.1f);
                    _footstepAudio.PlayOneShot(footstepSound, footstepVolume);
                }
            }
            else
            {
                StopFootsteps();
            }
        }
        else
        {
            StopFootsteps();
        }

        if (!_lookEnabled)
            return;

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -80f, 80f);
        _cam.transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }

    private void StopFootsteps()
    {
        _footstepAccumDistance = 0f;
        if (_footstepAudio != null && _footstepAudio.isPlaying)
            _footstepAudio.Stop();
    }
}