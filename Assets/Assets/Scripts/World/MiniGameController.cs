using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

/// <summary>Collect resources and survive progressively harder nights.</summary>
public sealed class MiniGameController : MonoBehaviour
{
    public static MiniGameController Instance { get; private set; }
    [SerializeField] private PlayerMovement player;
    [SerializeField] private ResourceInventory inventory;
    [SerializeField] private HeartBar hearts;
    [SerializeField] private DayNightCycle dayNight;
    [SerializeField] private IslandGenerator island;
    [SerializeField] private Tilemap grass;
    [SerializeField] private GameOverScreen resultScreen;
    [SerializeField] private Sprite stoneSprite;
    [SerializeField] private Sprite foodSprite;
    [SerializeField] private TextMeshProUGUI questTitle;
    [SerializeField] private TextMeshProUGUI questHint;
    [SerializeField] private TextMeshProUGUI woodCount;
    [SerializeField] private TextMeshProUGUI stoneCount;
    [SerializeField] private TextMeshProUGUI feedback;
    [SerializeField, Min(1)] private int requiredWood = 3;
    [SerializeField, Min(1)] private int requiredStone = 2;
    [SerializeField, Min(1)] private int startingStones = 8;
    [SerializeField, Min(0.1f)] private float campfireRange = 1.8f;
    private bool signalLit;
    private bool nightStarted;
    public int NightsSurvived { get; private set; }
    public int CurrentRound => NightsSurvived + 1;
    private float feedbackUntil;

    private void Awake()
    {
        Instance = this;
        if (player == null) player = FindAnyObjectByType<PlayerMovement>();
        if (inventory == null && player != null) inventory = player.GetComponent<ResourceInventory>();
        if (hearts == null) hearts = FindAnyObjectByType<HeartBar>();
        if (dayNight == null) dayNight = FindAnyObjectByType<DayNightCycle>();
        if (island == null) island = FindAnyObjectByType<IslandGenerator>();
        if (resultScreen == null) resultScreen = FindAnyObjectByType<GameOverScreen>();
    }

    private void OnEnable()
    {
        if (inventory != null) inventory.Changed += OnInventoryChanged;
        if (dayNight != null) dayNight.PhaseChanged += OnPhaseChanged;
    }

    private IEnumerator Start()
    {
        RefreshQuest();
        yield return new WaitForEndOfFrame(); // Island and campfire are finalized in LateUpdate.
        SpawnStones();
        ShowFeedback("COLLECT WOOD AND STONE TO LIGHT THE SIGNAL", 6f);
    }

    private void Update()
    {
        if (GameOverScreen.IsGameOver || DevConsole.IsOpen || Time.timeScale <= 0f) return;
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null) return;
        if (keyboard.hKey.wasPressedThisFrame) EatFood();

        bool nearFire = player != null && island != null && island.TryGetCampfirePosition(out Vector2 fire)
            && ((Vector2)player.transform.position - fire).sqrMagnitude <= campfireRange * campfireRange;
        if (nearFire && !signalLit && keyboard.eKey.wasPressedThisFrame) TryLightSignal();

