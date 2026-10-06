using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Runtime pause menu. Escape pauses the game and exposes game, audio and graphics settings.
/// Settings are saved with PlayerPrefs and restored automatically on the next launch.
/// </summary>
[DefaultExecutionOrder(-900)]
public sealed class PauseSettingsMenu : MonoBehaviour
{
    private const string MasterVolumeKey = "Settings.MasterVolume";
    private const string MusicVolumeKey = "Settings.MusicVolume";
    private const string SfxVolumeKey = "Settings.SfxVolume";
    private const string FullscreenKey = "Settings.Fullscreen";
    private const string VSyncKey = "Settings.VSync";
    private const string FrameRateKey = "Settings.FrameRate";
    private const string ResolutionIndexKey = "Settings.ResolutionIndex";

    private static PauseSettingsMenu instance;

    public static bool IsOpen => instance != null && instance.root != null && instance.root.activeSelf;

    private readonly Dictionary<AudioSource, float> originalSourceVolumes = new();
    private readonly List<GameObject> pages = new();
    private readonly List<Button> tabButtons = new();

    private GameObject root;
    private TextMeshProUGUI pageTitle;
    private TMP_FontAsset font;
    private bool usesPrefabLayout;
    private int activePage;
    private float timeScaleBeforePause = 1f;
    private float masterVolume;
    private float musicVolume;
    private float sfxVolume;
    private bool fullscreen;
    private bool vSync;
    private int frameRateIndex;
    private int resolutionIndex;
    private Resolution[] resolutions;

    private readonly int[] frameRateOptions = { 30, 60, 120, -1 };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreateMenu()
    {
        if (instance != null)
            return;

        GameObject prefab = Resources.Load<GameObject>("UI/PauseSettingsMenu");
        GameObject menuObject = prefab != null
            ? Instantiate(prefab)
            : new GameObject("PauseSettingsMenu");

        menuObject.name = "PauseSettingsMenu";
        instance = menuObject.GetComponent<PauseSettingsMenu>();
        if (instance == null)
            instance = menuObject.AddComponent<PauseSettingsMenu>();

        DontDestroyOnLoad(menuObject);
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        LoadSettings();
        ApplySettings();

        usesPrefabLayout = transform.Find("Canvas") != null;
        if (usesPrefabLayout)
            BindPrefabLayout();
        else
            BuildMinimalUi();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame)
            return;

        // Escape closes the development console first, not the game menu.
        if (DevConsole.IsOpen)
            return;

        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Open()
    {
        if (root == null || root.activeSelf)
            return;

        timeScaleBeforePause = Time.timeScale > 0f ? Time.timeScale : 1f;
        Time.timeScale = 0f;
        root.SetActive(true);
        ShowPage(0);
    }

    public void Close()
    {
        if (root == null || !root.activeSelf)
            return;

        root.SetActive(false);
        Time.timeScale = timeScaleBeforePause;
    }

