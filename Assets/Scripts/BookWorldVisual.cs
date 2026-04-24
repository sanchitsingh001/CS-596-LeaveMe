using UnityEngine;

/// <summary>
/// Replaces a single primitive "book" cube with layered cubes (covers, page blocks, spine)
/// and optional URP-style sparkle particles at the gutter, similar to an open enchanted book.
/// </summary>
[DisallowMultipleComponent]
public class BookWorldVisual : MonoBehaviour
{
    [Tooltip("Turn off the root MeshRenderer and build stacked primitives as children.")]
    public bool replacePrimitiveCube = true;

    [Tooltip("Cone burst of warm particles from the spine (requires URP Particles/Unlit or fallback).")]
    public bool addMagicParticles = true;

    [Header("Layout (book local space; parent scale still applies)")]
    [Tooltip("Horizontal distance from spine center to each page stack (wider = more open spread).")]
    public float pageHalfSpread = 0.34f;
    public float gutterHalfWidth = 0.05f;
    public float pageThickness = 0.045f;
    public float pageTiltDegrees = 12f;
    [Tooltip("Page slab width along the spread axis (local X).")]
    public float pageBlockWidth = 0.58f;

    ParticleSystem _magicBurst;

    static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    void Awake()
    {
        if (replacePrimitiveCube)
            BuildOpenBookMesh();
        if (addMagicParticles)
            BuildMagicBurst();
    }

    void BuildOpenBookMesh()
    {
        var rootMr = GetComponent<MeshRenderer>();
        if (rootMr != null)
            rootMr.enabled = false;

        var holder = new GameObject("BookVisual");
        holder.transform.SetParent(transform, false);

        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        if (lit == null)
            lit = Shader.Find("Standard");

        Material cover = CreateLit(lit, new Color(0.28f, 0.14f, 0.09f), 0.22f, 0f);
        Material page = CreateLit(lit, new Color(0.94f, 0.91f, 0.84f), 0.42f, 0f);
        Material spineMat = CreateLit(lit, new Color(0.18f, 0.1f, 0.07f), 0.18f, 0f);
        Material edge = CreateLit(lit, new Color(0.8f, 0.76f, 0.68f), 0.12f, 0f);

        float half = pageHalfSpread;
        float yPage = pageThickness * 0.45f;
        float edgeX = pageBlockWidth * 0.5f + 0.02f;

        CreateCube(holder.transform, "Spine", spineMat, new Vector3(0f, -0.02f, 0f), new Vector3(gutterHalfWidth * 2f, 0.09f, 0.94f));

        var left = CreateCube(holder.transform, "LeftPages", page, new Vector3(-half, yPage, 0f), new Vector3(pageBlockWidth, pageThickness, 0.88f));
        left.transform.localRotation = Quaternion.Euler(0f, 0f, pageTiltDegrees);

        var right = CreateCube(holder.transform, "RightPages", page, new Vector3(half, yPage, 0f), new Vector3(pageBlockWidth, pageThickness, 0.88f));
        right.transform.localRotation = Quaternion.Euler(0f, 0f, -pageTiltDegrees);

        CreateCube(holder.transform, "LeftPageEdge", edge, new Vector3(-half - edgeX, yPage, 0f), new Vector3(0.032f, pageThickness * 1.1f, 0.9f));
        CreateCube(holder.transform, "RightPageEdge", edge, new Vector3(half + edgeX, yPage, 0f), new Vector3(0.032f, pageThickness * 1.1f, 0.9f));

        float coverW = pageBlockWidth + 0.06f;
        float yCover = -pageThickness * 0.65f;
        CreateCube(holder.transform, "LeftCover", cover, new Vector3(-half, yCover, 0f), new Vector3(coverW, 0.04f, 0.96f));
        CreateCube(holder.transform, "RightCover", cover, new Vector3(half, yCover, 0f), new Vector3(coverW, 0.04f, 0.96f));
    }

    static GameObject CreateCube(Transform parent, string name, Material mat, Vector3 localPos, Vector3 localScale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = localScale;
        Destroy(go.GetComponent<Collider>());
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;
        return go;
    }

    static Material CreateLit(Shader shader, Color baseColor, float smoothness, float metallic)
    {
        var m = new Material(shader);
        m.SetColor(BaseColor, baseColor);
        if (m.HasProperty("_Smoothness"))
            m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic"))
            m.SetFloat("_Metallic", metallic);
        return m;
    }

    void BuildMagicBurst()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
            shader = Shader.Find("Particles/Standard Unlit");
        if (shader == null)
            return;

        var mat = new Material(shader);
        mat.SetColor(BaseColor, new Color(1f, 0.94f, 0.72f, 1f));

        var go = new GameObject("MagicBurst");
        go.transform.SetParent(transform, false);
        // Spawn from the book center, slightly above the page surface
        go.transform.localPosition = new Vector3(0f, 0.1f, 0f);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 2f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
        // Positive speed = upward (cone shape oriented to emit upward)
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.055f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.75f, 0.9f));
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        // Negative gravity = float upward continuously; -0.3 gives a gentle rising drift
        main.gravityModifier = -0.3f;
        main.maxParticles = 120;
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 360f);

        var em = ps.emission;
        em.rateOverTime = 36f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        // angle=18 keeps the fountain tight; rotation=0 means the cone already opens upward
        shape.angle = 18f;
        shape.radius = 0.25f;
        shape.arc = 360f;
        // No rotation override — default cone emits along the GameObject's +Y (up)
        // so particles naturally travel upward from the book's page surface
        shape.rotation = Vector3.zero;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 1f, 0.95f), 0f),
                new GradientColorKey(new Color(0.6f, 0.85f, 1f), 0.5f),
                new GradientColorKey(new Color(1f, 0.85f, 0.4f), 1f)
            },
            new[] {
                new GradientAlphaKey(0.9f, 0f),
                new GradientAlphaKey(0.6f, 0.5f),
                new GradientAlphaKey(0f, 1f)
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        var sz = ps.sizeOverLifetime;
        sz.enabled = true;
        AnimationCurve curve = new AnimationCurve(
            new Keyframe(0f, 0.3f),
            new Keyframe(0.2f, 1f),
            new Keyframe(1f, 0.05f));
        sz.size = new ParticleSystem.MinMaxCurve(1f, curve);

        var rnd = go.GetComponent<ParticleSystemRenderer>();
        rnd.renderMode = ParticleSystemRenderMode.Billboard;
        rnd.sharedMaterial = mat;

        _magicBurst = ps;
        ps.Play();
    }

    /// <summary>Hide gutter sparkles while reading (e.g. book open).</summary>
    public void SetMagicParticlesPlaying(bool play)
    {
        if (!addMagicParticles || _magicBurst == null)
            return;
        if (play)
            _magicBurst.Play();
        else
            _magicBurst.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }
}
