using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BookController : MonoBehaviour
{
    BookWorldVisual _bookVisual;
    FPSController _fpsController;
    [TextArea(3, 10)]
    public string[] pages;          // Fill these in Inspector
    public TMP_Text pageText;       // Drag PageText here (front page layer)
    [Tooltip("Optional back layer for two-page swipe; auto-created with default UI.")]
    public TMP_Text pageTextBack;
    public TMP_Text pageNumberText; // Drag PageNumber here
    public Button nextButton;
    public Button prevButton;
    public Button closeButton;
    public GameObject bookUI;       // Drag BookCanvas here (optional if createDefaultWorldUIIfMissing is on)
    [Tooltip("Max raycast distance from the camera for the E-key interaction (book can be several meters from spawn).")]
    public float interactDist = 8f;
    public Animator pageAnimator;   // optional

    [Tooltip("If Book UI fields are empty, builds a minimal World Space canvas at Play. It is not saved in the scene: you only see BookCanvas under Book while playing, and it disappears when you stop Play.")]
    public bool createDefaultWorldUIIfMissing = true;

    [Header("World Space UI (placement & facing)")]
    [Tooltip("Empty GameObject, child of the book, that defines where the BookCanvas sits and (for AlignToAnchor) how it is tilted.")]
    public Transform uiAnchor;
    [Tooltip("Offset from uiAnchor in the anchor's local space (slightly above the page surface).")]
    public Vector3 uiOffset = new Vector3(0f, 0.05f, 0.02f);

    public enum UIFacingMode
    {
        AlignToAnchor,
        YawTowardCamera,
        FaceCamera
    }

    [Tooltip("How BookCanvas rotation is chosen each frame while the book is open. YawTowardCamera keeps the page on the book while turning to face you.")]
    public UIFacingMode uiFacingMode = UIFacingMode.FaceCamera;

    [Header("Panel placement while open")]
    [Tooltip("0 = canvas stays at the book (full scene + book visible). 1 = pull toward camera (reading zoom).")]
    [Range(0f, 1f)]
    public float pullOpenUIInFrontOfCamera = 1f;
    [Tooltip("How far in front of the camera when pull is 1.")]
    public float openUIDistanceFromCamera = 0.65f;
    [Tooltip("Slight scale boost while open; keep near 1 for a natural in-world book.")]
    public float openCanvasExtraScale = 1.2f;

    [Header("Page turn / swipe")]
    [Tooltip("Animated page swipe between spreads (assignment: page-turn / page-swipe transition).")]
    public bool usePageSlideAnimation = true;
    [Tooltip("Total time for one page swipe (outgoing + incoming).")]
    public float pageTurnDuration = 0.6f;
    [Tooltip("Outgoing page tilts on Z like a hinge during the swipe.")]
    public float pageSwipeHingeDegrees = 6f;
    [Tooltip("Subtle horizontal squash at mid-swipe (paper bend). 0.08 ≈ 8% narrower at center.")]
    [Range(0f, 0.2f)]
    public float pageSwipePressAmount = 0.07f;

    [Header("Audio")]
    [Tooltip("Sound played when a page is turned (next/prev).")]
    public AudioClip pageTurnSound;
    [Tooltip("Sound played when the book is opened.")]
    public AudioClip bookOpenSound;
    [Tooltip("Sound played when the book is closed.")]
    public AudioClip bookCloseSound;
    [Tooltip("Volume for book audio effects.")]
    [Range(0f, 1f)]
    public float audioVolume = 0.8f;

    private AudioSource _bookAudio;

    [Header("PDF import (bonus)")]
    [Tooltip("On Start, replace the Pages array with text extracted from a PDF (one string per PDF page).")]
    public bool loadPdfOnStart;
    [Tooltip("File name inside the StreamingAssets folder (e.g. BookContent.pdf).")]
    public string pdfFileInStreamingAssets = "BookContent.pdf";
    [Tooltip("If set, this path is used instead of StreamingAssets (full path to .pdf).")]
    public string pdfAbsolutePath = "";

    [Tooltip("If text is mirrored, toggle this (or fix Canvas scale: avoid negative X/Y on the Canvas root).")]
    public bool flipCanvasLocalX;

    [Header("Page text (keep inside the page panel)")]
    [Tooltip("Apply wrapping / overflow / auto-size so text does not spill past the PageText rectangle.")]
    public bool configurePageTextLayout = true;
    [Tooltip("Shrink font to fit the PageText rect (set min/max; disable to use fixed Font Size from Inspector).")]
    public bool pageTextAutoSize = true;
    public float pageTextFontSizeMin = 26f;
    public float pageTextFontSizeMax = 72f;
    [Tooltip("How TMP handles content larger than the rect. Overflow shows full text; Truncate clips at edges.")]
    public TextOverflowModes pageTextOverflow = TextOverflowModes.Overflow;

    [Header("Canvas size & alignment (world space)")]
    [Tooltip("Virtual canvas width in UI units (like pixels). Physical width still comes from canvasWorldDimensions / pageFaceCollider. Larger = sharper TextMesh Pro.")]
    public float canvasVirtualWidth = 640f;
    [Tooltip("Set the BookCanvas root RectTransform width/height in world units so it matches the physical page (the pink gizmo should match the book mesh).")]
    public bool setCanvasWorldSize = true;
    [Tooltip("Physical size of the page panel on the open book (meters).")]
    public Vector2 canvasWorldDimensions = new Vector2(1.05f, 0.58f);
    [Tooltip("Optional: a BoxCollider on the page face — width/height are taken from its scaled size so the canvas matches the mesh.")]
    public BoxCollider pageFaceCollider;
    public enum PageColliderAxes
    {
        LocalXY,
        LocalXZ
    }
    [Tooltip("Which collider local axes map to canvas width (X) and height (Y).")]
    public PageColliderAxes pageColliderAxes = PageColliderAxes.LocalXY;

    [Tooltip("Stretch PageText to fill the canvas with this padding (left, right, top, bottom).")]
    public bool layoutPageTextStretch = true;
    public int pageTextPaddingLeft = 12;
    public int pageTextPaddingRight = 12;
    public int pageTextPaddingTop = 52;
    public int pageTextPaddingBottom = 108;

    private int currentPage = 0;
    private bool isOpen = false;
    private Vector3 _bookUILocalScale = Vector3.one;
    bool _pageTurnBusy;
    Coroutine _pageTurnRoutine;

    void Awake()
    {
        ResolveUiAnchor();
        _bookVisual = GetComponent<BookWorldVisual>();
        _bookAudio = gameObject.AddComponent<AudioSource>();
        _bookAudio.spatialBlend = 0f;   // 2D — book UI is screen-space when open
        _bookAudio.playOnAwake = false;
        _bookAudio.volume = audioVolume;
    }

    void ResolveUiAnchor()
    {
        if (uiAnchor != null)
            return;
        Transform found = transform.Find("UIAnchor");
        if (found != null)
            uiAnchor = found;
    }

    void Start()
    {
        ResolveUiAnchor();
        if (uiAnchor == null)
            Debug.LogWarning("BookController: add an empty child named \"UIAnchor\" (or assign uiAnchor) so the canvas follows the book.", this);

        if ((bookUI == null || pageText == null || pageNumberText == null || nextButton == null || prevButton == null || closeButton == null)
            && createDefaultWorldUIIfMissing)
            BuildDefaultWorldBookUI();

        if (bookUI == null)
        {
            Debug.LogError("BookController: assign bookUI (BookCanvas) or enable createDefaultWorldUIIfMissing.", this);
            return;
        }

        ApplyCanvasWorldSizeAndLayout();
        ApplyPageTextStretchLayout();
        ApplyPageTextLayout();
        bookUI.SetActive(false);
        _bookUILocalScale = bookUI.transform.localScale;

        if (nextButton != null)
            nextButton.onClick.AddListener(NextPage);
        if (prevButton != null)
            prevButton.onClick.AddListener(PrevPage);
        if (closeButton != null)
            closeButton.onClick.AddListener(CloseBook);

        TryLoadPdfOnStart();

        if (pages == null || pages.Length == 0)
            pages = DefaultStoryPages();

        _fpsController = FindObjectOfType<FPSController>();
    }

    void TryLoadPdfOnStart()
    {
        if (!loadPdfOnStart)
            return;

        string path = !string.IsNullOrWhiteSpace(pdfAbsolutePath)
            ? pdfAbsolutePath
            : BookPdfImporter.StreamingAssetsPath(pdfFileInStreamingAssets);

        if (BookPdfImporter.TryLoadPages(path, out string[] pdfPages, out string err))
            pages = pdfPages;
        else
            Debug.LogWarning("BookController: could not load PDF — " + err + " Falling back to Inspector pages or default story.", this);
    }

    /// <summary>Bonus: reload <see cref="pages"/> from the configured PDF while playing.</summary>
    public void ReloadPagesFromPdf()
    {
        string path = !string.IsNullOrWhiteSpace(pdfAbsolutePath)
            ? pdfAbsolutePath
            : BookPdfImporter.StreamingAssetsPath(pdfFileInStreamingAssets);

        if (!BookPdfImporter.TryLoadPages(path, out string[] pdfPages, out string err))
        {
            Debug.LogWarning("BookController: PDF reload failed — " + err, this);
            return;
        }

        pages = pdfPages;
        currentPage = Mathf.Clamp(currentPage, 0, Mathf.Max(0, pages.Length - 1));
        if (isOpen)
            DisplayPageInstant();
    }

    void BuildDefaultWorldBookUI()
    {
        TMP_FontAsset font = TMP_Settings.defaultFontAsset
            ?? Resources.Load<TMP_FontAsset>("Fonts & Materials/LiberationSans SDF");

        Sprite uiSprite = WhiteUISprite();

        var canvasGo = new GameObject("BookCanvas");
        canvasGo.transform.SetParent(transform, false);
        var canvasRt = canvasGo.AddComponent<RectTransform>();
        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = Camera.main;
        canvas.sortingOrder = 100;
        canvasGo.AddComponent<GraphicRaycaster>();

        canvasRt.localScale = Vector3.one;

        var panel = CreateUiRect("BookBackground", canvasRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var panelImage = panel.gameObject.AddComponent<Image>();
        panelImage.sprite = uiSprite;
        panelImage.color = new Color(0.98f, 0.96f, 0.90f, 1f);   // solid cream — max contrast

        closeButton = CreateTextButton("CloseBtn", canvasRt, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -16f), new Vector2(72f, 72f), "X", font, uiSprite);

        var viewport = CreateUiRect("PageViewport", canvasRt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        viewport.offsetMin = new Vector2(18f, 118f);
        viewport.offsetMax = new Vector2(-18f, -58f);
        viewport.gameObject.AddComponent<RectMask2D>();

        var backRt = CreateUiRect("PageTextBack", viewport, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        pageTextBack = backRt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultPageTmpStyle(pageTextBack, font);
        var backCg = backRt.gameObject.AddComponent<CanvasGroup>();
        backCg.alpha = 0f;
        backCg.blocksRaycasts = false;
        backCg.interactable = false;

        var pageTextRt = CreateUiRect("PageText", viewport, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        pageText = pageTextRt.gameObject.AddComponent<TextMeshProUGUI>();
        ApplyDefaultPageTmpStyle(pageText, font);
        var frontCg = pageTextRt.gameObject.AddComponent<CanvasGroup>();
        frontCg.blocksRaycasts = false;
        frontCg.interactable = false;

        var pageNumRt = CreateUiRect("PageNumber", canvasRt, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 86f), new Vector2(360f, 40f));
        pageNumberText = pageNumRt.gameObject.AddComponent<TextMeshProUGUI>();
        pageNumberText.text = "Page 1 of 1";
        pageNumberText.fontSize = 22;
        pageNumberText.alignment = TextAlignmentOptions.Center;
        pageNumberText.color = new Color(0.2f, 0.16f, 0.12f, 1f);
        if (font != null)
            pageNumberText.font = font;

        prevButton = CreateTextButton("PrevBtn", canvasRt, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 22f), new Vector2(120f, 64f), "<", font, uiSprite);
        nextButton = CreateTextButton("NextBtn", canvasRt, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 22f), new Vector2(120f, 64f), ">", font, uiSprite);

        bookUI = canvasGo;
    }

    static Sprite WhiteUISprite()
    {
        var tex = Texture2D.whiteTexture;
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
    }

    static RectTransform CreateUiRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta)
    {
        var go = new GameObject(name);
        var rt = go.AddComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.pivot = pivot;
        rt.anchoredPosition = anchoredPosition;
        rt.sizeDelta = sizeDelta;
        return rt;
    }

    static Button CreateTextButton(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 sizeDelta, string label, TMP_FontAsset font, Sprite uiSprite)
    {
        var rt = CreateUiRect(name, parent, anchorMin, anchorMax, pivot, anchoredPosition, sizeDelta);
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = uiSprite;
        img.color = new Color(0.38f, 0.34f, 0.3f, 1f);
        img.raycastTarget = true;
        var btn = rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = img;

        var textRt = CreateUiRect("Text", rt, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        var tmp = textRt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = label;
        tmp.fontSize = 34;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color = new Color(0.98f, 0.96f, 0.92f, 1f);
        tmp.raycastTarget = false;
        if (font != null)
            tmp.font = font;

        return btn;
    }

    static void ApplyDefaultPageTmpStyle(TMP_Text tmp, TMP_FontAsset font)
    {
        tmp.text = "";
        tmp.fontSize = 28;
        tmp.alignment = TextAlignmentOptions.TopJustified;
        // Rich dark-brown on cream — high contrast, easy on the eyes
        tmp.color = new Color(0.12f, 0.08f, 0.05f, 1f);
        if (font != null)
            tmp.font = font;
    }

    static Quaternion YawRotationTowardCamera(Transform anchor, Camera cam, Vector3 uiWorldPosition)
    {
        Vector3 up = anchor.up;
        if (up.sqrMagnitude < 1e-8f)
            up = Vector3.up;

        Vector3 toCam = cam.transform.position - uiWorldPosition;
        Vector3 forward = Vector3.ProjectOnPlane(toCam, up);
        if (forward.sqrMagnitude < 1e-6f)
            forward = Vector3.ProjectOnPlane(cam.transform.forward, up);
        if (forward.sqrMagnitude < 1e-6f)
            return anchor.rotation;

        forward.Normalize();
        return Quaternion.LookRotation(forward, up);
    }

    void ApplyCanvasWorldSizeAndLayout()
    {
        if (bookUI == null || !setCanvasWorldSize)
            return;

        RectTransform canvasRt = bookUI.GetComponent<RectTransform>();
        if (canvasRt == null)
            return;

        float physW = canvasWorldDimensions.x;
        float physH = canvasWorldDimensions.y;

        if (pageFaceCollider != null)
        {
            Vector3 s = Vector3.Scale(pageFaceCollider.size, pageFaceCollider.transform.lossyScale);
            switch (pageColliderAxes)
            {
                case PageColliderAxes.LocalXZ:
                    physW = Mathf.Abs(s.x);
                    physH = Mathf.Abs(s.z);
                    break;
                default:
                    physW = Mathf.Abs(s.x);
                    physH = Mathf.Abs(s.y);
                    break;
            }
        }

        float refW = Mathf.Max(64f, canvasVirtualWidth);
        float uniform = physW / refW;
        float refH = physH / uniform;

        canvasRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, refW);
        canvasRt.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, refH);
        bookUI.transform.localScale = new Vector3(uniform, uniform, uniform);
    }

    void ApplyPageTextStretchLayout()
    {
        if (!layoutPageTextStretch || pageText == null)
            return;

        if (pageTextBack != null && pageText.rectTransform.parent == pageTextBack.rectTransform.parent)
        {
            foreach (TMP_Text t in new[] { pageTextBack, pageText })
            {
                RectTransform prt = t.rectTransform;
                prt.anchorMin = Vector2.zero;
                prt.anchorMax = Vector2.one;
                prt.pivot = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = Vector2.zero;
                prt.sizeDelta = Vector2.zero;
                prt.offsetMin = Vector2.zero;
                prt.offsetMax = Vector2.zero;
            }

            Transform vp = pageText.rectTransform.parent;
            var vprt = vp as RectTransform;
            if (vprt != null)
            {
                vprt.offsetMin = new Vector2(pageTextPaddingLeft, pageTextPaddingBottom);
                vprt.offsetMax = new Vector2(-pageTextPaddingRight, -pageTextPaddingTop);
            }

            return;
        }

        RectTransform single = pageText.rectTransform;
        single.anchorMin = Vector2.zero;
        single.anchorMax = Vector2.one;
        single.pivot = new Vector2(0.5f, 0.5f);
        single.anchoredPosition = Vector2.zero;
        single.sizeDelta = Vector2.zero;
        single.offsetMin = new Vector2(pageTextPaddingLeft, pageTextPaddingBottom);
        single.offsetMax = new Vector2(-pageTextPaddingRight, -pageTextPaddingTop);
    }

    void ApplyPageTextLayout()
    {
        if (!configurePageTextLayout || pageText == null)
            return;

        foreach (TMP_Text t in EnumeratePageTextLayers())
        {
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = pageTextOverflow;
            t.enableAutoSizing = pageTextAutoSize;
            if (pageTextAutoSize)
            {
                t.fontSizeMin = pageTextFontSizeMin;
                t.fontSizeMax = pageTextFontSizeMax;
            }
        }
    }

    IEnumerable<TMP_Text> EnumeratePageTextLayers()
    {
        if (pageText != null)
            yield return pageText;
        if (pageTextBack != null)
            yield return pageTextBack;
    }

    void LateUpdate()
    {
        if (!isOpen || bookUI == null || !bookUI.activeInHierarchy)
            return;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Transform ui = bookUI.transform;
        Transform anchor = uiAnchor != null ? uiAnchor : transform;

        Vector3 anchorWorld = anchor.position + anchor.TransformDirection(uiOffset);
        Vector3 inFront = cam.transform.position + cam.transform.forward * Mathf.Max(0.15f, openUIDistanceFromCamera);
        ui.position = Vector3.Lerp(anchorWorld, inFront, Mathf.Clamp01(pullOpenUIInFrontOfCamera));

        switch (uiFacingMode)
        {
            case UIFacingMode.FaceCamera:
                ui.rotation = cam.transform.rotation;
                break;
            case UIFacingMode.AlignToAnchor:
                ui.rotation = anchor.rotation;
                break;
            case UIFacingMode.YawTowardCamera:
                ui.rotation = YawRotationTowardCamera(anchor, cam, ui.position);
                break;
        }

        float sx = flipCanvasLocalX ? -Mathf.Abs(_bookUILocalScale.x) : Mathf.Abs(_bookUILocalScale.x);
        ui.localScale = new Vector3(sx, _bookUILocalScale.y, _bookUILocalScale.z);
    }

    private bool _showHint = false;
    private GUIStyle _hintStyle;

    void OnGUI()
    {
        if (_hintStyle == null)
            _hintStyle = BuildHintStyle();

        if (isOpen)
        {
            GUI.Label(
                new Rect(Screen.width * 0.5f - 220f, Screen.height - 52f, 440f, 40f),
                "<b>[← →] or [< >] buttons</b>  Navigate pages    [Esc] Close book",
                _hintStyle);
            return;
        }

        if (!_showHint) return;
        GUI.Label(
            new Rect(Screen.width * 0.5f - 150f, Screen.height * 0.72f, 300f, 50f),
            "<b>Book</b>\n[E] Read",
            _hintStyle);
    }

    static GUIStyle BuildHintStyle()
    {
        var s = new GUIStyle(GUI.skin.label);
        s.fontSize  = 16;
        s.alignment = TextAnchor.MiddleCenter;
        s.normal.textColor = Color.white;
        s.richText  = true;
        return s;
    }

    void Update()
    {
        if (isOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                CloseBook();

            if (_pageTurnBusy)
                return;

            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.PageUp))
                PrevPage();
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.PageDown))
                NextPage();
            return;
        }

        _showHint = false;

        Camera cam = Camera.main;
        if (cam == null)
            return;

        Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
        if (!Physics.Raycast(ray, out RaycastHit hit, interactDist, ~0, QueryTriggerInteraction.Collide))
            return;

        if (hit.collider != null && hit.collider.gameObject == gameObject)
        {
            _showHint = true;
            if (Input.GetKeyDown(KeyCode.E))
                OpenBook();
        }
    }

    void OpenBook()
    {
        if (bookUI == null)
            return;

        isOpen = true;
        currentPage = 0;
        var cv = bookUI.GetComponent<Canvas>();
        if (cv != null && cv.worldCamera == null)
            cv.worldCamera = Camera.main;

        ApplyCanvasWorldSizeAndLayout();
        ApplyPageTextStretchLayout();
        ApplyPageTextLayout();

        float extra = Mathf.Max(0.25f, openCanvasExtraScale);
        Vector3 baseScale = bookUI.transform.localScale;
        _bookUILocalScale = new Vector3(baseScale.x * extra, baseScale.y * extra, baseScale.z * extra);

        if (cv != null)
            cv.sortingOrder = 100;

        bookUI.SetActive(true);
        ResetPageTurnVisuals();
        DisplayPageInstant();

        _bookVisual?.SetMagicParticlesPlaying(false);
        _fpsController?.SetMovementAndLookEnabled(false);

        if (_bookAudio != null && bookOpenSound != null)
            _bookAudio.PlayOneShot(bookOpenSound, audioVolume);

        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    public void CloseBook()
    {
        isOpen = false;
        if (_pageTurnRoutine != null)
        {
            StopCoroutine(_pageTurnRoutine);
            _pageTurnRoutine = null;
        }
        _pageTurnBusy = false;

        if (bookUI != null)
        {
            ResetPageTurnVisuals();
            bookUI.SetActive(false);
        }

        _bookVisual?.SetMagicParticlesPlaying(true);
        _fpsController?.SetMovementAndLookEnabled(true);

        if (_bookAudio != null && bookCloseSound != null)
            _bookAudio.PlayOneShot(bookCloseSound, audioVolume);

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void DisplayPageInstant()
    {
        if (pages == null || pages.Length == 0 || pageText == null)
            return;

        ApplyPageContent();
    }

    void ApplyPageContent()
    {
        pageText.text = pages[currentPage];
        if (pageTextBack != null)
        {
            MatchPageTextFormat(pageTextBack, pageText);
            pageTextBack.text = pages[currentPage];
            CanvasGroup cg = pageTextBack.GetComponent<CanvasGroup>();
            if (cg != null)
                cg.alpha = 0f;
        }

        if (pageNumberText != null)
            pageNumberText.text = "Page " + (currentPage + 1) + " of " + pages.Length;
        if (prevButton != null)
            prevButton.interactable = (currentPage > 0) && !_pageTurnBusy;
        if (nextButton != null)
            nextButton.interactable = (currentPage < pages.Length - 1) && !_pageTurnBusy;
        if (closeButton != null)
            closeButton.interactable = true;
    }

    static void MatchPageTextFormat(TMP_Text dest, TMP_Text src)
    {
        dest.font = src.font;
        dest.fontSize = src.fontSize;
        dest.fontStyle = src.fontStyle;
        dest.color = src.color;
        dest.alignment = src.alignment;
        dest.enableAutoSizing = src.enableAutoSizing;
        dest.fontSizeMin = src.fontSizeMin;
        dest.fontSizeMax = src.fontSizeMax;
        dest.textWrappingMode = src.textWrappingMode;
        dest.overflowMode = src.overflowMode;
    }

    void ResetPageTurnVisuals()
    {
        foreach (TMP_Text t in EnumeratePageTextLayers())
        {
            RectTransform rt = t.rectTransform;
            rt.anchoredPosition = new Vector2(0f, rt.anchoredPosition.y);
            rt.localEulerAngles = Vector3.zero;
            rt.localScale = Vector3.one;
            CanvasGroup cg = t.GetComponent<CanvasGroup>();
            if (cg != null)
                cg.alpha = t == pageTextBack ? 0f : 1f;
        }
    }

    bool UseDualLayerSwipe()
    {
        return pageTextBack != null && pageText != null && pageTextBack.gameObject.activeInHierarchy;
    }

    void SetNavigationLocked(bool locked)
    {
        if (prevButton != null)
            prevButton.interactable = !locked && currentPage > 0;
        if (nextButton != null)
            nextButton.interactable = !locked && currentPage < pages.Length - 1;
        if (closeButton != null)
            closeButton.interactable = true;
    }

    public void NextPage()
    {
        if (_pageTurnBusy || pages == null || currentPage >= pages.Length - 1)
            return;
        PlayPageTurnSound();
        if (usePageSlideAnimation)
        {
            if (_pageTurnRoutine != null)
                StopCoroutine(_pageTurnRoutine);
            _pageTurnRoutine = StartCoroutine(PageTurnSwipeRoutine(1));
        }
        else
        {
            currentPage++;
            if (pageAnimator)
                pageAnimator.SetTrigger("PageTurn");
            ApplyPageContent();
        }
    }

    public void PrevPage()
    {
        if (_pageTurnBusy || currentPage <= 0)
            return;
        PlayPageTurnSound();
        if (usePageSlideAnimation)
        {
            if (_pageTurnRoutine != null)
                StopCoroutine(_pageTurnRoutine);
            _pageTurnRoutine = StartCoroutine(PageTurnSwipeRoutine(-1));
        }
        else
        {
            currentPage--;
            if (pageAnimator)
                pageAnimator.SetTrigger("PageTurn");
            ApplyPageContent();
        }
    }

    /// <summary>Plays the page-turn sound if assigned.</summary>
    void PlayPageTurnSound()
    {
        if (_bookAudio != null && pageTurnSound != null)
            _bookAudio.PlayOneShot(pageTurnSound, audioVolume);
    }

    IEnumerator PageTurnSwipeRoutine(int direction)
    {
        _pageTurnBusy = true;
        SetNavigationLocked(true);
        yield return null;

        if (UseDualLayerSwipe())
            yield return PageTurnSwipeDualLayer(direction);
        else
            yield return PageTurnSwipeSingleLayer(direction);

        if (pageAnimator)
            pageAnimator.SetTrigger("PageTurn");

        _pageTurnBusy = false;
        _pageTurnRoutine = null;
        ApplyPageContent();
    }

    IEnumerator PageTurnSwipeDualLayer(int direction)
    {
        int nextIdx = currentPage + direction;
        RectTransform frontRt = pageText.rectTransform;
        RectTransform backRt = pageTextBack.rectTransform;
        float W = GetPageSwipeDistance(frontRt, 0.92f);
        float dur = Mathf.Max(0.18f, pageTurnDuration);
        float hinge = direction > 0 ? -pageSwipeHingeDegrees : pageSwipeHingeDegrees;

        MatchPageTextFormat(pageTextBack, pageText);
        pageTextBack.text = pages[nextIdx];

        CanvasGroup frontCg = pageText.GetComponent<CanvasGroup>();
        CanvasGroup backCg = pageTextBack.GetComponent<CanvasGroup>();
        if (frontCg != null)
            frontCg.alpha = 1f;
        if (backCg != null)
            backCg.alpha = 1f;

        float py = frontRt.anchoredPosition.y;
        float startBackX = direction > 0 ? W : -W;
        float endFrontX = direction > 0 ? -W : W;

        frontRt.anchoredPosition = new Vector2(0f, py);
        backRt.anchoredPosition = new Vector2(startBackX, py);
        frontRt.localEulerAngles = Vector3.zero;
        backRt.localEulerAngles = Vector3.zero;
        frontRt.localScale = Vector3.one;
        backRt.localScale = Vector3.one;

        float t = 0f;
        while (t < dur)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / dur);
            float e = EaseInOutQuart(u);

            frontRt.anchoredPosition = new Vector2(Mathf.Lerp(0f, endFrontX, e), py);
            backRt.anchoredPosition = new Vector2(Mathf.Lerp(startBackX, 0f, e), py);

            float hingeU = EaseOutCubic(Mathf.Clamp01(u * 1.2f));
            frontRt.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(0f, hinge, hingeU));
            float press = pageSwipePressAmount * Mathf.Sin(u * Mathf.PI);
            frontRt.localScale = new Vector3(1f - press, 1f, 1f);

            yield return null;
        }

        frontRt.anchoredPosition = new Vector2(endFrontX, py);
        backRt.anchoredPosition = new Vector2(0f, py);

        currentPage = nextIdx;
        pageText.text = pages[currentPage];
        ResetPageTurnVisuals();
    }

    IEnumerator PageTurnSwipeSingleLayer(int direction)
    {
        RectTransform rt = pageText.rectTransform;
        float W = GetPageSwipeDistance(rt, 0.88f);
        float half = Mathf.Max(0.1f, pageTurnDuration * 0.5f);
        float hinge = direction > 0 ? -pageSwipeHingeDegrees : pageSwipeHingeDegrees;
        float outEnd = direction > 0 ? -W : W;

        yield return PageSwipeSegment(rt, 0f, outEnd, half, 0f, hinge, true);

        currentPage += direction;
        ApplyPageContent();

        float inStart = direction > 0 ? W : -W;
        rt.anchoredPosition = new Vector2(inStart, rt.anchoredPosition.y);
        rt.localEulerAngles = new Vector3(0f, 0f, hinge);
        rt.localScale = Vector3.one;
        yield return PageSwipeSegment(rt, inStart, 0f, half, hinge, 0f, false);
    }

    IEnumerator PageSwipeSegment(RectTransform rt, float fromX, float toX, float duration, float hingeFrom, float hingeTo, bool applyPress)
    {
        float y = rt.anchoredPosition.y;
        if (duration < 1e-4f)
        {
            rt.anchoredPosition = new Vector2(toX, y);
            rt.localEulerAngles = new Vector3(0f, 0f, hingeTo);
            rt.localScale = Vector3.one;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.Clamp01(t / duration);
            float e = EaseInOutQuart(u);
            rt.anchoredPosition = new Vector2(Mathf.Lerp(fromX, toX, e), y);
            rt.localEulerAngles = new Vector3(0f, 0f, Mathf.Lerp(hingeFrom, hingeTo, e));
            if (applyPress)
            {
                float press = pageSwipePressAmount * Mathf.Sin(u * Mathf.PI);
                rt.localScale = new Vector3(1f - press, 1f, 1f);
            }
            else
                rt.localScale = Vector3.one;
            yield return null;
        }

        rt.anchoredPosition = new Vector2(toX, y);
        rt.localEulerAngles = new Vector3(0f, 0f, hingeTo);
        rt.localScale = Vector3.one;
    }

    void ForceBookUILayout()
    {
        if (bookUI != null)
        {
            var rootRt = bookUI.GetComponent<RectTransform>();
            if (rootRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRt);
        }
        Canvas.ForceUpdateCanvases();
    }

    float GetPageSwipeDistance(RectTransform pageRt, float widthScale)
    {
        ForceBookUILayout();
        float w = pageRt.rect.width;
        if (w < 16f)
        {
            var parentRt = pageRt.parent as RectTransform;
            if (parentRt != null)
                w = parentRt.rect.width + pageRt.offsetMin.x + pageRt.offsetMax.x;
        }
        if (w < 16f)
            w = Mathf.Max(16f, canvasVirtualWidth - pageTextPaddingLeft - pageTextPaddingRight);
        return Mathf.Max(180f, w * widthScale);
    }

    static float EaseInOutQuart(float t)
    {
        return t < 0.5f ? 8f * t * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 4f) / 2f;
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - Mathf.Pow(1f - t, 3f);
    }

    static string[] DefaultStoryPages()
    {
        return new[]
        {
            "The Star Atlas of Elara\n\nIn the oldest wing of the city library, past the maps that no longer matched any coast, there was a single desk where dust fell in straight lines, as if the air itself were holding its breath.",
            "Mira found the volume wedged behind a ledger she had been told not to move. Its cover was the deep blue of a winter hour before dawn, and along the edge, thin bands of gold caught the lamp like threads of sunlight underwater.",
            "The first chart she opened was not of land at all. Constellations wheeled across the paper in ink that seemed still wet—names in a script she almost recognized, as though she had dreamed the alphabet once and forgotten it on waking.",
            "She turned the page. The stars rearranged themselves. A river of light ran between two dark patches she could have sworn were the same empty sky a moment before. Her reflection in the polished wood looked briefly like someone older.",
            "Footsteps echoed in the corridor, then stopped. Mira did not look up. The book had begun to hum, very softly, like a held note through a wall of stone. The next spread showed no sky—only a coastline she knew from childhood holidays, but drawn with impossible precision.",
            "On the margin, in a hand like her own but steadier, someone had written: \"Every reader leaves a wake.\" The letters trembled when she breathed. Outside, rain began against the high windows, slow at first, then steady.",
            "She closed the atlas not with fear, but with care—as you would cover a sleeping child. The humming faded. On the last page, blank but for a single line, it read: \"Return when the rain writes your name on the glass.\" She smiled, and for a long time only listened."
        };
    }
}
