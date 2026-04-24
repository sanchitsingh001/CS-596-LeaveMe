using UnityEngine;

/// <summary>
/// Toggles a Point Light and an optional LED sphere emissive material on and off
/// at regular intervals to simulate a surveillance camera indicator light.
/// </summary>
public class BlinkingLight : MonoBehaviour
{
    [Header("Components")]
    [Tooltip("The Point Light used as the red indicator glow.")]
    public Light indicatorLight;
    [Tooltip("Optional small sphere that represents the physical LED. Its material emission is toggled.")]
    public Renderer ledSphereRenderer;

    [Header("Timing")]
    [Tooltip("Total cycle duration in seconds (light is ON for half, OFF for half).")]
    public float blinkInterval = 1f;

    [Header("Colours")]
    public Color lightOnColor = Color.red;
    [Tooltip("Emission HDR colour for the LED sphere when ON (use a bright HDR value for glow).")]
    public Color emissionOnColor = new Color(3f, 0f, 0f, 1f);   // HDR red

    // Material instance so we don't pollute the shared asset
    private Material _ledMatInstance;
    private bool _lightOn = true;

    // Cached shader property IDs for performance
    private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");

    private void Start()
    {
        if (indicatorLight != null)
        {
            indicatorLight.color = lightOnColor;
            indicatorLight.enabled = true;
        }

        if (ledSphereRenderer != null)
        {
            // Instantiate so we modify only this camera's LED, not the shared material
            _ledMatInstance = ledSphereRenderer.material;
            SetLedEmission(true);
        }

        InvokeRepeating(nameof(ToggleLight), 0f, blinkInterval * 0.5f);
    }

    private void ToggleLight()
    {
        _lightOn = !_lightOn;

        if (indicatorLight != null)
            indicatorLight.enabled = _lightOn;

        if (_ledMatInstance != null)
            SetLedEmission(_lightOn);
    }

    private void SetLedEmission(bool on)
    {
        if (_ledMatInstance == null) return;

        if (on)
        {
            _ledMatInstance.EnableKeyword("_EMISSION");
            _ledMatInstance.SetColor(EmissionColor, emissionOnColor);
        }
        else
        {
            _ledMatInstance.DisableKeyword("_EMISSION");
            _ledMatInstance.SetColor(EmissionColor, Color.black);
        }
    }

    private void OnDestroy()
    {
        // Clean up the instantiated material to avoid memory leaks
        if (_ledMatInstance != null)
            Destroy(_ledMatInstance);
    }
}
