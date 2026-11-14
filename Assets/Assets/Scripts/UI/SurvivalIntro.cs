using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>Start screen and a permanent, compact reminder of the controls.</summary>
public sealed class SurvivalIntro : MonoBehaviour
{
    [SerializeField] private TMP_FontAsset font;
    [SerializeField] private PlayerMovement player;
    private static bool skipNext;
    public static bool IsOpen { get; private set; }
    private GameObject intro;
    private GameObject controls;
    private TextMeshProUGUI roundLabel;
    private DayNightCycle cycle;

    public static void SkipNextIntro() => skipNext = true;

    private void Awake()
    {
        if (player == null) player = FindAnyObjectByType<PlayerMovement>();
        cycle = FindAnyObjectByType<DayNightCycle>();
        if (font == null)
        {
            var existing = FindAnyObjectByType<TextMeshProUGUI>();
            if (existing != null) font = existing.font;
        }
        BuildUI();
        if (skipNext)
        {
            skipNext = false;
            intro.SetActive(false);
            controls.SetActive(true);
            return;
        }
        IsOpen = true;
        controls.SetActive(false);
        if (player != null) player.enabled = false;
        Time.timeScale = 0f;
    }

    private void Update()
    {
        if (IsOpen && Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            StartGame();
        if (roundLabel != null && cycle != null)
        {
            int round = MiniGameController.Instance != null ? MiniGameController.Instance.CurrentRound : 1;
            int seconds = Mathf.CeilToInt(cycle.RemainingSeconds);
            roundLabel.text = $"{(cycle.IsNight ? "NIGHT" : "DAY")} {round}   |   {seconds / 60:00}:{seconds % 60:00}";
        }
    }

    public void StartGame()
    {
        if (!IsOpen) return;
        IsOpen = false;
        intro.SetActive(false);
        controls.SetActive(true);
        if (player != null) player.enabled = true;
        Time.timeScale = 1f;
    }

    private void OnDestroy()
    {
        if (IsOpen) { IsOpen = false; Time.timeScale = 1f; }
    }

    private void BuildUI()
    {
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        var canvasObject = new GameObject("Survival Interface", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        var canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 8000;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        controls = Panel(canvasObject.transform, "Controls", new Color(0.055f, 0.075f, 0.075f, 0.9f), new Vector2(0, 0), new Vector2(22, 22), new Vector2(405, 250));
        Label(controls.transform, "CONTROLS", 23, new Color(0.95f, 0.72f, 0.38f), new Vector2(18, -12), new Vector2(365, 32), TextAlignmentOptions.TopLeft);
        Label(controls.transform, "<color=#F4C67E>WASD / ARROWS</color>  MOVE\n<color=#F4C67E>R</color>  CHOP TREES\n<color=#F4C67E>F</color>  SWING SWORD\n<color=#F4C67E>H</color>  EAT / HEAL\n<color=#F4C67E>E</color>  LIGHT SIGNAL\n<color=#F4C67E>M</color>  SOUND ON / OFF", 19, Color.white, new Vector2(18, -53), new Vector2(370, 185), TextAlignmentOptions.TopLeft);
        var roundPanel = Panel(controls.transform, "Round", new Color(0.055f, 0.075f, 0.075f, 0.9f), new Vector2(0, 1), new Vector2(202.5f, 57), new Vector2(405, 50));
        roundLabel = Label(roundPanel.transform, "DAY 1   |   05:00", 22, new Color(1f, 0.82f, 0.53f), new Vector2(18, -10), new Vector2(370, 32), TextAlignmentOptions.TopLeft);

        intro = Panel(canvasObject.transform, "Startbildschirm", new Color(0.025f, 0.045f, 0.045f, 0.94f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1920, 1080));
        var backdrop = (RectTransform)intro.transform;
        backdrop.anchorMin = Vector2.zero;
        backdrop.anchorMax = Vector2.one;
        backdrop.sizeDelta = Vector2.zero;
        var frame = Panel(intro.transform, "Rahmen", new Color(0.74f, 0.52f, 0.29f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1030, 830));
        var card = Panel(frame.transform, "Karte", new Color(0.075f, 0.105f, 0.095f, 1f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1020, 820));
        Label(card.transform, "A SURVIVAL ADVENTURE", 21, new Color(0.76f, 0.77f, 0.65f), new Vector2(0, -50), new Vector2(940, 35), TextAlignmentOptions.Center, true);
        Label(card.transform, "ISLAND SURVIVAL", 65, new Color(1f, 0.83f, 0.52f), new Vector2(0, -96), new Vector2(940, 85), TextAlignmentOptions.Center, true);
        Label(card.transform, "GATHER. FIGHT. SURVIVE THE NIGHT.", 21, new Color(0.84f, 0.87f, 0.76f), new Vector2(0, -185), new Vector2(930, 35), TextAlignmentOptions.Center, true);
        var tutorial = Panel(card.transform, "Spielziel", new Color(0.12f, 0.16f, 0.135f, 1f), new Vector2(0.5f, 1), new Vector2(0, -235), new Vector2(890, 250));
        Label(tutorial.transform, "HOW TO SURVIVE", 24, new Color(1f, 0.78f, 0.46f), new Vector2(28, -22), new Vector2(830, 35), TextAlignmentOptions.TopLeft);
        Label(tutorial.transform, "01   CHOP TREES WITH R AND COLLECT 3 WOOD.\n02   WALK OVER STONES TO COLLECT 2 STONE.\n03   PRESS E AT THE FIRE TO LIGHT IT AND HEAL.\n04   SURVIVE. EACH NIGHT GETS HARDER!", 20, Color.white, new Vector2(28, -72), new Vector2(835, 160), TextAlignmentOptions.TopLeft);
        Label(card.transform, "WASD / ARROWS  MOVE     F  SWORD     H  EAT", 19, new Color(0.89f, 0.9f, 0.81f), new Vector2(0, -510), new Vector2(950, 35), TextAlignmentOptions.Center, true);
        Label(card.transform, "ANIMALS DROP FOOD. ZOMBIES COME AT NIGHT.", 17, new Color(0.67f, 0.74f, 0.66f), new Vector2(0, -552), new Vector2(950, 35), TextAlignmentOptions.Center, true);
        var buttonObject = Panel(card.transform, "Spiel starten", new Color(0.79f, 0.53f, 0.28f, 1f), new Vector2(0.5f, 1), new Vector2(0, -620), new Vector2(390, 75));
        var button = buttonObject.AddComponent<Button>();
        var colors = button.colors;
        colors.highlightedColor = new Color(1f, 0.9f, 0.72f);
        colors.pressedColor = new Color(0.78f, 0.72f, 0.62f);
        button.colors = colors;
        button.onClick.AddListener(StartGame);
        var buttonText = Label(buttonObject.transform, "START GAME", 27, new Color(0.09f, 0.1f, 0.075f), new Vector2(0, -15), new Vector2(380, 45), TextAlignmentOptions.Center, true);
        buttonText.raycastTarget = false;
        Label(card.transform, "CLICK OR PRESS ENTER", 16, new Color(0.67f, 0.74f, 0.66f), new Vector2(0, -716), new Vector2(700, 30), TextAlignmentOptions.Center, true);
    }

    private static GameObject Panel(Transform parent, string name, Color color, Vector2 anchor, Vector2 position, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(UnityEngine.UI.Image));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor.y == 0 ? new Vector2(0, 0) : anchor.y == 1 ? new Vector2(0.5f, 1) : new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        go.GetComponent<UnityEngine.UI.Image>().color = color;
        return go;
    }

    private TextMeshProUGUI Label(Transform parent, string text, float size, Color color, Vector2 position, Vector2 dimensions, TextAlignmentOptions alignment, bool center = false)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = center ? new Vector2(0.5f, 1) : new Vector2(0, 1);
        rect.pivot = center ? new Vector2(0.5f, 1) : new Vector2(0, 1);
        rect.anchoredPosition = position;
        rect.sizeDelta = dimensions;
        var label = go.GetComponent<TextMeshProUGUI>();
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.raycastTarget = false;
        return label;
    }
}
