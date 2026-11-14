using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Spawns a bounded number of zombies on walkable land during the night.</summary>
public sealed class ZombieNightSpawner : MonoBehaviour
{
    [SerializeField] private DayNightCycle cycle;
    [SerializeField] private PlayerMovement player;
    [SerializeField] private HeartBar hearts;
    [SerializeField] private Tilemap grass;
    [SerializeField] private Tilemap sand;
    [Tooltip("Zombie spritesheets in the same order as crawler flags.")]
    [SerializeField] private Texture2D[] zombieSheets;
    [SerializeField] private bool[] crawlerSheets;
    [SerializeField, Min(1)] private int maxZombies = 6;
    [SerializeField, Min(0.1f)] private float spawnInterval = 8f;
    [SerializeField, Min(0.5f)] private float spawnMinDistance = 5f;
    [SerializeField, Min(1f)] private float spawnMaxDistance = 9f;
    [SerializeField, Min(0f)] private float playerInvulnerability = 1f;
    private readonly List<IslandZombie> zombies = new();
    private readonly List<Sprite[,]> cachedFrames = new();
    private float nextSpawn;
    private float nextPlayerHit;
    private int nightNumber;
    public int NightNumber => nightNumber;
    public int CurrentZombieLimit => Mathf.Min(24, maxZombies + Mathf.Max(0, nightNumber - 1) * 2);
    public float CurrentSpawnInterval => Mathf.Max(1.5f, spawnInterval * Mathf.Pow(0.88f, Mathf.Max(0, nightNumber - 1)));
    public int CurrentZombieHealth => 3 + Mathf.Min(5, Mathf.Max(0, nightNumber - 1) / 3);
    public float CurrentSpeedMultiplier => Mathf.Min(1.6f, 1f + Mathf.Max(0, nightNumber - 1) * 0.04f);

    private void Awake()
    {
        if (cycle == null) cycle = GetComponent<DayNightCycle>();
        if (player == null) player = FindAnyObjectByType<PlayerMovement>();
        if (hearts == null) hearts = FindAnyObjectByType<HeartBar>();
        if (cycle != null) cycle.PhaseChanged += OnPhaseChanged;
        BuildFrames();
    }

    private void Update()
    {
        if (cycle == null || !cycle.IsNight || player == null || cachedFrames.Count == 0) return;
        zombies.RemoveAll(zombie => zombie == null);
        if (zombies.Count >= CurrentZombieLimit || Time.time < nextSpawn) return;
        nextSpawn = Time.time + CurrentSpawnInterval;
        SpawnOne();
    }

    private void OnPhaseChanged(bool night)
    {
        nextSpawn = Time.time;
        if (night) { nightNumber++; return; }
        foreach (IslandZombie zombie in zombies)
            if (zombie != null && !zombie.IsDying) Destroy(zombie.gameObject);
        zombies.Clear();
    }

    public void TryDamagePlayer()
    {
        if (hearts == null) hearts = FindAnyObjectByType<HeartBar>();
        if (hearts == null || hearts.CurrentHealth <= 0 || Time.time < nextPlayerHit) return;
        nextPlayerHit = Time.time + playerInvulnerability;
        hearts.TakeDamage(1);
    }

    private void SpawnOne()
    {
        float min = Mathf.Max(0.5f, spawnMinDistance);
        float max = Mathf.Max(min, spawnMaxDistance);
        for (int attempt = 0; attempt < 40; attempt++)
        {
            Vector2 position = (Vector2)player.transform.position + Random.insideUnitCircle.normalized * Random.Range(min, max);
            bool walkable = (grass != null && grass.HasTile(grass.WorldToCell(position)))
                || (sand != null && sand.HasTile(sand.WorldToCell(position)));
            if (!walkable || Physics2D.OverlapCircle(position, 0.5f) != null) continue;
            int index = Random.Range(0, cachedFrames.Count);
            if (cachedFrames[index] == null) continue;
            GameObject instance = new GameObject("Night Zombie");
            instance.transform.position = position;
            instance.transform.SetParent(transform, true);
            IslandZombie zombie = instance.AddComponent<IslandZombie>();
            bool crawler = crawlerSheets != null && index < crawlerSheets.Length && crawlerSheets[index];
            zombie.Configure(cachedFrames[index], crawler, this, player, grass, sand, CurrentZombieHealth, CurrentSpeedMultiplier);
            zombies.Add(zombie);
            return;
        }
    }

    private void BuildFrames()
    {
        cachedFrames.Clear();
        if (zombieSheets == null) return;
        foreach (Texture2D sheet in zombieSheets)
        {
            if (sheet == null || sheet.width < 64 || sheet.height < 96 || sheet.height % 16 != 0)
            {
                cachedFrames.Add(null);
                continue;
            }
            int rows = sheet.height / 16;
            Sprite[,] frames = new Sprite[rows, 4];
            for (int row = 0; row < rows; row++)
                for (int col = 0; col < 4; col++)
                {
                    Sprite frame = Sprite.Create(sheet, new Rect(col * 16, sheet.height - (row + 1) * 16, 16, 16), new Vector2(0.5f, 0.5f), 16f);
                    frame.name = $"{sheet.name}_r{row}_f{col}";
                    frames[row, col] = frame;
                }
            cachedFrames.Add(frames);
        }
    }

    private void OnDestroy()
    {
        if (cycle != null) cycle.PhaseChanged -= OnPhaseChanged;
        foreach (Sprite[,] sheet in cachedFrames)
            if (sheet != null)
                foreach (Sprite sprite in sheet)
                    if (sprite != null) Destroy(sprite);
    }
}
