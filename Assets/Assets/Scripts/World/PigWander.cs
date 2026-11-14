using System.Collections;
using UnityEngine;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(SpriteRenderer))]
public sealed class PigWander : MonoBehaviour, ISwordTarget
{
    [SerializeField] private Tilemap ground;
    [SerializeField] private Sprite[] walkFrames;
    [SerializeField] private Sprite[] walkDownFrames;
    [SerializeField] private Sprite[] walkUpFrames;
    [SerializeField] private Sprite[] idleSideFrames;
    [SerializeField] private Sprite[] idleDownFrames;
    [SerializeField] private Sprite[] idleUpFrames;
    [SerializeField] private Sprite[] grazeFrames;
    [SerializeField] private Sprite[] lieDownFrames;
    [SerializeField] private Sprite[] wakeFrames;
    [SerializeField] private Sprite[] sleepFrames;
    [SerializeField] private Sprite[] damageSideFrames;
    [SerializeField] private Sprite[] damageDownFrames;
    [SerializeField] private Sprite[] damageUpFrames;
    [SerializeField, Min(0f)] private float speed = 0.8f;
    [SerializeField, Min(0.1f)] private float animationRate = 6f;
    [SerializeField, Min(1f)] private float pixelsPerUnit = 100f;
    [Header("Sleep and escape")]
    [SerializeField, Range(0f, 1f)] private float sleepChance = 0.02f;
    [SerializeField, Min(0f)] private float sleepCooldown = 45f;
    [SerializeField, Min(0f)] private float wakeDistance = 2f;
    [SerializeField, Min(0f)] private float fleeSpeed = 2f;
    [SerializeField, Min(0f)] private float fleeDuration = 3f;
    [SerializeField] private bool fleeOnApproach;
    [Tooltip("Enable when the side-view sprites face right instead of left.")]
    [SerializeField] private bool sideSpritesFaceRight;
    [Header("Health and collision")]
    [SerializeField, Min(1)] private int maxHealth = 3;
    [SerializeField] private Collider2D animalCollider;
    [Header("Hit feedback")]
    [SerializeField, Min(0f)] private float knockbackDistance = 0.35f;
    [SerializeField, Min(0.01f)] private float hitDuration = 0.4f;
    [SerializeField, Min(0.01f)] private float deathFadeDuration = 0.65f;
    private SpriteRenderer spriteRenderer;
    private Vector2 direction;
    private float nextDecision;
    private bool initialized;
    private Vector3 logicalPosition;
    private Vector2 facing = Vector2.left;
    private Coroutine actionRoutine;
    private bool performingAction;
    private bool sleeping;
    private float nextSleepTime;
    private float fleeUntil;
    private Transform player;
    private int health;
    private bool dying;
    private Vector2 hitDirection;
    private Vector3 originalScale;
    private Color originalColor;

    public bool CanBeHit => initialized && !dying && isActiveAndEnabled;
    public Collider2D HitCollider => animalCollider;

