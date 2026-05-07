using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Narrative state machine for "The Logbook".
/// Listens to existing interactables via NotifyAction, spawns/enables the book,
/// appends entries, and drives a minimal HUD + ending.
/// </summary>
public class LogbookStoryManager : MonoBehaviour
{
    public enum LogbookAction
    {
        PizzaAteSlice,
        FridgeOpened,
        FridgeItemPlaced,
        PhoneBasicUsed,
        PhoneCallAttempt,
        PhoneTextAttempt,
        MicrowaveOpened,
        WindowOpened,
        WindowClosed,
        ChairSat,
        CctvNoticed
    }

    private enum Phase
    {
        Scene1_EnterLookAround,
        Scene2_BookAppears,
        Scene3_TestBook,
        Scene4_Phone,
        Scene5_CctvReveal,
        Scene6_Ending
    }

    public static LogbookStoryManager Instance { get; private set; }

    [Header("References")]
    public LogbookStoryHUD hud;
    public BookController book;
    [Tooltip("Root GameObject for the book in the scene (disabled at start).")]
    public GameObject bookRoot;
    [Tooltip("Where the book should appear (table/bed/floor).")]
    public Transform bookSpawnPoint;
    [Tooltip("Root GameObject for the CCTV/camera (optional). If assigned, keep it disabled until the CCTV reveal phase.")]
    public GameObject cctvRoot;
    [Tooltip("Optional spawn point for the CCTV/camera (if you want it to appear somewhere specific).")]
    public Transform cctvSpawnPoint;
    [Tooltip("Lens / camera body to center the player's view on during the ending. If assigned, overrides the raycast hit point.")]
    public Transform cctvLookTarget;

    [Header("CCTV ending")]
    [Tooltip("How long the view stays locked on the camera with a FOV 'zoom' while the screen fades.")]
    [Range(1.2f, 4f)]
    public float cctvFocusDuration = 2.5f;
    [Tooltip("Field of view while staring at the CCTV (lower = tighter zoom).")]
    [Range(18f, 70f)]
    public float cctvNarrowFov = 34f;
    [Tooltip("Full-screen fade to black duration during the CCTV beat (can overlap the focus).")]
    [Range(1f, 5f)]
    public float cctvScreenFadeSeconds = 2.6f;

    [Header("Book spawning")]
    [Tooltip("After this many unique actions, the book appears.")]
    public int bookAppearsAfterUniqueActions = 3;
    [Tooltip("If on, the camera turns to face the book the moment it appears so the player can't miss it.")]
    public bool forceLookAtBookOnSpawn = true;
    [Tooltip("Seconds the forced look-toward-book takes (movement and mouse-look are disabled during this time).")]
    [Range(0.1f, 4f)]
    public float bookLookDuration = 1.0f;

    [Header("Ending audio")]
    public AudioClip doorOpenBehindPlayerClip;
    [Range(0.1f, 6f)]
    public float doorBehindDistance = 1.5f;
    [Range(0f, 1f)]
    public float doorBehindVolume = 0.9f;
    [Tooltip("Extra seconds of silence after the door clip finishes before replaying (only used during repeats).")]
    [Range(0f, 2f)]
    public float doorEndExtraPauseAfterClip = 0.15f;

    [Header("Story tuning")]
    [Tooltip("How many 'test actions' to log before advancing to the phone phase.")]
    public int testActionsToLog = 1;

    [Header("Book paging")]
    [Tooltip("Approximate maximum characters per page before creating a new page.")]
    public int maxCharsPerPage = 900;

    private Phase _phase = Phase.Scene1_EnterLookAround;
    public bool IsCctvRevealPhaseActive => _phase == Phase.Scene5_CctvReveal || _phase == Phase.Scene6_Ending;

    private readonly HashSet<LogbookAction> _allActionsSeen = new HashSet<LogbookAction>();
    private readonly HashSet<LogbookAction> _uniqueForSpawn = new HashSet<LogbookAction>();

    // Keep an action history so we can backfill entries when the book appears.
    private readonly List<LogbookAction> _actionHistory = new List<LogbookAction>();
    private bool _backfilledHistory;

