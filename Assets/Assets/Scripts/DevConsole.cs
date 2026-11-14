using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A small in-game console for testing. Toggle it with Ctrl + Shift + C.
/// Other systems can add commands via DevConsole.RegisterCommand(...).
/// </summary>
[DefaultExecutionOrder(-1000)]
public sealed class DevConsole : MonoBehaviour
{
    private const int MaxLogLines = 18;

    private static DevConsole instance;
    private static readonly Dictionary<string, Command> commands = new(StringComparer.OrdinalIgnoreCase);

    [SerializeField] private bool enableInReleaseBuilds;
    [SerializeField] private Key alternateToggleKey = Key.F10;

    private readonly List<string> logLines = new();
    private GameObject panel;
    private TextMeshProUGUI outputText;
    private TMP_InputField inputField;

    public static bool IsOpen => instance != null && instance.panel != null && instance.panel.activeSelf;

    public static void CloseForGameOver()
    {
        if (IsOpen) instance.Close();
    }

    private sealed class Command
    {
        public string Description;
        public Func<string[], string> Execute;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateConsole()
    {
        if (instance != null)
            return;

        GameObject consoleObject = new("DevConsole");
        instance = consoleObject.AddComponent<DevConsole>();
        DontDestroyOnLoad(consoleObject);
    }

    /// <summary>Add a project-specific command, for example: give wood 5.</summary>
    public static void RegisterCommand(string name, string description, Func<string[], string> execute)
    {
        if (string.IsNullOrWhiteSpace(name) || execute == null)
            throw new ArgumentException("A command needs a name and an execute function.");

        commands[name.Trim()] = new Command { Description = description ?? string.Empty, Execute = execute };
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;

        if (!enableInReleaseBuilds && !Application.isEditor && !Debug.isDebugBuild)
        {
            Destroy(gameObject);
            return;
        }

        RegisterBuiltInCommands();
        BuildUi();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        bool control = keyboard.leftCtrlKey.isPressed || keyboard.rightCtrlKey.isPressed;
        bool shift = keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed;
        bool togglePressed = control && shift && keyboard.cKey.wasPressedThisFrame;

        if (togglePressed || keyboard[alternateToggleKey].wasPressedThisFrame)
            Toggle();

        if (panel.activeSelf && keyboard.escapeKey.wasPressedThisFrame)
            Close();
    }

    private void RegisterBuiltInCommands()
    {
        RegisterCommand("help", "Shows all commands.", _ => string.Join("\n", commands
            .OrderBy(pair => pair.Key)
            .Select(pair => $"> {pair.Key} - {pair.Value.Description}")));

        RegisterCommand("clear", "Clears the console output.", _ =>
        {
            logLines.Clear();
            RefreshOutput();
            return string.Empty;
        });

        RegisterCommand("echo", "Prints text to the console.", args => string.Join(" ", args));

        RegisterCommand("player", "Shows the player position.", _ =>
        {
            Transform player = FindPlayer();
            return player == null ? "Player not found." : $"Player: {player.position.x:0.0}, {player.position.y:0.0}";
        });

        RegisterCommand("chop", "Plays the chop animation.", _ =>
        {
            PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
            if (player == null)
                return "Player not found.";

            return player.TryStartChop()
                ? "Chop animation started."
                : "Cannot start the chop animation right now.";
        });

        RegisterCommand("tp", "Teleports the player: tp <x> <y>", TeleportPlayer);

        RegisterCommand("timescale", "Sets the time scale: timescale <0-5>", SetTimeScale);

        RegisterCommand("reload", "Reloads the current scene.", _ =>
        {
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
            return "Reloading scene.";
        });
        
        RegisterCommand(
            "give", "Gives resources: give <wood|stone|food> <amount>", GiveResources);

        RegisterCommand(
            "life", "Leben ändern: life <give|remove> <half hearts>", ChangeLife);

        RegisterCommand("time", "Day/night: time <day|night|skip|status>", ChangeWorldTime);
        RegisterCommand("weather", "Weather: weather <rain|clear|skip|status>", ChangeWeather);
    }

