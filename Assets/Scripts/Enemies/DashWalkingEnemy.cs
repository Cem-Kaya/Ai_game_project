using UnityEngine;

/// <summary>
/// A ground-based enemy that patrols platforms, chases the player on sight and performs a fast dash when close enough.
/// Uses sensors to avoid walking into walls or off ledges.  Dash behaviour is gated by a cooldown to prevent spamming.
/// </summary>
public class DashWalkingEnemy : EnemyBase
{
    [Header("Patrol")]
    [SerializeField] private float patrolSpeed = 1.6f;
    [Tooltip("-1 = start left, +1 = start right")]
    [SerializeField] private int startDirection = 1;

    [Header("Sensors (place as child transforms)")]
    [SerializeField] private Transform frontCheck;   // feet level, slightly ahead
    [SerializeField] private Transform groundCheck;  // feet level, slightly ahead
    [SerializeField] private float wallCheckDistance = 0.25f;
    [SerializeField] private float groundCheckDistance = 0.35f;

    [Header("Chase & Dash")]
    [Tooltip("Distance within which the enemy can see and start chasing the player.")]
    [SerializeField] private float sightRange = 4f;
    [Tooltip("Vertical tolerance for sight checks so the enemy doesn't chase players far above or below.")]
    [SerializeField] private float sightVerticalTolerance = 1.5f;
    [SerializeField] private float chaseSpeed = 2f;
    [Tooltip("Horizontal distance to the player that triggers the dash.")]
    [SerializeField] private float dashRange = 2f;
    [SerializeField] private float dashSpeed = 6f;
    [SerializeField] private float dashDuration = 0.35f;
    [SerializeField] private float dashCooldown = 2f;

    [Header("Player Detection")]
    [Tooltip("Layer mask used to identify the player for detection queries.")]
    [SerializeField] private LayerMask playerMask;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private int direction;
    private float dashTimer;
    private float cooldownTimer;

    protected override void OnEnable()
    {
        base.OnEnable();
        direction = Mathf.Sign(startDirection) == 0f ? 1 : (startDirection > 0 ? 1 : -1);
        SetState(EnemyState.Patrol);
        // Face initial direction
        if ((direction > 0 && !facingRight) || (direction < 0 && facingRight))
        {
            Flip();
            MirrorSensors();
        }
        dashTimer = 0f;
        cooldownTimer = 0f;
    }

    private void FixedUpdate()
    {
        if (state == EnemyState.Dead) return;

        // update timers
        if (cooldownTimer > 0f) cooldownTimer -= Time.fixedDeltaTime;
        if (dashTimer > 0f)
        {
            dashTimer -= Time.fixedDeltaTime;
            if (dashTimer <= 0f)
            {
                // dash ends: stop horizontal motion and return to chasing
                rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
                SetState(EnemyState.Chase);
                cooldownTimer = dashCooldown;
            }
        }

        // Detect player in range
        Transform target = DetectPlayer();
        bool sees = target != null;

        if (state == EnemyState.Patrol)
        {
            if (sees) SetState(EnemyState.Chase);
            Patrol();
            return;
        }
        else if (state == EnemyState.Chase)
        {
            if (!sees)
            {
                SetState(EnemyState.Patrol);
                return;
            }
            // attempt dash if conditions are met
            if (target != null)
            {
                float horizontalDistance = Mathf.Abs(target.position.x - transform.position.x);
                if (cooldownTimer <= 0f && horizontalDistance <= dashRange && HasLineOfSight(target))
                {
                    StartDash(target);
                    return;
                }
            }
            Chase(target);
            return;
        }
        else if (state == EnemyState.Attack)
        {
            // dashing behaviour handled by timer above
            return;
        }
        else if (state == EnemyState.Hurt)
        {
            // simple recovery from hurt
            SetState(EnemyState.Patrol);
            return;
        }
    }

    private void Patrol()
    {
        Vector2 facingVec = facingRight ? Vector2.right : Vector2.left;
        bool wallAhead = Physics2D.Raycast(frontCheck.position, facingVec, wallCheckDistance, groundLayer);
        bool groundAhead = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckDistance, groundLayer);

