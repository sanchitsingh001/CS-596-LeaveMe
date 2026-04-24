using UnityEngine;

public class FPSController : MonoBehaviour
{
    public float speed = 5f;
    public float mouseSensitivity = 2f;

    [Header("Footsteps")]
    public AudioClip footstepSound;
    [Tooltip("Time in seconds between each footstep sound.")]
    public float footstepInterval = 0.45f;

    private float _xRotation = 0f;
    private CharacterController _controller;
    private Camera _cam;
    private bool _inputEnabled = true;

    private AudioSource _footstepAudio;
    private float _footstepTimer = 0f;

    /// <summary>Enable or disable all player movement and mouse-look input.</summary>
    public void SetMovementAndLookEnabled(bool enabled)
    {
        _inputEnabled = enabled;
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

    private void Start()
    {
        _controller = GetComponent<CharacterController>();
        _cam = Camera.main;
        Cursor.lockState = CursorLockMode.Locked;

        // Footstep audio source
        _footstepAudio = gameObject.AddComponent<AudioSource>();
        _footstepAudio.spatialBlend  = 0f;
        _footstepAudio.playOnAwake   = false;
        _footstepAudio.volume        = 0.45f;
        _footstepAudio.pitch         = 1f;
    }

    private void Update()
    {
        if (!_inputEnabled)
            return;

        float x = Input.GetAxis("Horizontal");
        float z = Input.GetAxis("Vertical");
        Vector3 move = transform.right * x + transform.forward * z;
        _controller.Move(move * speed * Time.deltaTime);

        // Footstep tick — use clip + Play() to avoid PlayOneShot scheduling lag
        bool isMoving = (Mathf.Abs(x) > 0.1f || Mathf.Abs(z) > 0.1f) && _controller.isGrounded;
        if (isMoving && footstepSound != null)
        {
            _footstepTimer -= Time.deltaTime;
            if (_footstepTimer <= 0f)
            {
                _footstepAudio.clip  = footstepSound;
                _footstepAudio.pitch = Random.Range(0.9f, 1.1f);
                _footstepAudio.Play();
                _footstepTimer = footstepInterval;
            }
        }
        else if (!isMoving)
        {
            // Don't reset to 0 — let the current interval wind down so the next
            // step plays at a natural cadence when movement resumes.
            _footstepTimer = Mathf.Min(_footstepTimer, footstepInterval * 0.5f);
        }

        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -80f, 80f);
        _cam.transform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);
        transform.Rotate(Vector3.up * mouseX);
    }
}