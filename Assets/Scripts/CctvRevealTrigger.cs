using UnityEngine;

/// <summary>
/// Attach to a CCTV/camera object. When the player looks at it (center ray) within range,
/// shows a one-time HUD message and notifies the Logbook story manager.
/// </summary>
[RequireComponent(typeof(Collider))]
public class CctvRevealTrigger : MonoBehaviour
{
    public float noticeDistance = 6f;
    public string noticeText = "The camera light is blinking.";

    [Tooltip("Optional: if set, uses this HUD instead of finding one.")]
    public LogbookStoryHUD hud;

    private bool _noticed;

    private void Start()
    {
        if (hud == null)
            hud = FindFirstObjectByType<LogbookStoryHUD>();
    }

    private void Update()
    {
        if (_noticed) return;
        if (Camera.main == null) return;
        if (LogbookStoryManager.Instance != null && !LogbookStoryManager.Instance.IsCctvRevealPhaseActive)
            return;

        Ray ray = Camera.main.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, noticeDistance, ~0, QueryTriggerInteraction.Collide))
            return;

        if (hit.collider == null) return;
        if (hit.collider.gameObject != gameObject && !hit.collider.transform.IsChildOf(transform))
            return;

        _noticed = true;
        hud?.ShowSubtitle(noticeText, 2.8f);
        if (LogbookStoryManager.Instance != null)
        {
            LogbookStoryManager.Instance.SetPendingCctvFocusWorldHit(hit.point);
            LogbookStoryManager.Instance.NotifyAction(LogbookStoryManager.LogbookAction.CctvNoticed);
        }
    }
}