        if (feedback == null || Time.time < feedbackUntil) return;
        if (nearFire && !signalLit)
        {
            feedback.text = inventory != null && inventory.GetAmount("wood") >= requiredWood && inventory.GetAmount("stone") >= requiredStone
                ? "E: LIGHT SIGNAL AND HEAL" : "SIGNAL: 3 WOOD + 2 STONE";
            feedback.enabled = true;
        }
        else feedback.enabled = false;
    }

    public void SpawnFood(Vector3 position) => SpawnPickup("food", 1, foodSprite, position);

    public void NotifyPickup(string id, int amount)
    {
        ShowFeedback("+" + amount + " " + id.ToUpperInvariant(), 2f);
    }

    private void SpawnPickup(string id, int amount, Sprite sprite, Vector3 position)
    {
        if (sprite == null || player == null || inventory == null) return;
        GameObject go = new GameObject(id == "food" ? "Food Pickup" : "Stone Pickup");
        go.transform.SetParent(transform, false);
        go.transform.position = position;
        go.AddComponent<WorldPickup>().Configure(id, amount, sprite, player, inventory);
    }

    private void SpawnStones()
    {
        if (grass == null || player == null || stoneSprite == null) return;
        var placed = new List<Vector2>();
        for (int attempt = 0; attempt < 400 && placed.Count < startingStones; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(1.8f, 7f);
            Vector2 pos = (Vector2)player.transform.position + offset;
            if (!grass.HasTile(grass.WorldToCell(pos))) continue;
            bool blocked = false;
            foreach (Collider2D hit in Physics2D.OverlapCircleAll(pos, 0.35f))
                if (hit != null && !hit.isTrigger) { blocked = true; break; }
            if (blocked) continue;
            foreach (Vector2 existing in placed)
                if ((existing - pos).sqrMagnitude < 0.49f) { blocked = true; break; }
            if (blocked) continue;
            placed.Add(pos);
            SpawnPickup("stone", 1, stoneSprite, pos);
        }
        if (placed.Count < requiredStone)
            Debug.LogWarning("MiniGame: Not enough free grass positions for the stone objective.", this);
    }

    private void EatFood()
    {
        if (inventory == null || hearts == null) return;
        if (hearts.CurrentHealth >= hearts.MaxHealth) { ShowFeedback("HEALTH IS FULL", 2f); return; }
        if (!inventory.TryRemove("food", 1)) { ShowFeedback("NO FOOD", 2f); return; }
        hearts.Heal(2);
        ShowFeedback("ATE FOOD  +1 HEART", 2f);
    }

    private void TryLightSignal()
    {
        if (inventory == null || signalLit) return;
        if (inventory.GetAmount("wood") < requiredWood || inventory.GetAmount("stone") < requiredStone)
        {
            ShowFeedback("YOU NEED 3 WOOD + 2 STONE", 2.5f);
            return;
        }
        signalLit = true;
        GameAudio.Play(GameAudio.Sound.Signal);
        inventory.TryRemove("wood", requiredWood);
        inventory.TryRemove("stone", requiredStone);
        if (hearts != null) hearts.Heal(hearts.MaxHealth);
        RefreshQuest();
        ShowFeedback("SIGNAL LIT! HEALTH RESTORED", 4f);
    }

    private void OnInventoryChanged(string id, int amount) => RefreshQuest();

    private void OnPhaseChanged(bool night)
    {
        if (night)
        {
            nightStarted = true;
            ShowFeedback($"NIGHT {CurrentRound} - ZOMBIES ARE COMING", 4f);
        }
        else
        {
            if (nightStarted) NightsSurvived++;
            nightStarted = false;
            ShowFeedback($"NIGHT {NightsSurvived} SURVIVED - PREPARE FOR THE NEXT", 4f);
        }
        RefreshQuest();
    }

    private void RefreshQuest()
    {
        if (questTitle != null) questTitle.text = signalLit ? "SURVIVE THE NIGHTS" : "LIGHT THE SIGNAL";
        if (questHint != null) questHint.text = signalLit ? "EACH NIGHT IS HARDER" : "COLLECT 3 WOOD + 2 STONE\nPRESS E AT THE CAMPFIRE";
        if (woodCount != null) woodCount.text = signalLit ? $"{requiredWood}/{requiredWood}" : $"{(inventory != null ? inventory.GetAmount("wood") : 0)}/{requiredWood}";
        if (stoneCount != null) stoneCount.text = signalLit ? $"{requiredStone}/{requiredStone}" : $"{(inventory != null ? inventory.GetAmount("stone") : 0)}/{requiredStone}";
    }

    private void ShowFeedback(string message, float seconds)
    {
        if (feedback == null) return;
        feedback.text = message;
        feedback.enabled = true;
        feedbackUntil = Time.time + seconds;
    }

    private void OnDisable()
    {
        if (inventory != null) inventory.Changed -= OnInventoryChanged;
        if (dayNight != null) dayNight.PhaseChanged -= OnPhaseChanged;
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }
}