    private IEnumerator Start()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
        originalColor = spriteRenderer.color;
        health = Mathf.Max(1, maxHealth);
        if (animalCollider == null) animalCollider = GetComponent<Collider2D>();
        if (animalCollider != null) animalCollider.enabled = false;
        spriteRenderer.enabled = false;
        // Wait for the procedural island and player spawn to finish.
        for (int attempt = 0; attempt < 300 && !initialized; attempt++)
        {
            yield return null;
            if (ground == null || !ground.HasTile(ground.WorldToCell(transform.position)))
            {
                PlayerMovement player = FindAnyObjectByType<PlayerMovement>();
                if (ground == null || player == null) continue;
                for (int i = 0; i < 24; i++)
                {
                    Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(2f, 4f);
                    Vector3 candidate = player.transform.position + (Vector3)offset;
                    if (!ground.HasTile(ground.WorldToCell(candidate))) continue;
                    if (Physics2D.OverlapCircle(candidate, 0.35f) != null) continue;
                    bool tooClose = false;
                    foreach (PigWander other in FindObjectsByType<PigWander>(FindObjectsInactive.Exclude))
                    {
                        if (other != this && other.initialized &&
                            ((Vector2)(other.transform.position - candidate)).sqrMagnitude < 1f)
                        {
                            tooClose = true;
                            break;
                        }
                    }
                    if (tooClose) continue;
                    transform.position = candidate;
                    initialized = true;
                    break;
                }
            }
            else initialized = true;
        }
        logicalPosition = transform.position;
        PlayerMovement playerMovement = FindAnyObjectByType<PlayerMovement>();
        if (playerMovement != null) player = playerMovement.transform;
        nextSleepTime = Time.time + sleepCooldown;
        spriteRenderer.enabled = initialized;
        if (animalCollider != null) animalCollider.enabled = initialized;
        if (!initialized) Debug.LogWarning("PigWander: No free grass position found.", this);
    }

    private void Update()
    {
        if (!initialized) return;
        if (sleeping && PlayerIsNear())
        {
            if (actionRoutine != null) StopCoroutine(actionRoutine);
            actionRoutine = StartCoroutine(WakeAndFlee());
        }
        if (fleeOnApproach && !sleeping && !performingAction && PlayerIsNear())
            fleeUntil = Time.time + fleeDuration;
        if (performingAction) return;
        bool fleeing = Time.time < fleeUntil && player != null;
        if (fleeing)
        {
            direction = FindEscapeDirection();
            if (direction != Vector2.zero) facing = direction;
        }
        else if (Time.time >= nextDecision)
        {
            int choice = Random.Range(0, 10);
            if (Time.time >= nextSleepTime && !PlayerIsNear() && Random.value < sleepChance)
            {
                actionRoutine = StartCoroutine(Sleep());
                return;
            }
            if (choice == 8)
            {
                actionRoutine = StartCoroutine(Graze());
                return;
            }
            direction = choice == 0 ? Vector2.left : choice == 1 ? Vector2.right
                : choice == 2 ? Vector2.up : choice == 3 ? Vector2.down : Vector2.zero;
            nextDecision = Time.time + Random.Range(1.5f, 3.5f);
            if (direction != Vector2.zero) facing = direction;
        }

        Vector3 next = logicalPosition + (Vector3)(direction * ((fleeing ? fleeSpeed : speed) * Time.deltaTime));
        if (ground.HasTile(ground.WorldToCell(next)) && CanMoveTo(next)) logicalPosition = next;
        else { direction = Vector2.zero; nextDecision = Time.time; }
        float ppu = Mathf.Max(1f, pixelsPerUnit);
        transform.position = new Vector3(Mathf.Round(logicalPosition.x * ppu) / ppu,
            Mathf.Round(logicalPosition.y * ppu) / ppu, logicalPosition.z);

        bool moving = direction != Vector2.zero;
        Sprite[] frames = facing.y > 0 ? (moving ? walkUpFrames : idleUpFrames)
            : facing.y < 0 ? (moving ? walkDownFrames : idleDownFrames)
            : (moving ? walkFrames : idleSideFrames);
        spriteRenderer.flipX = facing.x != 0f && ((facing.x > 0f) != sideSpritesFaceRight);
        if (frames != null && frames.Length > 0)
        {
            int frame = (int)(Time.time * (moving ? animationRate * (fleeing ? 1.5f : 1f) : 2f)) % frames.Length;
            if (frames[frame] != null) spriteRenderer.sprite = frames[frame];
        }
    }

    private IEnumerator Graze()
    {
        performingAction = true;
        direction = Vector2.zero;
        yield return PlayFrames(grazeFrames, 5f, false);
        FinishAction();
    }

    private IEnumerator Sleep()
    {
        performingAction = true;
        sleeping = true;
        nextSleepTime = Time.time + sleepCooldown;
        direction = Vector2.zero;
        yield return PlayFrames(lieDownFrames, 6f, false);
        float until = Time.time + Random.Range(4f, 8f);
        while (Time.time < until)
        {
            if (sleepFrames != null && sleepFrames.Length > 0)
                spriteRenderer.sprite = sleepFrames[(int)(Time.time * 4f) % sleepFrames.Length];
            yield return null;
        }
        yield return PlayFrames(wakeFrames, 6f, false);
        sleeping = false;
        FinishAction();
    }

    private IEnumerator WakeAndFlee()
    {
        sleeping = false;
        performingAction = true;
        yield return PlayFrames(wakeFrames, 10f, false);
        fleeUntil = Time.time + fleeDuration;
        FinishAction();
    }

    private bool PlayerIsNear()
    {
        return player != null && player.gameObject.activeInHierarchy
            && ((Vector2)(player.position - transform.position)).sqrMagnitude <= wakeDistance * wakeDistance;
    }

    private Vector2 FindEscapeDirection()
    {
        Vector2 away = (Vector2)(transform.position - player.position);
        Vector2[] choices = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        Vector2 best = Vector2.zero;
        float bestScore = float.NegativeInfinity;
        foreach (Vector2 candidate in choices)
        {
            Vector2 probe = (Vector2)logicalPosition + candidate * 0.35f;
            if (!ground.HasTile(ground.WorldToCell(probe))) continue;
            if (!CanMoveTo(probe)) continue;
            float score = Vector2.Dot(away, candidate);
            if (score > bestScore) { bestScore = score; best = candidate; }
        }
        return best;
    }

    private bool CanMoveTo(Vector2 position)
    {
        Vector2 center = animalCollider != null
            ? position + (Vector2)(animalCollider.bounds.center - transform.position)
            : position;
        float radius = animalCollider != null
            ? Mathf.Max(0.05f, Mathf.Min(animalCollider.bounds.extents.x, animalCollider.bounds.extents.y))
            : 0.1f;
        foreach (Collider2D hit in Physics2D.OverlapCircleAll(center, radius))
        {
            if (hit == null || hit.isTrigger || hit == animalCollider || hit.transform.IsChildOf(transform))
                continue;
            return false;
        }
        return true;
    }

    /// <summary>Applies sword damage and starts the animal's hit reaction.</summary>
    public bool TakeDamage(int amount)
    {
        Vector2 away = player != null ? (Vector2)(transform.position - player.position) : Vector2.zero;
        return TakeDamage(amount, away);
    }

    public bool TakeDamage(int amount, Vector2 knockbackDirection)
    {
        if (!CanBeHit || amount <= 0) return false;
        hitDirection = knockbackDirection.sqrMagnitude > 0.0001f ? knockbackDirection.normalized : Vector2.zero;
        health = Mathf.Max(0, health - amount);
        dying = health == 0;
        PlayDamageAnimation();
        return true;
    }

    /// <summary>Plays the hit animation. Call from a future combat system.</summary>
    public void PlayDamageAnimation()
    {
        if (!initialized || !isActiveAndEnabled) return;
        if (actionRoutine != null) StopCoroutine(actionRoutine);
        sleeping = false;
        actionRoutine = StartCoroutine(DamageReaction());
    }

    private IEnumerator DamageReaction()
    {
        performingAction = true;
        direction = Vector2.zero;
        Sprite[] frames = facing.y > 0 ? damageUpFrames : facing.y < 0 ? damageDownFrames : damageSideFrames;
        float elapsed = 0f;
        float lastOffset = 0f;
        while (elapsed < hitDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / hitDuration);
            float offset = knockbackDistance * (1f - (1f - t) * (1f - t));
            Vector3 candidate = logicalPosition + (Vector3)(hitDirection * (offset - lastOffset));
            if (ground != null && ground.HasTile(ground.WorldToCell(candidate)) && CanMoveTo(candidate))
                logicalPosition = candidate;
            lastOffset = offset;
            SetPixelPosition();
            float squash = Mathf.Sin(t * Mathf.PI) * 0.12f;
            transform.localScale = Vector3.Scale(originalScale, new Vector3(1f + squash, 1f - squash, 1f));
            if (frames != null && frames.Length > 0)
            {
                int index = Mathf.Min(frames.Length - 1, (int)(t * frames.Length));
                if (frames[index] != null) spriteRenderer.sprite = frames[index];
            }
            spriteRenderer.flipX = facing.x != 0f && ((facing.x > 0f) != sideSpritesFaceRight);
            yield return null;
        }
        transform.localScale = originalScale;
        if (dying)
        {
            spriteRenderer.flipX = false;
            yield return PlayFrames(lieDownFrames, 8f, false);
            if (animalCollider != null) animalCollider.enabled = false;
            elapsed = 0f;
            while (elapsed < deathFadeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / deathFadeDuration);
                Color color = originalColor;
                color.a *= 1f - t;
                spriteRenderer.color = color;
                yield return null;
            }
            if (MiniGameController.Instance != null)
                MiniGameController.Instance.SpawnFood(transform.position);
            Destroy(gameObject);
            yield break;
        }
        fleeUntil = Time.time + fleeDuration;
        FinishAction();
    }

    private void SetPixelPosition()
    {
        float ppu = Mathf.Max(1f, pixelsPerUnit);
        transform.position = new Vector3(Mathf.Round(logicalPosition.x * ppu) / ppu,
            Mathf.Round(logicalPosition.y * ppu) / ppu, logicalPosition.z);
    }

    private IEnumerator PlayFrames(Sprite[] frames, float rate, bool useFacing)
    {
        if (frames == null || frames.Length == 0) yield break;
        spriteRenderer.flipX = useFacing && facing.x != 0f && ((facing.x > 0f) != sideSpritesFaceRight);
        foreach (Sprite frame in frames)
        {
            if (frame != null) spriteRenderer.sprite = frame;
            yield return new WaitForSeconds(1f / rate);
        }
    }

    private void FinishAction()
    {
        performingAction = false;
        actionRoutine = null;
        nextDecision = Time.time + Random.Range(0.5f, 1.5f);
    }
}