    private void BuildUi()
    {
        EnsureEventSystem();

        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        panel = CreateImage("Panel", canvasObject.transform, new Color(0.035f, 0.055f, 0.07f, 0.96f));
        SetStretch(panel.GetComponent<RectTransform>(), new Vector2(18, 18), new Vector2(-18, 255));

        TextMeshProUGUI header = CreateText("Header", panel.transform, font, "DEV CONSOLE   [Ctrl + Shift + C to close]", 18, new Color(1f, 0.78f, 0.3f));
        SetTopStretch(header.rectTransform, new Vector2(16, -12), new Vector2(-16, 30));

        outputText = CreateText("Output", panel.transform, font, string.Empty, 16, new Color(0.9f, 0.95f, 0.96f));
        outputText.textWrappingMode = TextWrappingModes.Normal;
        outputText.overflowMode = TextOverflowModes.Overflow;
        SetStretch(outputText.rectTransform, new Vector2(16, 55), new Vector2(-16, -48));

        GameObject inputBackground = CreateImage("InputBackground", panel.transform, new Color(0.12f, 0.17f, 0.2f, 1f));
        SetBottomStretch(inputBackground.GetComponent<RectTransform>(), new Vector2(16, 14), new Vector2(-16, 36));

        inputField = inputBackground.AddComponent<TMP_InputField>();
        TextMeshProUGUI inputText = CreateText("Text", inputBackground.transform, font, string.Empty, 18, Color.white);
        inputText.alignment = TextAlignmentOptions.MidlineLeft;
        SetStretch(inputText.rectTransform, new Vector2(12, 2), new Vector2(-12, -2));

        TextMeshProUGUI placeholder = CreateText("Placeholder", inputBackground.transform, font, "Enter command ...  (help)", 18, new Color(0.55f, 0.62f, 0.65f));
        placeholder.alignment = TextAlignmentOptions.MidlineLeft;
        SetStretch(placeholder.rectTransform, new Vector2(12, 2), new Vector2(-12, -2));

        inputField.textViewport = inputBackground.GetComponent<RectTransform>();
        inputField.textComponent = inputText;
        inputField.placeholder = placeholder;
        inputField.lineType = TMP_InputField.LineType.SingleLine;
        inputField.onSubmit.AddListener(Submit);

        WriteLine("Dev console ready. Type 'help' for commands.");
        panel.SetActive(false);
    }

    private void Toggle()
    {
        if (panel.activeSelf)
            Close();
        else
            Open();
    }

    private void Open()
    {
        panel.SetActive(true);
        inputField.text = string.Empty;
        inputField.ActivateInputField();
    }

    private void Close()
    {
        inputField.DeactivateInputField();
        panel.SetActive(false);
    }

    private void Submit(string submittedText)
    {
        string line = submittedText.Trim();
        inputField.text = string.Empty;
        inputField.ActivateInputField();

        if (string.IsNullOrEmpty(line))
            return;

        WriteLine($"<color=#FFC74D>> {line}</color>");
        string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        string commandName = parts[0];
        string[] args = parts.Skip(1).ToArray();

        if (!commands.TryGetValue(commandName, out Command command))
        {
            WriteLine($"Unknown resource: {commandName}. Nutze 'help'.");
            return;
        }

        try
        {
            string response = command.Execute(args);
            if (!string.IsNullOrEmpty(response))
                WriteLine(response);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteLine($"Error: {exception.Message}");
        }
    }

    private string TeleportPlayer(string[] args)
    {
        if (args.Length != 2 || !TryParse(args[0], out float x) || !TryParse(args[1], out float y))
            return "Usage: tp <x> <y>";

        Transform player = FindPlayer();
        if (player == null)
            return "Player not found.";

        Rigidbody2D body = player.GetComponent<Rigidbody2D>();
        if (body != null)
            body.position = new Vector2(x, y);
        else
            player.position = new Vector3(x, y, player.position.z);

        return $"Teleported to {x:0.0}, {y:0.0}.";
    }

    private static string SetTimeScale(string[] args)
    {
        if (args.Length != 1 || !TryParse(args[0], out float scale))
            return "Usage: timescale <0-5>";

        Time.timeScale = Mathf.Clamp(scale, 0f, 5f);
        return $"Time Scale: {Time.timeScale:0.0}";
    }

