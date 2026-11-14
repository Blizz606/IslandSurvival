using UnityEngine;
using UnityEngine.UI;

/// <summary>Occasional screen-space rain; the HUD remains above the weather.</summary>
public sealed class IslandWeather : MonoBehaviour
{
    [SerializeField] private Texture2D rainSheet;
    [SerializeField, Min(0f)] private float clearMinSeconds = 120f;
    [SerializeField, Min(0f)] private float clearMaxSeconds = 240f;
    [SerializeField, Min(0f)] private float rainMinSeconds = 45f;
    [SerializeField, Min(0f)] private float rainMaxSeconds = 90f;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 3f;
    [SerializeField, Range(0f, 1f)] private float rainOpacity = 0.7f;
    [SerializeField, Min(0.01f)] private float tileScale = 2f;

    private const int FrameSize = 64;
    private const int Columns = 8;
    private const int FrameCount = 16;
    private const float FramesPerSecond = 24f;
    private readonly Sprite[] rainFrames = new Sprite[FrameCount];
    private Image rainImage;
    private float remaining;
    private float alpha;
    private float animationTime;
    private bool raining;

    public bool IsRaining => raining;
    public float RemainingSeconds => Mathf.Max(0f, remaining);

    private void Awake()
    {
        if (rainSheet == null || rainSheet.width < Columns * FrameSize || rainSheet.height < 2 * FrameSize)
        {
            Debug.LogError("IslandWeather: Rain sheet missing or has unexpected dimensions.", this);
            enabled = false;
            return;
        }

        for (int i = 0; i < FrameCount; i++)
        {
            int x = (i % Columns) * FrameSize;
            int y = rainSheet.height - (i / Columns + 1) * FrameSize;
            rainFrames[i] = Sprite.Create(rainSheet, new Rect(x, y, FrameSize, FrameSize),
                new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            rainFrames[i].name = "Rain_" + i;
        }

        GameObject canvasObject = new GameObject("Rain Overlay", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = -15; // Behind nighttime darkness (-10) and the HUD (0).

        GameObject imageObject = new GameObject("Rain", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        imageObject.transform.SetParent(canvasObject.transform, false);
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        rainImage = imageObject.GetComponent<Image>();
        rainImage.sprite = rainFrames[0];
        rainImage.type = Image.Type.Tiled;
        rainImage.pixelsPerUnitMultiplier = 1f / Mathf.Max(0.01f, tileScale);
        rainImage.raycastTarget = false;
        rainImage.enabled = false;
        remaining = RandomDuration(clearMinSeconds, clearMaxSeconds);
    }

    private void Update()
    {
        if (rainImage == null) return;
        remaining -= Time.deltaTime;
        if (remaining <= 0f) SetRain(!raining);

        float target = raining ? rainOpacity : 0f;
        alpha = Mathf.MoveTowards(alpha, target, rainOpacity * Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
        rainImage.enabled = alpha > 0f;
        rainImage.color = new Color(1f, 1f, 1f, alpha);
        if (rainImage.enabled)
        {
            animationTime += Time.deltaTime * FramesPerSecond;
            rainImage.sprite = rainFrames[(int)animationTime % FrameCount];
        }
    }

    /// <summary>Start or stop rain now, then continue with automatic weather.</summary>
    public void SetRain(bool enabled)
    {
        raining = enabled;
        remaining = enabled ? RandomDuration(rainMinSeconds, rainMaxSeconds)
            : RandomDuration(clearMinSeconds, clearMaxSeconds);
    }

    private static float RandomDuration(float min, float max)
    {
        min = Mathf.Max(1f, min);
        return Random.Range(min, Mathf.Max(min, max));
    }

    private void OnDestroy()
    {
        foreach (Sprite sprite in rainFrames)
            if (sprite != null) Destroy(sprite);
    }
}
