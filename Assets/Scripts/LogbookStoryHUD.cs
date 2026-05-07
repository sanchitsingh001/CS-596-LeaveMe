using UnityEngine;

/// <summary>
/// Minimal on-screen objective + subtitle overlay for "The Logbook" narrative.
/// Uses OnGUI to match the project's existing hint style.
/// </summary>
public class LogbookStoryHUD : MonoBehaviour
{
    [Header("Layout")]
    public float objectiveY = 24f;
    public float subtitleY = 90f;
    public float maxWidth = 820f;

    [Header("Subtitle timing")]
    public float subtitleSeconds = 3.5f;
    public float subtitleFadeSeconds = 0.6f;

    [Header("Ending")]
    public float endingFadeSeconds = 1.0f;

    private string _objective = "";
    private string _subtitle = "";
    private float _subtitleUntilTime;

    private bool _endingActive;
    private float _endingStartedTime;
    private float _endingFadeSecondsActive;
    private string _endingText = "ENDING: OBSERVED";

    private GUIStyle _objectiveStyle;
    private GUIStyle _subtitleStyle;
    private GUIStyle _endingStyle;

    public void SetObjective(string text)
    {
        _objective = text ?? "";
    }

    public void ShowSubtitle(string text, float? secondsOverride = null)
    {
        _subtitle = text ?? "";
        float dur = secondsOverride.HasValue ? Mathf.Max(0.1f, secondsOverride.Value) : Mathf.Max(0.1f, subtitleSeconds);
        _subtitleUntilTime = Time.time + dur;
    }

    public void StartEnding(string endingText = null, float? fadeSecondsOverride = null)
    {
        _endingActive = true;
        _endingStartedTime = Time.time;
        if (!string.IsNullOrEmpty(endingText))
            _endingText = endingText;
        _endingFadeSecondsActive = fadeSecondsOverride.HasValue && fadeSecondsOverride.Value > 0.01f
            ? fadeSecondsOverride.Value
            : endingFadeSeconds;
    }

    private void OnGUI()
    {
        EnsureStyles();

        float w = Mathf.Min(maxWidth, Screen.width - 40f);
        float x = Screen.width * 0.5f - w * 0.5f;

        if (!string.IsNullOrEmpty(_objective) && !_endingActive)
        {
            GUI.Label(new Rect(x, objectiveY, w, 46f), _objective, _objectiveStyle);
        }

        if (!_endingActive && !string.IsNullOrEmpty(_subtitle))
        {
            float remaining = _subtitleUntilTime - Time.time;
            if (remaining > 0f)
            {
                float a = 1f;
                if (subtitleFadeSeconds > 0.01f && remaining < subtitleFadeSeconds)
                    a = Mathf.Clamp01(remaining / subtitleFadeSeconds);

                Color prev = GUI.color;
                GUI.color = new Color(prev.r, prev.g, prev.b, a);
                GUI.Label(new Rect(x, subtitleY, w, 120f), _subtitle, _subtitleStyle);
                GUI.color = prev;
            }
        }

        if (_endingActive)
        {
            float t = Time.time - _endingStartedTime;
            float a = _endingFadeSecondsActive <= 0.01f ? 1f : Mathf.Clamp01(t / _endingFadeSecondsActive);

            Color prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, a);
            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            if (a >= 0.95f)
            {
                GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), _endingText, _endingStyle);
            }
        }
    }

    private void EnsureStyles()
    {
        if (_objectiveStyle == null)
        {
            _objectiveStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20,
                alignment = TextAnchor.UpperCenter,
                richText = true,
                wordWrap = true
            };
            _objectiveStyle.normal.textColor = Color.white;
        }

        if (_subtitleStyle == null)
        {
            _subtitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                alignment = TextAnchor.UpperCenter,
                richText = true,
                wordWrap = true
            };
            _subtitleStyle.normal.textColor = Color.white;
        }

        if (_endingStyle == null)
        {
            _endingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 34,
                alignment = TextAnchor.MiddleCenter,
                richText = true,
                wordWrap = true
            };
            _endingStyle.normal.textColor = Color.white;
        }
    }
}