    private static bool TryParse(string value, out float number)
    {
        return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            || float.TryParse(value, NumberStyles.Float, CultureInfo.CurrentCulture, out number);
    }

    private static Transform FindPlayer()
    {
        PlayerMovement movement = FindAnyObjectByType<PlayerMovement>();
        return movement != null ? movement.transform : GameObject.Find("Player")?.transform;
    }

    private static string GiveResources(string[] args)
    {
        if (args.Length != 2)
            return "Usage: give <material> <amount>";
        
        string itemId = args[0].ToLowerInvariant();
        
        if (itemId != "wood" && itemId != "stone" && itemId != "food")
            return "Unknown resource: " + itemId;

        if (!int.TryParse(args[1], out int amount) || amount <= 0)
            return "Number higher than zero required";
        
        ResourceInventory inventory = 
            UnityEngine.Object.FindAnyObjectByType<ResourceInventory>();

        if (inventory == null)
            return "Inventory not found";
        
        inventory.Add(itemId, amount);

        return $"+{amount} {itemId} received";
    }

    private static string ChangeLife(string[] args)
    {
        if (args.Length != 2)
            return "Usage: life <give|remove> <half hearts>";

        if (!int.TryParse(args[1], out int amount) || amount <= 0)
            return "Amount must be greater than zero.";

        HeartBar heartBar = UnityEngine.Object.FindAnyObjectByType<HeartBar>();
        if (heartBar == null)
            return "Heart bar not found.";

        string action = args[0].ToLowerInvariant();
        if (action == "give" || action == "add" || action == "heal")
        {
            heartBar.Heal(amount);
            return $"+{amount} half hearts. Health: {heartBar.CurrentHealth}/{heartBar.MaxHealth}";
        }

        if (action == "remove" || action == "damage" || action == "take")
        {
            heartBar.TakeDamage(amount);
            return $"-{amount} half hearts. Health: {heartBar.CurrentHealth}/{heartBar.MaxHealth}";
        }

        return "Usage: life <give|remove> <half hearts>";
    }

    private static string ChangeWorldTime(string[] args)
    {
        DayNightCycle cycle = FindAnyObjectByType<DayNightCycle>();
        if (cycle == null) return "No day-night cycle found in this scene.";
        if (args.Length != 1) return "Usage: time <day|night|skip|status>";
        switch (args[0].ToLowerInvariant())
        {
            case "day": cycle.SetNight(false); break;
            case "night": cycle.SetNight(true); break;
            case "skip": cycle.SkipPhase(); break;
            case "status": break;
            default: return "Usage: time <day|night|skip|status>";
        }
        return $"{(cycle.IsNight ? "Night" : "Day")}: {Mathf.CeilToInt(cycle.RemainingSeconds)} seconds remaining.";
    }

    private static string ChangeWeather(string[] args)
    {
        IslandWeather weather = FindAnyObjectByType<IslandWeather>();
        if (weather == null) return "No weather system found in this scene.";
        if (args.Length != 1) return "Usage: weather <rain|clear|skip|status>";
        switch (args[0].ToLowerInvariant())
        {
            case "rain": weather.SetRain(true); break;
            case "clear": weather.SetRain(false); break;
            case "skip": weather.SetRain(!weather.IsRaining); break;
            case "status": break;
            default: return "Usage: weather <rain|clear|skip|status>";
        }
        return $"{(weather.IsRaining ? "Rain" : "Clear")}: about {Mathf.CeilToInt(weather.RemainingSeconds)} seconds remaining.";
    }

    private void WriteLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return;

        logLines.Add(line);
        if (logLines.Count > MaxLogLines)
            logLines.RemoveAt(0);
        RefreshOutput();
    }

    private void RefreshOutput()
    {
        if (outputText != null)
            outputText.text = string.Join("\n", logLines);
    }

    private static void EnsureEventSystem()
    {
        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
    }

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = true;
        return go;
    }

    private static TextMeshProUGUI CreateText(string name, Transform parent, TMP_FontAsset font, string value, float size, Color color)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    private static void SetStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void SetTopStretch(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    private static void SetBottomStretch(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }
}
