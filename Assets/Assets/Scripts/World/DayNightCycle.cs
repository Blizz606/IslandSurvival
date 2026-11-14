using UnityEngine;
using UnityEngine.UI;

/// <summary>Five minutes of day and five minutes of night, using scaled game time.</summary>
[DefaultExecutionOrder(1000)]
public sealed class DayNightCycle : MonoBehaviour
{
    [SerializeField, Min(1f)] private float daySeconds = 300f;
    [SerializeField, Min(1f)] private float nightSeconds = 300f;
    [SerializeField, Range(0f, 1f)] private float nightDarkness = 1f;
    [SerializeField, Min(0f)] private float transitionSeconds = 20f;
    [Header("Night visibility")]
    [Tooltip("Radius as a fraction of the screen height.")]
    [SerializeField, Range(0.01f, 0.5f)] private float visibleRadius = 0.14f;
    [SerializeField, Range(0.001f, 0.15f)] private float edgeSoftness = 0.07f;
    [SerializeField, Range(0f, 0.05f)] private float flickerStrength = 0.002f;
    [SerializeField, Min(0f)] private float flickerSpeed = 1.7f;
    [SerializeField] private Transform player;
    private float phaseTime;
    private bool isNight;
    private Image tint;
    private Material nightMaterial;
    private Camera worldCamera;
    private float nextPlayerSearch;
    private static readonly int CenterId = Shader.PropertyToID("_Center");
    private static readonly int AspectId = Shader.PropertyToID("_Aspect");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int FeatherId = Shader.PropertyToID("_Feather");

    public bool IsNight => isNight;
    public float RemainingSeconds => Mathf.Max(0f, (isNight ? nightSeconds : daySeconds) - phaseTime);
    public event System.Action<bool> PhaseChanged;

    private void Awake()
    {
        GameObject canvasObject = new GameObject("World Night Tint", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -10;
        GameObject imageObject = new GameObject("Tint", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        tint = imageObject.GetComponent<Image>();
        tint.raycastTarget = false;
        Shader shader = Resources.Load<Shader>("NightVisionMask");
        if (shader != null && shader.isSupported)
        {
            nightMaterial = new Material(shader) { name = "Night Vision (runtime)" };
            tint.material = nightMaterial;
        }
        else
            Debug.LogError("DayNightCycle: NightVisionMask shader missing or unsupported.", this);
        UpdateTint();
    }

    private void LateUpdate()
    {
        if (nightMaterial == null) return;
        if (worldCamera == null) worldCamera = Camera.main;
        if (player == null && Time.unscaledTime >= nextPlayerSearch)
        {
            PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
            if (movement != null) player = movement.transform;
            nextPlayerSearch = Time.unscaledTime + 1f;
        }
        Vector3 screenPosition = worldCamera != null && player != null
            ? worldCamera.WorldToScreenPoint(player.position)
            : new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f);
        float width = Mathf.Max(1, Screen.width);
        float height = Mathf.Max(1, Screen.height);
        nightMaterial.SetVector(CenterId, new Vector4(screenPosition.x / width, screenPosition.y / height, 0f, 0f));
        nightMaterial.SetFloat(AspectId, width / height);
        float flicker = (Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f) - 0.5f) * 2f * flickerStrength;
        nightMaterial.SetFloat(RadiusId, Mathf.Max(0.01f, visibleRadius + flicker));
        nightMaterial.SetFloat(FeatherId, Mathf.Max(0.001f, edgeSoftness));
    }

    private void Update()
    {
        phaseTime += Time.deltaTime;
        float duration = isNight ? nightSeconds : daySeconds;
        if (phaseTime >= duration)
            SetNight(!isNight);
        else
            UpdateTint();
    }

    public void SetNight(bool night)
    {
        bool changed = isNight != night;
        isNight = night;
        phaseTime = 0f;
        UpdateTint();
        if (changed) PhaseChanged?.Invoke(isNight);
    }

    public void SkipPhase() => SetNight(!isNight);

    private void UpdateTint()
    {
        if (tint == null) return;
        float duration = isNight ? nightSeconds : daySeconds;
        float transition = Mathf.Min(transitionSeconds, duration * 0.5f);
        float blend = transition <= 0f ? 1f : Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(phaseTime / transition));
        float darkness = isNight ? blend : 1f - blend;
        // The first day starts in daylight, rather than fading from a previous night.
        if (!isNight && !hasSeenNight) darkness = 0f;
        if (isNight) hasSeenNight = true;
        tint.color = new Color(0f, 0f, 0f, darkness * nightDarkness);
    }

    private bool hasSeenNight;

    private void OnDestroy()
    {
        if (nightMaterial != null) Destroy(nightMaterial);
    }
}
