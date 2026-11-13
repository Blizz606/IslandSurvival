using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
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
        if (prefab == null || prefab.GetComponent<PauseSettingsMenu>() == null)
        {
            Debug.LogError("PauseSettingsMenu: Das Ressourcen-Prefab UI/PauseSettingsMenu mit PauseSettingsMenu-Komponente fehlt.");
            return;
        }

        GameObject menuObject = Instantiate(prefab);
        menuObject.name = "PauseSettingsMenu";
        instance = menuObject.GetComponent<PauseSettingsMenu>();

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

        BindPrefabLayout();
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
        RefreshPrefabControls();
    }

    private void NextFrameRate()
    {
        frameRateIndex = (frameRateIndex + 1) % frameRateOptions.Length;
        SaveSettings();
        RefreshPrefabControls();
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
        RefreshPrefabControls();
    }

    private static void QuitGame()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
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
            Debug.LogError("PauseSettingsMenu: Im Prefab fehlen Canvas/PauseOverlay oder die vollständigen Einstellungen-Seiten.", this);
            enabled = false;
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
            Debug.LogError("PauseSettingsMenu: Im Prefab fehlen Einstellungen-Seiten oder Tabs.", this);
            root = null;
            enabled = false;
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
        if (pages.Count < 3)
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

}