    // Latest payloads (for precision logs)
    private int _lastPizzaSlicesLeft = -1;
    private int _lastPizzaTotalSlices = -1;
    private string _lastPlacedItemName;
    private bool _lastWindowIsOpen;

    // Log entries
    private readonly List<string> _entries = new List<string>();
    private int _entryCounter = 0;

    private FPSController _fps;
    private bool _bookSpawned;
    private bool _bookReadAtLeastOnce;
    private int _testActionsLogged;
    private bool _phoneCallLogged;
    private bool _phoneTextLogged;
    private bool _cctvLogged;
    private bool _endingTriggered;
    private Vector3? _cctvFocusOverrideFromHit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (hud == null)
            hud = FindFirstObjectByType<LogbookStoryHUD>();

        if (book == null)
            book = FindFirstObjectByType<BookController>(FindObjectsInactive.Include);

        if (bookRoot == null && book != null)
            bookRoot = book.gameObject;

        if (book != null)
            book.OnBookOpened += HandleBookOpened;

        if (_fps == null)
            _fps = FindFirstObjectByType<FPSController>();

        // Start subtitle + objective
        hud?.ShowSubtitle("You enter your room.\nSomething feels slightly off.", 4.2f);
        hud?.SetObjective("<b>Objective:</b> Look around the room.");

        // Optional: book should start hidden
        if (bookRoot != null)
            bookRoot.SetActive(false);

        // Optional: CCTV should start hidden and only appear near the end
        if (cctvRoot != null)
            cctvRoot.SetActive(false);