        if (wallAhead || !groundAhead)
        {
            direction *= -1;
            Flip();
            MirrorSensors();
        }

        Move(direction * patrolSpeed);
    }

    private void Chase(Transform target)
    {
        if (target == null)
        {
            SetState(EnemyState.Patrol);
            return;
        }
        float diff = target.position.x - transform.position.x;
        int desiredDir = diff >= 0f ? 1 : -1;
        if ((desiredDir > 0 && !facingRight) || (desiredDir < 0 && facingRight))
        {
            Flip();
            MirrorSensors();
        }
        Vector2 facingVec = facingRight ? Vector2.right : Vector2.left;
        bool wallAhead = Physics2D.Raycast(frontCheck.position, facingVec, wallCheckDistance, groundLayer);
        bool groundAhead = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckDistance, groundLayer);
        if (wallAhead || !groundAhead)
        {
            Move(0f);
            return;
        }
        Move(desiredDir * chaseSpeed);
    }

    private void StartDash(Transform target)
    {
        if (target == null) return;
        SetState(EnemyState.Attack);
        dashTimer = dashDuration;
        float diff = target.position.x - transform.position.x;
        int desiredDir = diff >= 0f ? 1 : -1;
        direction = desiredDir;
        if ((desiredDir > 0 && !facingRight) || (desiredDir < 0 && facingRight))
        {
            Flip();
            MirrorSensors();
        }
        Vector2 dashVel = new Vector2(desiredDir * dashSpeed, rb.linearVelocity.y);
        rb.linearVelocity = dashVel;
    }

    /// <summary>
    /// Basic visibility check: determines whether the player is within horizontal/vertical range and not obstructed by ground.
    /// </summary>
    /// <summary>
    /// Detects a player within sightRange using OverlapCircle on the playerMask.  Ensures the vertical difference is within
    /// sightVerticalTolerance and that the target is in front of the enemy.  Returns the player's transform if found, otherwise null.
    /// </summary>
    private Transform DetectPlayer()
    {
        Collider2D hit = Physics2D.OverlapCircle(transform.position, sightRange, playerMask);
        if (hit == null) return null;
        Transform target = hit.transform;
        float yDiff = Mathf.Abs(target.position.y - transform.position.y);
        if (yDiff > sightVerticalTolerance) return null;
        float xDiff = target.position.x - transform.position.x;
        if (facingRight && xDiff < 0f) return null;
        if (!facingRight && xDiff > 0f) return null;
        return target;
    }

    /// <summary>
    /// Checks if there is a clear path to the specified target by raycasting between the enemy and the target using the groundLayer mask.
    /// </summary>
    private bool HasLineOfSight(Transform target)
    {
        if (target == null) return false;
        Vector2 origin = frontCheck ? (Vector2)frontCheck.position : (Vector2)transform.position;
        Vector2 toTarget = (Vector2)target.position - origin;
        RaycastHit2D hit = Physics2D.Raycast(origin, toTarget.normalized, toTarget.magnitude, groundLayer);
        return hit.collider == null;
    }

    /// <summary>
    /// Mirror sensor local positions so they remain in front of the enemy after flipping.  Uses localPosition so world-space flips are correct when SpriteRenderer.flipX is used.
    /// </summary>
    private void MirrorSensors()
    {
        if (frontCheck)
        {
            Vector3 local = frontCheck.localPosition;
            frontCheck.localPosition = new Vector3(-local.x, local.y, local.z);
        }
        if (groundCheck)
        {
            Vector3 local = groundCheck.localPosition;
            groundCheck.localPosition = new Vector3(-local.x, local.y, local.z);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (frontCheck)
        {
            Vector2 facingVec = facingRight ? Vector2.right : Vector2.left;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(frontCheck.position, frontCheck.position + (Vector3)(facingVec * wallCheckDistance));
        }
        if (groundCheck)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(groundCheck.position, groundCheck.position + Vector3.down * groundCheckDistance);
        }
        // sight range visualization
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position + (Vector3)(facingRight ? Vector2.right : Vector2.left) * sightRange * 0.5f,
            new Vector3(sightRange, sightVerticalTolerance * 2f, 0.1f));
    }
}