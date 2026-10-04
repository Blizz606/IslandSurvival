using UnityEngine;

[DisallowMultipleComponent]
public class TreeOcclusion : MonoBehaviour
{
    public string renderSortingLayer = "Default";
    public int treeSortingOrder = 11;

    [Range(0.05f, 1f)]
    public float occludedAlpha = 0.65f;

    [Min(0.01f)]
    public float fadeSpeed = 8f;

    [Min(0.1f)]
    public float fadeDistance = 0.55f;

    [Min(1f)]
    public float ditherPixelSize = 2f;

    [Tooltip("Aktivieren für Pixel-Dither. Deaktiviert ergibt einen sauberen Alpha-Fade.")]
    public bool usePixelDither = false;

    public bool logFadeChanges = true;

    // This value must not be derived from SpriteRenderer.color: dithering leaves
    // the surviving pixels opaque, so the renderer's alpha is not the fade state.
    public float CurrentFade => currentFade;

    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int DitherScaleId = Shader.PropertyToID("_DitherScale");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static bool warnedAboutShader;

    private SpriteRenderer[] renderers;
    private Color[] originalColors;
    private Material[] originalMaterials;
    private Material[] ditherMaterials;
    private bool usesDitherShader;
    private bool wasPlayerBehindTree;
    private Transform player;
    private float currentFade = 1f;
    private float nextPlayerSearchTime;

    private void Awake()
    {
        Initialize();
    }

    private void Initialize()
    {
        if (renderers != null) return;

        renderers = GetComponentsInChildren<SpriteRenderer>(true);
        originalColors = new Color[renderers.Length];
        originalMaterials = new Material[renderers.Length];

        for (int i = 0; i < renderers.Length; i++)
        {
            originalColors[i] = renderers[i].color;
            originalMaterials[i] = renderers[i].sharedMaterial;
        }

        CreateDitherMaterials();
    }

    private void OnEnable()
    {
        Initialize();
        currentFade = 1f;
        wasPlayerBehindTree = false;
        nextPlayerSearchTime = 0f;
        ApplyFade();
    }

    private void LateUpdate()
    {
        if (player == null && Time.unscaledTime >= nextPlayerSearchTime)
        {
            PlayerMovement playerMovement = FindFirstObjectByType<PlayerMovement>();
            if (playerMovement != null) player = playerMovement.transform;
            nextPlayerSearchTime = Time.unscaledTime + 1f;
        }

        bool playerBehindTree = false;
        float targetFade = 1f;
        if (player != null && player.gameObject.activeInHierarchy)
        {
            Bounds treeBounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                if (renderers[i] != null)
                {
                    treeBounds.Encapsulate(renderers[i].bounds);
                }
            }

            Vector3 closestTreePoint = treeBounds.ClosestPoint(player.position);
            Vector2 playerOffset = player.position - closestTreePoint;
            float distance = Mathf.Max(0.1f, fadeDistance);
            float distanceToTree = playerOffset.magnitude;
            float influence = 1f - Mathf.Clamp01(distanceToTree / distance);
            targetFade = Mathf.Lerp(1f, Mathf.Clamp01(occludedAlpha), influence);
            playerBehindTree = influence > 0f;
        }

        if (playerBehindTree != wasPlayerBehindTree)
        {
            if (logFadeChanges)
            {
                Debug.Log(name + " tree fade active: " + playerBehindTree
                    + " (" + (usesDitherShader ? "pixel dither" : "alpha fallback") + ")", this);
            }
            wasPlayerBehindTree = playerBehindTree;
        }

        AdvanceFade(targetFade, Time.deltaTime);
    }

    private void AdvanceFade(float targetFade, float deltaTime)
    {
        currentFade = Mathf.MoveTowards(currentFade,
            Mathf.Clamp01(targetFade),
            Mathf.Max(0.01f, fadeSpeed) * Mathf.Max(0f, deltaTime));
        ApplyFade();
    }

    private void ApplyFade()
    {
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;

            if (usesDitherShader)
            {
                ditherMaterials[i].SetFloat(FadeId, currentFade);
                ditherMaterials[i].SetFloat(DitherScaleId, Mathf.Max(1f, ditherPixelSize));
                if (renderers[i].sharedMaterial != ditherMaterials[i])
                    renderers[i].sharedMaterial = ditherMaterials[i];
                renderers[i].color = originalColors[i];
            }
            else
            {
                Color color = originalColors[i];
                color.a *= currentFade;
                renderers[i].color = color;
            }

            if (!string.IsNullOrEmpty(renderSortingLayer))
            {
                renderers[i].sortingLayerName = renderSortingLayer;
            }

            renderers[i].sortingOrder = treeSortingOrder;
        }
    }

    private void CreateDitherMaterials()
    {
        if (!usePixelDither)
        {
            return;
        }

        // A Resources reference also keeps this dynamically assigned shader in builds.
        Shader shader = Resources.Load<Shader>("TreeDitherFade");
        if (shader == null || !shader.isSupported)
        {
            if (!warnedAboutShader)
            {
                Debug.LogWarning("TreeOcclusion: TreeDitherFade shader missing or unsupported. "
                    + "Using alpha fade; check the shader import errors in the Console.", this);
                warnedAboutShader = true;
            }
            return;
        }

        ditherMaterials = new Material[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            ditherMaterials[i] = new Material(shader)
            {
                name = "Tree Dither (runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (originalMaterials[i] != null && originalMaterials[i].HasProperty(ColorId))
                ditherMaterials[i].SetColor(ColorId, originalMaterials[i].GetColor(ColorId));
        }

        usesDitherShader = true;
    }

    private void OnDisable()
    {
        if (renderers == null) return;

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] == null) continue;
            renderers[i].sharedMaterial = originalMaterials[i];
            renderers[i].color = originalColors[i];
        }
        currentFade = 1f;
    }

    private void OnDestroy()
    {
        if (ditherMaterials == null)
        {
            return;
        }

        foreach (Material material in ditherMaterials)
        {
            if (material == null) continue;
            if (Application.isPlaying) Destroy(material);
            else DestroyImmediate(material);
        }
    }
}
