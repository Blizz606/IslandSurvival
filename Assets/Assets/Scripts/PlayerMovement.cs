using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Bewegung")]
    [SerializeField, Min(0f)] private float moveSpeed = 5f;
    [SerializeField] private bool blockPhysicsColliders = true;
    [SerializeField, Min(0.01f)] private float collisionRadius = 0.2f;

    [Header("Begehbare Tilemaps")]
    [SerializeField] private Tilemap grassTilemap;
    [SerializeField] private Tilemap grassOverlayTilemap;
    [SerializeField] private Tilemap sandTilemap;

    [Header("Animation")]
    [SerializeField] private Animator animator;

    [Header("Aktion")]
    [Tooltip("Taste zum Auslösen der Holzfäll-Animation.")]
    [SerializeField] private Key chopKey = Key.R;
    [Tooltip("Trigger-Name im Animator. Der Standard ist Chop.")]
    [SerializeField] private string chopTrigger = "Chop";
    [Tooltip("Vollständiger Name des Chop-States im Animator.")]
    [SerializeField] private string chopStateName = "Base Layer.Chop";
    [Tooltip("Sicherheit: Bewegung wird wieder freigegeben, falls der Animator falsch verbunden ist.")]
    [SerializeField, Min(0.1f)] private float chopFailSafeDuration = 2f;

    [Header("Kamera")]
    [SerializeField] private bool followCamera = true;
    [SerializeField, Min(0f)] private float cameraSmoothTime = 0.15f;

    [Header("Darstellung")]
    [SerializeField] private string renderSortingLayer = "Default";
    [SerializeField] private int playerSortingOrder = 10;

    private static readonly int IsMoving = Animator.StringToHash("IsMoving");
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int MoveY = Animator.StringToHash("MoveY");
    private static readonly int LastMoveX = Animator.StringToHash("LastMoveX");
    private static readonly int LastMoveY = Animator.StringToHash("LastMoveY");

    private Rigidbody2D body;
    private Camera mainCamera;
    private SpriteRenderer[] playerRenderers;
    private Vector2 input;
    private Vector2 facingDirection = Vector2.down;
    private Vector3 cameraVelocity;
    private bool hasIsMoving;
    private bool hasMoveX;
    private bool hasMoveY;
    private bool hasLastMoveX;
    private bool hasLastMoveY;
    private bool hasChopTrigger;
    private int chopTriggerHash;
    private bool isChopping;
    private bool hasEnteredChopState;
    private float chopStartTime;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.freezeRotation = true;

        if (animator == null)
            animator = GetComponent<Animator>();

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        chopTriggerHash = Animator.StringToHash(chopTrigger);
        CacheAnimatorParameters();

        mainCamera = Camera.main;
        ConfigurePlayerRenderers();
    }

    private void Update()
    {
        UpdateChopLock();
        input = isChopping ? Vector2.zero : ReadInput();

        if (input.sqrMagnitude > 0f)
            facingDirection = GetCardinalDirection(input);

        UpdateAnimator();

        if (!isChopping)
            TryTriggerChop();
    }

    private void FixedUpdate()
    {
        if (input.sqrMagnitude <= 0f)
            return;

        Vector2 targetPosition = body.position + input * moveSpeed * Time.fixedDeltaTime;
        if (!IsWalkable(targetPosition) || IsBlockedByCollider(targetPosition))
            return;

        body.MovePosition(targetPosition);
    }

    private Vector2 ReadInput()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return Vector2.zero;

        float horizontal = 0f;
        float vertical = 0f;

        if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) horizontal -= 1f;
        if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) horizontal += 1f;
        if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) vertical -= 1f;
        if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) vertical += 1f;

        return new Vector2(horizontal, vertical).normalized;
    }

    private Vector2 GetCardinalDirection(Vector2 direction)
    {
        if (Mathf.Abs(direction.x) > Mathf.Abs(direction.y))
            return new Vector2(Mathf.Sign(direction.x), 0f);

        return new Vector2(0f, Mathf.Sign(direction.y));
    }

    private void UpdateAnimator()
    {
        if (animator == null)
            return;

        bool isMoving = input.sqrMagnitude > 0f;
        if (hasIsMoving) animator.SetBool(IsMoving, isMoving);
        if (hasMoveX) animator.SetFloat(MoveX, isMoving ? facingDirection.x : 0f);
        if (hasMoveY) animator.SetFloat(MoveY, isMoving ? facingDirection.y : 0f);
        if (hasLastMoveX) animator.SetFloat(LastMoveX, facingDirection.x);
        if (hasLastMoveY) animator.SetFloat(LastMoveY, facingDirection.y);
    }

    private void TryTriggerChop()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard[chopKey].wasPressedThisFrame)
            return;

        TryStartChop();
    }

    /// <summary>Starts the chopping action. Used by the R key and developer tools.</summary>
    public bool TryStartChop()
    {
        if (isChopping || !hasChopTrigger || animator == null)
            return false;

        isChopping = true;
        hasEnteredChopState = false;
        chopStartTime = Time.unscaledTime;
        input = Vector2.zero;
        animator.SetTrigger(chopTriggerHash);
        return true;
    }

    private void UpdateChopLock()
    {
        if (!isChopping)
            return;

        // The state becomes active one Animator update after the trigger was set.
        // Once it has completed and returned to Idle/Walk, movement is unlocked.
        if (animator != null)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.IsName(chopStateName))
            {
                hasEnteredChopState = true;
                return;
            }

            if (hasEnteredChopState && !animator.IsInTransition(0))
            {
                isChopping = false;
                return;
            }
        }

        // Prevents a permanent movement lock when a Chop transition is configured incorrectly.
        if (Time.unscaledTime - chopStartTime >= chopFailSafeDuration)
            isChopping = false;
    }

    private void CacheAnimatorParameters()
    {
        if (animator == null)
        {
            Debug.LogWarning("PlayerMovement: Kein Animator gefunden. Die Bewegung funktioniert, aber Animationen werden nicht abgespielt.", this);
            return;
        }

        if (animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("PlayerMovement: Der Animator hat keinen Controller. Weise den Player.controller im Inspector zu.", animator);
            return;
        }

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.nameHash == IsMoving && parameter.type == AnimatorControllerParameterType.Bool)
                hasIsMoving = true;
            else if (parameter.nameHash == MoveX && parameter.type == AnimatorControllerParameterType.Float)
                hasMoveX = true;
            else if (parameter.nameHash == MoveY && parameter.type == AnimatorControllerParameterType.Float)
                hasMoveY = true;
            else if (parameter.nameHash == LastMoveX && parameter.type == AnimatorControllerParameterType.Float)
                hasLastMoveX = true;
            else if (parameter.nameHash == LastMoveY && parameter.type == AnimatorControllerParameterType.Float)
                hasLastMoveY = true;
            else if (parameter.nameHash == chopTriggerHash && parameter.type == AnimatorControllerParameterType.Trigger)
                hasChopTrigger = true;
        }
    }

    private bool IsWalkable(Vector2 worldPosition)
    {
        return HasTileAt(grassTilemap, worldPosition)
            || HasTileAt(grassOverlayTilemap, worldPosition)
            || HasTileAt(sandTilemap, worldPosition);
    }

    private static bool HasTileAt(Tilemap tilemap, Vector2 worldPosition)
    {
        return tilemap != null && tilemap.HasTile(tilemap.WorldToCell(worldPosition));
    }

    private bool IsBlockedByCollider(Vector2 worldPosition)
    {
        if (!blockPhysicsColliders)
            return false;

        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPosition, collisionRadius);
        foreach (Collider2D hit in hits)
        {
            if (hit.isTrigger || hit.transform == transform || hit.transform.IsChildOf(transform))
                continue;

            return true;
        }

        return false;
    }

    private void ConfigurePlayerRenderers()
    {
        playerRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        foreach (SpriteRenderer spriteRenderer in playerRenderers)
        {
            if (!string.IsNullOrEmpty(renderSortingLayer))
                spriteRenderer.sortingLayerName = renderSortingLayer;

            spriteRenderer.sortingOrder = playerSortingOrder;
        }
    }

    private void UpdateSpriteFacing()
    {
        if (playerRenderers == null || playerRenderers.Length == 0)
            playerRenderers = GetComponentsInChildren<SpriteRenderer>(true);

        // The right-facing animation is reused for the left side.
        // Up/down sprites remain unflipped, so a previous left-facing idle
        // cannot accidentally carry over into a vertical idle animation.
        bool flipX = facingDirection.x < 0f;
        foreach (SpriteRenderer spriteRenderer in playerRenderers)
        {
            if (spriteRenderer != null)
                spriteRenderer.flipX = flipX;
        }
    }

    private void LateUpdate()
    {
        UpdateSpriteFacing();

        if (!followCamera)
            return;

        if (mainCamera == null)
            mainCamera = Camera.main;

        if (mainCamera == null)
            return;

        Vector3 targetPosition = transform.position;
        targetPosition.z = mainCamera.transform.position.z;
        mainCamera.transform.position = Vector3.SmoothDamp(
            mainCamera.transform.position,
            targetPosition,
            ref cameraVelocity,
            cameraSmoothTime
        );
    }
}