        // Seed first log entry immediately (enter room)
        AddEntry("Subject entered the room.");
        UpdateBookPages();
    }

    private void OnDestroy()
    {
        if (book != null)
            book.OnBookOpened -= HandleBookOpened;

        if (Instance == this)
            Instance = null;
    }

    public void SetPendingCctvFocusWorldHit(Vector3 worldPoint)
    {
        _cctvFocusOverrideFromHit = worldPoint;
    }

    public void NotifyAction(LogbookAction action)
    {
        if (_endingTriggered) return;

        // Track first-time-seen (only used for spawn gating), but keep a full history of ALL actions
        // so repeatable interactions (pizza slices, window toggles, etc.) always log.
        _allActionsSeen.Add(action);
        _actionHistory.Add(action);

        // Phase-independent: CCTV ends the story when noticed
        if (action == LogbookAction.CctvNoticed)
        {
            // Ignore premature CCTV notices; the camera should only matter at the end.
            if (!IsCctvRevealPhaseActive)
                return;

            if (!_cctvLogged)
            {
                _cctvLogged = true;
                HandleCctvReveal();
            }
            return;
        }

        // Scene1: collect actions to spawn book
        if (_phase == Phase.Scene1_EnterLookAround)
        {
            if (CountsTowardSpawn(action))
                _uniqueForSpawn.Add(action);

            if (!_bookSpawned && _uniqueForSpawn.Count >= Mathf.Max(1, bookAppearsAfterUniqueActions))
            {
                SpawnBook();
                _phase = Phase.Scene2_BookAppears;
                hud?.ShowSubtitle("Wait... was that book there before?");
                hud?.SetObjective("<b>Objective:</b> Read the book.");
            }
        }

        // After the book exists, append log entries for the actions that matter
        if (!_bookSpawned)
            return;

        // When the book first appears, backfill ALL actions the player already did (including repeats),
        // then return because this current action is part of the backfill history.
        if (!_backfilledHistory)
        {
            BackfillHistoryIntoBook();
            _backfilledHistory = true;
            UpdateBookPages();
            book?.RefreshPageContentIfOpen();
            return;
        }

        AppendActionToBook(action);

        UpdateBookPages();
        book?.RefreshPageContentIfOpen();

        // Phase progression
        if (_phase == Phase.Scene2_BookAppears && _bookReadAtLeastOnce)
        {
            _phase = Phase.Scene3_TestBook;
            hud?.SetObjective("<b>Objective:</b> Do something else. Then check the book again.");
        }

        if (_phase == Phase.Scene3_TestBook && _testActionsLogged >= Mathf.Max(1, testActionsToLog))
        {
            _phase = Phase.Scene4_Phone;
            hud?.SetObjective("<b>Objective:</b> Try calling for help. Then send a text.");
        }

        if (_phase == Phase.Scene4_Phone && _phoneCallLogged && _phoneTextLogged)
        {
            _phase = Phase.Scene5_CctvReveal;
            hud?.SetObjective("<b>Objective:</b> Find out who is watching.");
            ActivateCctvIfNeeded();
        }
    }

    public void NotifyWindowToggled(bool isOpen)
    {
        _lastWindowIsOpen = isOpen;
        NotifyAction(isOpen ? LogbookAction.WindowOpened : LogbookAction.WindowClosed);
    }

    private void ActivateCctvIfNeeded()
    {
        if (cctvRoot == null) return;
        if (cctvRoot.activeSelf) return;

        if (cctvSpawnPoint != null)
        {
            cctvRoot.transform.position = cctvSpawnPoint.position;
            cctvRoot.transform.rotation = cctvSpawnPoint.rotation;
        }

        cctvRoot.SetActive(true);
    }

    private void HandleBookOpened()
    {
        if (!_bookSpawned) return;
        _bookReadAtLeastOnce = true;

        if (_phase == Phase.Scene2_BookAppears)
        {
            _phase = Phase.Scene3_TestBook;
            hud?.SetObjective("<b>Objective:</b> Do something else. Then check the book again.");
        }
    }

    private void SpawnBook()
    {
        _bookSpawned = true;

        if (bookRoot != null)
        {
            bookRoot.SetActive(true);
            if (bookSpawnPoint != null)
            {
                bookRoot.transform.position = bookSpawnPoint.position;
                bookRoot.transform.rotation = bookSpawnPoint.rotation;
            }
        }

        if (forceLookAtBookOnSpawn)
            ForceLookAtBook();
    }

    private void ForceLookAtBook()
    {
        if (_fps == null)
            _fps = FindFirstObjectByType<FPSController>();
        if (_fps == null) return;

        // Prefer the spawn point or the book transform; fall back to the book root.
        Transform target = null;
        if (bookSpawnPoint != null)
            target = bookSpawnPoint;
        else if (book != null)
            target = book.transform;
        else if (bookRoot != null)
            target = bookRoot.transform;
        if (target == null) return;

        _fps.LookAtPoint(target.position, bookLookDuration);
    }

    /// <summary>
    /// More precise pizza logging: call this instead of the plain action when you have counts.
    /// </summary>
    public void NotifyPizzaSliceEaten(int slicesLeft, int totalSlices)
    {
        _lastPizzaSlicesLeft = slicesLeft;
        _lastPizzaTotalSlices = totalSlices;
        NotifyAction(LogbookAction.PizzaAteSlice);
    }

    /// <summary>
    /// Logs a placed item name (best-effort).
    /// </summary>
    public void NotifyFridgeItemPlaced(GameObject item)
    {
        _lastPlacedItemName = item != null ? item.name : null;
        NotifyAction(LogbookAction.FridgeItemPlaced);
    }

    private void RegisterTestActionIfRelevant()
    {
        if (_phase != Phase.Scene3_TestBook) return;
        _testActionsLogged++;
        if (_testActionsLogged == 1)
        {
            // Creepy kicker line after the player proves the book is live.
            AddNonEntryLine("Subject is becoming aware.");
        }
    }

    private void HandleCctvReveal()
    {
        if (_endingTriggered) return;
        _endingTriggered = true;
        _phase = Phase.Scene6_Ending;

        AddFinalBlock(
            "FINAL ENTRY:",
            "Subject noticed the camera.",
            "Observation successful.",
            "Do not let the subject leave."
        );
        UpdateBookPages();
        book?.RefreshPageContentIfOpen();

        StartCoroutine(CctvEndingSequence());
    }

    private IEnumerator CctvEndingSequence()
    {
        if (_fps == null)
            _fps = FindFirstObjectByType<FPSController>();

        Vector3 focus = ResolveCctvFocusWorld();
        _fps?.LookAtPointWithFovZoom(focus, cctvFocusDuration, cctvNarrowFov, null, leaveControlsDisabledWhenDone: true);

        // Two distinct door hits, with a pause that lets the clip fully play.
        float clipLen = (doorOpenBehindPlayerClip != null) ? Mathf.Max(0.01f, doorOpenBehindPlayerClip.length) : 0.35f;
        float pauseAfter = Mathf.Max(0f, doorEndExtraPauseAfterClip);

        PlayDoorBehindPlayer();
        yield return new WaitForSeconds(clipLen + pauseAfter);
        PlayDoorBehindPlayer();

        hud?.ShowSubtitle("You hear the door open behind you.", 3.2f);
        hud?.StartEnding("ENDING: OBSERVED", cctvScreenFadeSeconds);

        yield return new WaitForSeconds(cctvFocusDuration);
    }

    private Vector3 ResolveCctvFocusWorld()
    {
        if (cctvLookTarget != null)
        {
            _cctvFocusOverrideFromHit = null;
            return cctvLookTarget.position;
        }
        if (_cctvFocusOverrideFromHit.HasValue)
        {
            Vector3 p = _cctvFocusOverrideFromHit.Value;
            _cctvFocusOverrideFromHit = null;
            return p;
        }
        if (cctvRoot != null)
            return cctvRoot.transform.position;
        var trig = FindFirstObjectByType<CctvRevealTrigger>();
        if (trig != null)
            return trig.transform.position;
        if (Camera.main != null)
            return Camera.main.transform.position + Camera.main.transform.forward * 2f;
        return transform.position;
    }

    private void PlayDoorBehindPlayer()
    {
        if (doorOpenBehindPlayerClip == null) return;
        Camera cam = Camera.main;
        if (cam == null) return;

        Vector3 pos = cam.transform.position - cam.transform.forward * Mathf.Max(0.1f, doorBehindDistance);
        var go = new GameObject("Logbook_DoorBehindAudio");
        go.transform.position = pos;

        var src = go.AddComponent<AudioSource>();
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Logarithmic;
        src.minDistance = 0.5f;
        src.maxDistance = 10f;
        src.dopplerLevel = 0f;

        src.PlayOneShot(doorOpenBehindPlayerClip, doorBehindVolume);
        Destroy(go, doorOpenBehindPlayerClip.length + 0.25f);
    }

    private static bool CountsTowardSpawn(LogbookAction action)
    {
        // Anything "normal room activity" should be able to surface the book,
        // otherwise players can do a lot and see nothing.
        return action == LogbookAction.PizzaAteSlice
               || action == LogbookAction.FridgeOpened
               || action == LogbookAction.FridgeItemPlaced
               || action == LogbookAction.PhoneBasicUsed
               || action == LogbookAction.MicrowaveOpened
               || action == LogbookAction.WindowOpened
               || action == LogbookAction.WindowClosed
               || action == LogbookAction.ChairSat;
    }

    private string BuildPizzaSliceEntry()
    {
        if (_lastPizzaSlicesLeft >= 0 && _lastPizzaTotalSlices > 0)
        {
            int eaten = Mathf.Clamp(_lastPizzaTotalSlices - _lastPizzaSlicesLeft, 0, _lastPizzaTotalSlices);
            return $"Subject ate one slice of pizza.\nPizza status: {_lastPizzaSlicesLeft}/{_lastPizzaTotalSlices} slices left (eaten {eaten}).\nIt tastes fine. That’s the problem—too fine.";
        }
        return "Subject ate one slice of pizza.\nIt tastes fine. That’s the problem—too fine.";
    }

    private void BackfillHistoryIntoBook()
    {
        // We already added ENTRY 01: entered room.
        // Backfill whatever the player did before the book existed.
        for (int i = 0; i < _actionHistory.Count; i++)
        {
            AppendActionToBook(_actionHistory[i]);
        }
    }

    private void AppendActionToBook(LogbookAction action)
    {
        switch (action)
        {
            case LogbookAction.PhoneBasicUsed:
                AddEntry("Subject used the phone.\nI half-expect it to be warm from someone else’s hand.");
                break;
            case LogbookAction.PizzaAteSlice:
                AddEntry(BuildPizzaSliceEntry());
                break;
            case LogbookAction.FridgeOpened:
                AddEntry("Subject opened the fridge.\nCold air rolls out like a warning.");
                break;
            case LogbookAction.FridgeItemPlaced:
                AddEntry($"Subject placed an item in the fridge.\nItem: {(_lastPlacedItemName ?? "Unknown")}\nWhy does it feel like I’m putting it back for someone?");
                RegisterTestActionIfRelevant();
                break;
            case LogbookAction.MicrowaveOpened:
                AddEntry("Subject opened the microwave.\nThe inside looks too clean—unused, or reset.");
                RegisterTestActionIfRelevant();
                break;
            case LogbookAction.WindowOpened:
                AddEntry("Subject opened the window.\nThe air outside doesn’t smell like outside.");
                RegisterTestActionIfRelevant();
                break;
            case LogbookAction.WindowClosed:
                AddEntry("Subject closed the window.\nThe room feels smaller the moment it shuts.");
                RegisterTestActionIfRelevant();
                break;
            case LogbookAction.ChairSat:
                AddEntry("Subject sat in the chair.\nFor a second, the room stops pretending it’s normal.");
                RegisterTestActionIfRelevant();
                break;
            case LogbookAction.PhoneCallAttempt:
                if (!_phoneCallLogged) _phoneCallLogged = true;
                AddEntry("Subject attempted to contact the outside.\nConnection blocked.\nThe dial tone feels staged.");
                break;
            case LogbookAction.PhoneTextAttempt:
                if (!_phoneTextLogged) _phoneTextLogged = true;
                AddEntry("Subject attempted written communication.\nMessage intercepted.\nMy words vanish like I never typed them.");
                break;
        }
    }

    private void AddEntry(string body)
    {
        _entryCounter++;
        string n = _entryCounter.ToString("D2");
        _entries.Add($"ENTRY {n}:\n{body}");
    }

    private void AddNonEntryLine(string line)
    {
        _entries.Add(line);
    }

    private void AddFinalBlock(params string[] lines)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            sb.Append(lines[i]);
            if (i < lines.Length - 1)
                sb.Append('\n');
        }
        _entries.Add(sb.ToString());
    }

    private void UpdateBookPages()
    {
        if (book == null) return;

        book.pages = BuildPagedLog();
    }

    private string[] BuildPagedLog()
    {
        int limit = Mathf.Clamp(maxCharsPerPage, 200, 5000);

        var pages = new List<string>();
        var sb = new StringBuilder();

        void FlushPage()
        {
            if (sb.Length == 0) return;
            pages.Add(sb.ToString());
            sb.Clear();
        }

        for (int i = 0; i < _entries.Count; i++)
        {
            string block = _entries[i] ?? "";
            string withSpacing = (sb.Length == 0) ? block : ("\n\n" + block);

            // If adding this would exceed the page limit, start a new page first.
            if (sb.Length > 0 && sb.Length + withSpacing.Length > limit)
            {
                FlushPage();
                withSpacing = block;
            }

            // If a single block itself is huge, split it across pages.
            if (withSpacing.Length > limit && withSpacing.Length > 0)
            {
                // Add as much as fits, then continue.
                int start = 0;
                while (start < withSpacing.Length)
                {
                    int remaining = withSpacing.Length - start;
                    int take = Mathf.Min(limit - sb.Length, remaining);
                    if (take <= 0)
                    {
                        FlushPage();
                        continue;
                    }

                    sb.Append(withSpacing, start, take);
                    start += take;

                    if (sb.Length >= limit)
                        FlushPage();
                }
            }
            else
            {
                sb.Append(withSpacing);
            }
        }

        FlushPage();
        if (pages.Count == 0)
            pages.Add("");

        return pages.ToArray();
    }
}

