using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>One directional, animated nighttime enemy.</summary>
[RequireComponent(typeof(SpriteRenderer), typeof(CircleCollider2D))]
public sealed class IslandZombie : MonoBehaviour, ISwordTarget
{
    [Header("Death")]
    [SerializeField, Min(0f)] private float bodyHoldSeconds = 1f;
    [SerializeField, Min(0.01f)] private float bodyFadeSeconds = 2f;
    private Sprite[,] frames;
    private bool crawler;
    private ZombieNightSpawner spawner;
    private PlayerMovement player;
    private Tilemap ground;
    private Tilemap beach;
    private SpriteRenderer spriteRenderer;
    private CircleCollider2D hitCollider;
    private int health = 3;
    private float speedMultiplier = 1f;
    private int facing;
    private bool busy;
    private bool dying;
    private float nextAttack;
    private Vector2 logicalPosition;
    private Coroutine reaction;
    private static readonly int[] WalkOrder = { 0, 1, 2, 1 };

    public bool CanBeHit => !dying && isActiveAndEnabled;
    public Collider2D HitCollider => hitCollider;
    public int CurrentHealth => Mathf.Max(0, health);
    public bool IsDying => dying;

    public void Configure(Sprite[,] sprites, bool isCrawler, ZombieNightSpawner owner,
        PlayerMovement target, Tilemap grass, Tilemap sand, int startingHealth, float movementMultiplier)
    {
        frames = sprites;
        crawler = isCrawler;
        spawner = owner;
        player = target;
        ground = grass;
        beach = sand;
        health = Mathf.Max(1, startingHealth);
        speedMultiplier = Mathf.Max(0.1f, movementMultiplier);
        spriteRenderer = GetComponent<SpriteRenderer>();
        spriteRenderer.sortingLayerName = "Default";
        spriteRenderer.sortingOrder = 10;
        hitCollider = GetComponent<CircleCollider2D>();
        hitCollider.radius = 0.22f;
        logicalPosition = transform.position;
        SetFrame(0, 1);
    }

    private void Update()
    {
        if (frames == null || player == null || dying || busy) return;
        Vector2 toPlayer = (Vector2)player.transform.position - logicalPosition;
        float distance = toPlayer.magnitude;
        if (distance < 0.8f)
        {
            SetFacing(toPlayer);
            if (Time.time >= nextAttack)
                reaction = StartCoroutine(Attack());
            else SetFrame(facing, 1);
            return;
        }

        Vector2 desired = toPlayer.normalized;
        Vector2 step = desired * ((crawler ? 0.65f : 0.95f) * speedMultiplier * Time.deltaTime);
        if (!TryMove(step))
        {
            // Slide along obstacles instead of stopping at every tree.
            if (Mathf.Abs(step.x) >= Mathf.Abs(step.y))
            {
                if (!TryMove(new Vector2(step.x, 0f))) TryMove(new Vector2(0f, step.y));
            }
            else if (!TryMove(new Vector2(0f, step.y))) TryMove(new Vector2(step.x, 0f));
        }
        SetFacing(desired);
        SetFrame(facing, WalkOrder[(int)(Time.time * 6f) % WalkOrder.Length]);
    }

    private bool TryMove(Vector2 delta)
    {
        if (delta.sqrMagnitude < 0.0000001f) return false;
        Vector2 next = logicalPosition + delta;
        if (!HasGround(next)) return false;
        foreach (RaycastHit2D hit in Physics2D.CircleCastAll(logicalPosition, 0.18f, delta.normalized, delta.magnitude))
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.collider == hitCollider
                || hit.collider.transform.IsChildOf(transform)) continue;
            return false;
        }
        logicalPosition = next;
        transform.position = new Vector3(Mathf.Round(next.x * 100f) / 100f, Mathf.Round(next.y * 100f) / 100f, transform.position.z);
        return true;
    }

    private bool HasGround(Vector2 position)
    {
        return (ground != null && ground.HasTile(ground.WorldToCell(position)))
            || (beach != null && beach.HasTile(beach.WorldToCell(position)));
    }

    private void SetFacing(Vector2 direction)
    {
        if (direction.sqrMagnitude < 0.0001f) return;
        facing = Mathf.Abs(direction.x) > Mathf.Abs(direction.y)
            ? (direction.x < 0f ? 1 : 2) : (direction.y < 0f ? 0 : 3);
    }

    private void SetFrame(int row, int column)
    {
        if (frames != null && row < frames.GetLength(0) && frames[row, column] != null)
            spriteRenderer.sprite = frames[row, column];
    }

    private IEnumerator Attack()
    {
        busy = true;
        nextAttack = Time.time + 1.4f;
        int row = crawler ? facing : 4 + facing;
        for (int i = 0; i < 3; i++)
        {
            SetFrame(row, i);
            if (i == 1 && CanReachPlayer())
                spawner.TryDamagePlayer();
            yield return new WaitForSeconds(0.125f);
        }
        busy = false;
        reaction = null;
    }

    private bool CanReachPlayer()
    {
        if (player == null || Vector2.Distance(transform.position, player.transform.position) > 1.1f)
            return false;
        foreach (RaycastHit2D hit in Physics2D.LinecastAll(transform.position, player.transform.position))
        {
            if (hit.collider == null || hit.collider.isTrigger || hit.collider == hitCollider
                || hit.collider.transform.IsChildOf(transform)
                || hit.collider.transform == player.transform || hit.collider.transform.IsChildOf(player.transform))
                continue;
            return false;
        }
        return true;
    }

    public bool TakeDamage(int amount, Vector2 direction)
    {
        if (!CanBeHit || amount <= 0) return false;
        health -= amount;
        dying = health <= 0;
        if (reaction != null) StopCoroutine(reaction);
        reaction = StartCoroutine(HitReaction(direction));
        return true;
    }

    private IEnumerator HitReaction(Vector2 direction)
    {
        busy = true;
        SetFrame(crawler ? 4 : 8, facing);
        Vector2 push = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.zero;
        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            TryMove(push * (0.7f * dt));
            yield return null;
        }

        if (dying)
        {
            hitCollider.enabled = false;
            for (int i = 0; i < 4; i++)
            {
                SetFrame(crawler ? 5 : 9, i);
                yield return new WaitForSeconds(1f / 6f);
            }
            if (bodyHoldSeconds > 0f)
                yield return new WaitForSeconds(bodyHoldSeconds);
            Color original = spriteRenderer.color;
            float fadeElapsed = 0f;
            float duration = Mathf.Max(0.01f, bodyFadeSeconds);
            while (fadeElapsed < duration)
            {
                fadeElapsed += Time.deltaTime;
                Color faded = original;
                faded.a = original.a * (1f - Mathf.Clamp01(fadeElapsed / duration));
                spriteRenderer.color = faded;
                yield return null;
            }
            Color transparent = original;
            transparent.a = 0f;
            spriteRenderer.color = transparent;
            Destroy(gameObject);
            yield break;
        }
        busy = false;
        reaction = null;
    }
}
