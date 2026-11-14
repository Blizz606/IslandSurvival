using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>Shows the death screen and restarts the current scene.</summary>
public sealed class GameOverScreen : MonoBehaviour
{
    [SerializeField] private HeartBar hearts;
    [SerializeField] private PlayerMovement player;
    [SerializeField] private GameObject screen;
    [SerializeField] private CanvasGroup screenGroup;
    [SerializeField] private Button restartButton;
    [SerializeField] private TextMeshProUGUI title;
    [SerializeField] private TextMeshProUGUI subtitle;
    [SerializeField] private TextMeshProUGUI buttonLabel;
    [SerializeField, Min(0.01f)] private float fadeSeconds = 0.65f;

    public static bool IsGameOver { get; private set; }
    private bool restarting;

    private void Awake()
    {
        IsGameOver = false;
        if (hearts == null) hearts = FindAnyObjectByType<HeartBar>();
        if (player == null) player = FindAnyObjectByType<PlayerMovement>();
        if (screen != null) screen.SetActive(false);
        if (restartButton != null) restartButton.onClick.AddListener(RestartGame);
    }

    private void OnEnable()
    {
        if (hearts != null) hearts.HealthChanged += OnHealthChanged;
    }

    private void Start()
    {
        if (hearts != null && hearts.CurrentHealth <= 0) Show();
    }

    private void OnDisable()
    {
        if (hearts != null) hearts.HealthChanged -= OnHealthChanged;
    }

    private void OnHealthChanged(int health, int maximum)
    {
        if (health <= 0)
        {
            var game = MiniGameController.Instance;
            if (subtitle != null && game != null) subtitle.text = $"NIGHTS SURVIVED: {game.NightsSurvived}";
            Show();
        }
    }

    private void Show()
    {
        if (IsGameOver) return;
        if (screen == null || screenGroup == null || restartButton == null)
        {
            Debug.LogError("GameOverScreen: Screen, CanvasGroup or Restart Button is missing.", this);
            return;
        }

        IsGameOver = true;
        DevConsole.CloseForGameOver();
        if (player != null) player.enabled = false;
        Time.timeScale = 0f;
        if (FindAnyObjectByType<EventSystem>() == null)
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        screenGroup.alpha = 0f;
        screen.SetActive(true);
        StartCoroutine(FadeIn());
    }

    public void ShowVictory()
    {
        if (IsGameOver) return;
        if (title != null) title.text = "YOU SURVIVED";
        if (subtitle != null) subtitle.text = "YOUR SIGNAL WAS SEEN AT DAWN";
        if (buttonLabel != null) buttonLabel.text = "PLAY AGAIN";
        Show();
    }

    private IEnumerator FadeIn()
    {
        float elapsed = 0f;
        while (elapsed < fadeSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            screenGroup.alpha = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, fadeSeconds));
            yield return null;
        }
        screenGroup.alpha = 1f;
        restartButton.Select();
    }

    public void RestartGame()
    {
        if (!IsGameOver || restarting) return;
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex < 0)
        {
            Debug.LogError("GameOverScreen: Active scene is not enabled in Build Settings; cannot restart.", this);
            return;
        }
        restarting = true;
        SurvivalIntro.SkipNextIntro();
        Time.timeScale = 1f;
        SceneManager.LoadScene(scene.buildIndex);
    }

    private void OnDestroy()
    {
        if (restartButton != null) restartButton.onClick.RemoveListener(RestartGame);
        if (IsGameOver)
        {
            Time.timeScale = 1f;
            IsGameOver = false;
        }
    }
}