    private void LoadSettings()
    {
        masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, 0.9f);
        musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, 0.75f);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, 0.85f);
        fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        vSync = PlayerPrefs.GetInt(VSyncKey, 1) == 1;
        frameRateIndex = Mathf.Clamp(PlayerPrefs.GetInt(FrameRateKey, 1), 0, frameRateOptions.Length - 1);

        resolutions = Screen.resolutions;
        if (resolutions == null || resolutions.Length == 0)
            resolutions = new[] { new Resolution { width = 1280, height = 720 }, new Resolution { width = 1920, height = 1080 } };

        resolutionIndex = Mathf.Clamp(PlayerPrefs.GetInt(ResolutionIndexKey, FindCurrentResolutionIndex()), 0, resolutions.Length - 1);
    }

    private int FindCurrentResolutionIndex()
    {
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i].width == Screen.currentResolution.width && resolutions[i].height == Screen.currentResolution.height)
                return i;
        }

        return Mathf.Max(0, resolutions.Length - 1);
    }

    private void ApplySettings()
    {
        AudioListener.volume = masterVolume;
        ApplyAudioSourceVolumes();
        QualitySettings.vSyncCount = vSync ? 1 : 0;
        Application.targetFrameRate = vSync ? -1 : frameRateOptions[frameRateIndex];
        Screen.fullScreenMode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
    }

    private void ApplyAudioSourceVolumes()
    {
        AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsSortMode.None);
        foreach (AudioSource source in sources)
        {
            if (source == null)
                continue;

            if (!originalSourceVolumes.TryGetValue(source, out float originalVolume))
            {
                originalVolume = source.volume;
                originalSourceVolumes[source] = originalVolume;
            }

            string name = source.gameObject.name.ToLowerInvariant();
            bool isMusic = name.Contains("music") || name.Contains("musik") || name.Contains("bgm") || name.Contains("ambient");
            source.volume = originalVolume * (isMusic ? musicVolume : sfxVolume);
        }
    }

    private void SaveSettings()
    {
        PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume);
        PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume);
        PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume);
        PlayerPrefs.SetInt(FullscreenKey, fullscreen ? 1 : 0);
        PlayerPrefs.SetInt(VSyncKey, vSync ? 1 : 0);
        PlayerPrefs.SetInt(FrameRateKey, frameRateIndex);
        PlayerPrefs.SetInt(ResolutionIndexKey, resolutionIndex);
        PlayerPrefs.Save();
        ApplySettings();
    }

    private void BuildUi()
    {
        EnsureEventSystem();
        font = TMP_Settings.defaultFontAsset;

        GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        root = CreateImage("PauseOverlay", canvasObject.transform, new Color(0.02f, 0.04f, 0.06f, 0.84f));
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

        GameObject window = CreateImage("SettingsWindow", root.transform, new Color(0.08f, 0.12f, 0.15f, 1f));
        RectTransform windowRect = window.GetComponent<RectTransform>();
        windowRect.anchorMin = windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.sizeDelta = new Vector2(880f, 600f);
        windowRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI title = CreateText("Title", window.transform, "PAUSE", 36f, new Color(1f, 0.78f, 0.3f), TextAlignmentOptions.Center);
        Anchor(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -26f), new Vector2(0f, 54f));

        TextMeshProUGUI hint = CreateText("Hint", window.transform, "ESC – Weiterspielen", 15f, new Color(0.65f, 0.75f, 0.78f), TextAlignmentOptions.Center);
        Anchor(hint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -70f), new Vector2(0f, 25f));

        GameObject tabs = CreateImage("Tabs", window.transform, new Color(0.055f, 0.08f, 0.1f, 1f));
        Anchor(tabs.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(24f, 24f), new Vector2(178f, -94f));

        GameObject content = new("Content", typeof(RectTransform));
        content.transform.SetParent(window.transform, false);
        Anchor(content.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(205f, 30f), new Vector2(-28f, -100f));

        CreateTab(tabs.transform, "SPIEL", 0, 0);
        CreateTab(tabs.transform, "AUDIO", 1, 1);
        CreateTab(tabs.transform, "GRAFIK", 2, 2);
        CreateTab(tabs.transform, "STEUERUNG", 3, 3);

        BuildGamePage(content.transform);
        BuildAudioPage(content.transform);
        BuildGraphicsPage(content.transform);
        BuildControlsPage(content.transform);

        root.SetActive(false);
    }

    /// <summary>Used once by the editor to generate the editable Canvas prefab.</summary>
    public void BuildEditablePrefabLayout()
    {
        usesPrefabLayout = false;
        LoadSettings();
        BuildMinimalUi();
    }

    private void BuildMinimalUi()
    {
        EnsureEventSystem();

        GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9000;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // Only a subtle backdrop and one empty Image panel remain. Put any UI
        // sprite directly into the Panel's Image component in the Inspector.
        root = CreateImage("PauseOverlay", canvasObject.transform, new Color(0f, 0f, 0f, 0.55f));
        Stretch(root.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);

        GameObject panel = CreateImage("Panel", root.transform, Color.white);
        RectTransform panelRect = panel.GetComponent<RectTransform>();
        panelRect.anchorMin = panelRect.anchorMax = new Vector2(0.5f, 0.5f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.sizeDelta = new Vector2(720f, 460f);
        panelRect.anchoredPosition = Vector2.zero;
    }

    private void CreateTab(Transform parent, string label, int page, int row)
    {
        Button button = CreateButton("Tab_" + label, parent, label, new Color(0.16f, 0.24f, 0.27f, 1f));
        Anchor(button.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(12f, -22f - row * 62f), new Vector2(-12f, 46f));
        button.onClick.AddListener(() => ShowPage(page));
        tabButtons.Add(button);
    }

    private void BuildGamePage(Transform parent)
    {
        GameObject page = CreatePage(parent, "SPIEL");
        CreateLabel(page.transform, "Das Spiel ist pausiert.", 22f, new Vector2(0f, 1f), new Vector2(0f, -62f), new Vector2(0f, 35f));

        Button resume = CreateButton("Resume", page.transform, "WEITERSPIELEN", new Color(0.2f, 0.42f, 0.3f, 1f));
        Anchor(resume.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -125f), new Vector2(0f, 54f));
        resume.onClick.AddListener(Close);

        Button reset = CreateButton("ResetSettings", page.transform, "EINSTELLUNGEN ZURÜCKSETZEN", new Color(0.28f, 0.2f, 0.14f, 1f));
        Anchor(reset.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -198f), new Vector2(0f, 54f));
        reset.onClick.AddListener(ResetSettings);

        Button quit = CreateButton("Quit", page.transform, "SPIEL BEENDEN", new Color(0.42f, 0.18f, 0.18f, 1f));
        Anchor(quit.GetComponent<RectTransform>(), new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 25f), new Vector2(0f, 54f));
        quit.onClick.AddListener(QuitGame);
    }

    private void BuildAudioPage(Transform parent)
    {
        GameObject page = CreatePage(parent, "AUDIO");
        CreateSliderRow(page.transform, "GESAMTLAUTSTÄRKE", 0, masterVolume, value => { masterVolume = value; SaveSettings(); });
        CreateSliderRow(page.transform, "MUSIK", 1, musicVolume, value => { musicVolume = value; SaveSettings(); });
        CreateSliderRow(page.transform, "SOUNDEFFEKTE", 2, sfxVolume, value => { sfxVolume = value; SaveSettings(); });
        CreateLabel(page.transform, "Musikquellen werden über Namen wie Music, Musik, BGM oder Ambient erkannt.", 14f, new Vector2(0f, 0f), new Vector2(0f, 22f), new Vector2(0f, 32f));
    }

    private void BuildGraphicsPage(Transform parent)
    {
        GameObject page = CreatePage(parent, "GRAFIK");
        CreateCycleRow(page.transform, "AUFLÖSUNG", 0, ResolutionLabel(), NextResolution);
        CreateToggleRow(page.transform, "VOLLBILD", 1, fullscreen, value => { fullscreen = value; SaveSettings(); });
        CreateToggleRow(page.transform, "V-SYNC", 2, vSync, value => { vSync = value; SaveSettings(); });
        CreateCycleRow(page.transform, "BILDRATE", 3, FrameRateLabel(), NextFrameRate);
        CreateLabel(page.transform, "V-Sync überschreibt die eingestellte Bildrate.", 14f, new Vector2(0f, 0f), new Vector2(0f, 22f), new Vector2(0f, 32f));
    }

    private void BuildControlsPage(Transform parent)
    {
        GameObject page = CreatePage(parent, "STEUERUNG");
        CreateLabel(page.transform, "BEWEGEN", 18f, new Vector2(0f, 1f), new Vector2(0f, -65f), new Vector2(0f, 28f));
        CreateValueLabel(page.transform, "WASD oder Pfeiltasten", 0);
        CreateLabel(page.transform, "Axt benutzen", 18f, new Vector2(0f, 1f), new Vector2(0f, -185f), new Vector2(0f, 28f));
        CreateValueLabel(page.transform, "R", 2);
        CreateLabel(page.transform, "Pause-Menü", 18f, new Vector2(0f, 1f), new Vector2(0f, -305f), new Vector2(0f, 28f));
        CreateValueLabel(page.transform, "ESC", 4);
        CreateLabel(page.transform, "Tasten-Neubelegung kommt, sobald das Aktions-System erweitert wird.", 14f, new Vector2(0f, 0f), new Vector2(0f, 22f), new Vector2(0f, 32f));
    }

    private GameObject CreatePage(Transform parent, string name)
    {
        GameObject page = new(name + "Page", typeof(RectTransform));
        page.transform.SetParent(parent, false);
        Stretch(page.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        pages.Add(page);
        return page;
    }

    private void CreateSliderRow(Transform parent, string label, int row, float value, Action<float> changed)
    {
        float y = -70f - row * 110f;
        CreateLabel(parent, label, 18f, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(0f, 28f));
        Slider slider = CreateSlider(parent, value);
        Anchor(slider.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, y - 50f), new Vector2(0f, 22f));
        slider.onValueChanged.AddListener(changed.Invoke);
    }

    private void CreateToggleRow(Transform parent, string label, int row, bool value, Action<bool> changed)
    {
        float y = -70f - row * 84f;
        CreateLabel(parent, label, 18f, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(0f, 28f));
        Toggle toggle = CreateToggle(parent, value);
        Anchor(toggle.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-128f, y - 2f), new Vector2(128f, 38f));
        toggle.onValueChanged.AddListener(changed.Invoke);
    }

    private void CreateCycleRow(Transform parent, string label, int row, string value, Action next)
    {
        float y = -70f - row * 84f;
        CreateLabel(parent, label, 18f, new Vector2(0f, 1f), new Vector2(0f, y), new Vector2(0f, 28f));
        Button button = CreateButton("Cycle_" + label, parent, value + "  >", new Color(0.15f, 0.22f, 0.25f, 1f));
        Anchor(button.GetComponent<RectTransform>(), new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-228f, y - 2f), new Vector2(228f, 38f));
        button.onClick.AddListener(next.Invoke);
    }

    private void CreateValueLabel(Transform parent, string value, int row)
    {
        TextMeshProUGUI label = CreateText("Value", parent, value, 22f, new Color(1f, 0.8f, 0.35f), TextAlignmentOptions.Left);
        Anchor(label.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, -108f - row * 60f), new Vector2(0f, 32f));
    }

    private void ShowPage(int page)
    {
        activePage = Mathf.Clamp(page, 0, pages.Count - 1);
        for (int i = 0; i < pages.Count; i++)
            pages[i].SetActive(i == activePage);

        for (int i = 0; i < tabButtons.Count; i++)
        {
            Image image = tabButtons[i].GetComponent<Image>();
            if (image != null)
                image.color = i == activePage ? new Color(0.8f, 0.5f, 0.18f, 1f) : new Color(0.16f, 0.24f, 0.27f, 1f);
        }
    }

    private void NextResolution()
    {
        resolutionIndex = (resolutionIndex + 1) % resolutions.Length;
        Resolution resolution = resolutions[resolutionIndex];
        Screen.SetResolution(resolution.width, resolution.height, fullscreen);
        SaveSettings();
        RebuildForUpdatedLabels();
    }

    private void NextFrameRate()
    {
        frameRateIndex = (frameRateIndex + 1) % frameRateOptions.Length;
        SaveSettings();
        RebuildForUpdatedLabels();
    }

    private string ResolutionLabel()
    {
        Resolution resolution = resolutions[Mathf.Clamp(resolutionIndex, 0, resolutions.Length - 1)];
        return $"{resolution.width} x {resolution.height}";
    }

    private string FrameRateLabel()
    {
        int frameRate = frameRateOptions[frameRateIndex];
        return frameRate < 0 ? "UNBEGRENZT" : frameRate + " FPS";
    }

    private void ResetSettings()
    {
        PlayerPrefs.DeleteKey(MasterVolumeKey);
        PlayerPrefs.DeleteKey(MusicVolumeKey);
        PlayerPrefs.DeleteKey(SfxVolumeKey);
        PlayerPrefs.DeleteKey(FullscreenKey);
        PlayerPrefs.DeleteKey(VSyncKey);
        PlayerPrefs.DeleteKey(FrameRateKey);
        PlayerPrefs.DeleteKey(ResolutionIndexKey);
        LoadSettings();
        ApplySettings();
        RebuildForUpdatedLabels();
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void RebuildForUpdatedLabels()
    {
        if (usesPrefabLayout)
        {
            RefreshPrefabControls();
            return;
        }

        int page = activePage;
        Destroy(root.transform.parent.gameObject);
        pages.Clear();
        tabButtons.Clear();
        root = null;
        BuildUi();
        root.SetActive(true);
        ShowPage(page);
    }

    private void BindPrefabLayout()
    {
        Transform canvas = transform.Find("Canvas");
        Transform overlay = canvas != null ? canvas.Find("PauseOverlay") : null;
        Transform window = overlay != null ? overlay.Find("SettingsWindow") : null;
        Transform content = window != null ? window.Find("Content") : null;
        Transform tabs = window != null ? window.Find("Tabs") : null;

        // Minimal editable prefab: Escape still pauses the game, while the
        // visual layout is only the single panel the designer can customize.
        if (overlay != null && overlay.Find("Panel") != null && window == null)
        {
            root = overlay.gameObject;
            root.SetActive(false);
            return;
        }

        if (overlay == null || content == null || tabs == null)
        {
            Debug.LogWarning("PauseSettingsMenu: Das Prefab ist unvollständig. Die UI wird zur Sicherheit neu erzeugt.", this);
            usesPrefabLayout = false;
            BuildMinimalUi();
            return;
        }

        root = overlay.gameObject;
        pages.Clear();
        tabButtons.Clear();

        string[] pageNames = { "SPIELPage", "AUDIOPage", "GRAFIKPage", "STEUERUNGPage" };
        string[] tabNames = { "Tab_SPIEL", "Tab_AUDIO", "Tab_GRAFIK", "Tab_STEUERUNG" };
        foreach (string pageName in pageNames)
        {
            Transform page = content.Find(pageName);
            if (page != null)
                pages.Add(page.gameObject);
        }

        foreach (string tabName in tabNames)
        {
            Button tab = tabs.Find(tabName)?.GetComponent<Button>();
            if (tab != null)
                tabButtons.Add(tab);
        }

        if (pages.Count != 4 || tabButtons.Count != 4)
        {
            Debug.LogWarning("PauseSettingsMenu: Seiten oder Tabs fehlen im Prefab. Die UI wird zur Sicherheit neu erzeugt.", this);
            usesPrefabLayout = false;
            BuildMinimalUi();
            return;
        }

        for (int i = 0; i < tabButtons.Count; i++)
        {
            int pageIndex = i;
            tabButtons[i].onClick.RemoveAllListeners();
            tabButtons[i].onClick.AddListener(() => ShowPage(pageIndex));
        }

        Button resume = pages[0].transform.Find("Resume")?.GetComponent<Button>();
        Button reset = pages[0].transform.Find("ResetSettings")?.GetComponent<Button>();
        Button quit = pages[0].transform.Find("Quit")?.GetComponent<Button>();
        if (resume != null) resume.onClick.AddListener(Close);
        if (reset != null) reset.onClick.AddListener(ResetSettings);
        if (quit != null) quit.onClick.AddListener(QuitGame);

        Slider[] audioSliders = pages[1].GetComponentsInChildren<Slider>(true);
        if (audioSliders.Length >= 3)
        {
            audioSliders[0].onValueChanged.AddListener(value => { masterVolume = value; SaveSettings(); });
            audioSliders[1].onValueChanged.AddListener(value => { musicVolume = value; SaveSettings(); });
            audioSliders[2].onValueChanged.AddListener(value => { sfxVolume = value; SaveSettings(); });
        }

        Button resolution = pages[2].transform.Find("Cycle_AUFLÖSUNG")?.GetComponent<Button>();
        Button frameRate = pages[2].transform.Find("Cycle_BILDRATE")?.GetComponent<Button>();
        if (resolution != null) resolution.onClick.AddListener(NextResolution);
        if (frameRate != null) frameRate.onClick.AddListener(NextFrameRate);

        Toggle[] graphicsToggles = pages[2].GetComponentsInChildren<Toggle>(true);
        if (graphicsToggles.Length >= 2)
        {
            graphicsToggles[0].onValueChanged.AddListener(value => { fullscreen = value; SaveSettings(); });
            graphicsToggles[1].onValueChanged.AddListener(value => { vSync = value; SaveSettings(); });
        }

        RefreshPrefabControls();
        root.SetActive(false);
    }

    private void RefreshPrefabControls()
    {
        if (!usesPrefabLayout || pages.Count < 3)
            return;

        Slider[] audioSliders = pages[1].GetComponentsInChildren<Slider>(true);
        if (audioSliders.Length >= 3)
        {
            audioSliders[0].SetValueWithoutNotify(masterVolume);
            audioSliders[1].SetValueWithoutNotify(musicVolume);
            audioSliders[2].SetValueWithoutNotify(sfxVolume);
        }

        Toggle[] graphicsToggles = pages[2].GetComponentsInChildren<Toggle>(true);
        if (graphicsToggles.Length >= 2)
        {
            graphicsToggles[0].SetIsOnWithoutNotify(fullscreen);
            graphicsToggles[1].SetIsOnWithoutNotify(vSync);
        }

        SetButtonText(pages[2].transform.Find("Cycle_AUFLÖSUNG")?.GetComponent<Button>(), ResolutionLabel() + "  >");
        SetButtonText(pages[2].transform.Find("Cycle_BILDRATE")?.GetComponent<Button>(), FrameRateLabel() + "  >");
    }

    private static void SetButtonText(Button button, string value)
    {
        if (button == null)
            return;

        TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(true);
        if (label != null)
            label.text = value;
    }

    private static GameObject CreateImage(string name, Transform parent, Color color)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        Image image = go.GetComponent<Image>();
        image.color = color;
        return go;
    }

    private TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color, TextAlignmentOptions alignment)
    {
        GameObject go = new(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        TextMeshProUGUI label = go.GetComponent<TextMeshProUGUI>();
        label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.alignment = alignment;
        label.raycastTarget = false;
        return label;
    }

    private Button CreateButton(string name, Transform parent, string label, Color color)
    {
        GameObject go = CreateImage(name, parent, color);
        Button button = go.AddComponent<Button>();
        ColorBlock colors = button.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.9f);
        colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
        button.colors = colors;

        TextMeshProUGUI text = CreateText("Text", go.transform, label, 18f, Color.white, TextAlignmentOptions.Center);
        Stretch(text.rectTransform, new Vector2(8f, 2f), new Vector2(-8f, -2f));
        return button;
    }

    private Slider CreateSlider(Transform parent, float value)
    {
        GameObject go = CreateImage("Slider", parent, new Color(0.12f, 0.18f, 0.2f, 1f));
        Slider slider = go.AddComponent<Slider>();
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.value = value;

        GameObject fill = CreateImage("Fill", go.transform, new Color(0.95f, 0.65f, 0.22f, 1f));
        RectTransform fillRect = fill.GetComponent<RectTransform>();
        Stretch(fillRect, Vector2.zero, Vector2.zero);
        fillRect.pivot = new Vector2(0f, 0.5f);
        slider.fillRect = fillRect;

        GameObject handle = CreateImage("Handle", go.transform, new Color(1f, 0.88f, 0.55f, 1f));
        RectTransform handleRect = handle.GetComponent<RectTransform>();
        handleRect.sizeDelta = new Vector2(18f, 34f);
        slider.handleRect = handleRect;
        slider.targetGraphic = handle.GetComponent<Image>();
        return slider;
    }

    private Toggle CreateToggle(Transform parent, bool value)
    {
        GameObject go = new("Toggle", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        Toggle toggle = go.AddComponent<Toggle>();

        GameObject background = CreateImage("Background", go.transform, new Color(0.12f, 0.18f, 0.2f, 1f));
        Stretch(background.GetComponent<RectTransform>(), Vector2.zero, Vector2.zero);
        toggle.targetGraphic = background.GetComponent<Image>();

        GameObject checkmark = CreateImage("Checkmark", background.transform, new Color(0.95f, 0.65f, 0.22f, 1f));
        Anchor(checkmark.GetComponent<RectTransform>(), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(5f, 0f), new Vector2(28f, 28f));
        toggle.graphic = checkmark.GetComponent<Image>();

        TextMeshProUGUI text = CreateText("State", go.transform, "AN", 16f, Color.white, TextAlignmentOptions.Right);
        Stretch(text.rectTransform, new Vector2(12f, 0f), new Vector2(-12f, 0f));
        toggle.SetIsOnWithoutNotify(value);
        return toggle;
    }

    private void CreateLabel(Transform parent, string value, float size, Vector2 anchor, Vector2 position, Vector2 height)
    {
        TextMeshProUGUI label = CreateText("Label", parent, value, size, new Color(0.84f, 0.91f, 0.92f), TextAlignmentOptions.Left);
        // Labels always stretch across the content area. Anchoring them to a single
        // point gave them a width of zero, which made TMP wrap every letter vertically.
        label.rectTransform.anchorMin = new Vector2(0f, anchor.y);
        label.rectTransform.anchorMax = new Vector2(1f, anchor.y);
        label.rectTransform.pivot = new Vector2(0.5f, 0.5f);
        label.rectTransform.anchoredPosition = new Vector2(0f, position.y);
        label.rectTransform.sizeDelta = new Vector2(0f, height.y);
    }

    private static void Anchor(RectTransform rect, Vector2 min, Vector2 max, Vector2 position, Vector2 size)
    {
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    private static void EnsureEventSystem()
    {
        if (!Application.isPlaying)
            return;

        if (FindAnyObjectByType<EventSystem>() != null)
            return;

        GameObject eventSystem = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        DontDestroyOnLoad(eventSystem);
    }
}
