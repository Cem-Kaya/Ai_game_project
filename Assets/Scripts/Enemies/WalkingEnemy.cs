using UnityEngine;

public class WalkingEnemy : EnemyBase
{
    [Header("Patrol")]
    [SerializeField] private float patrolSpeed = 1.6f;
    [Tooltip("-1 = start left, +1 = start right")]
    [SerializeField] private int direction = 1;

    [Header("Sensors (place as child transforms)")]
    [SerializeField] private Transform frontCheck;   // feet level, slightly ahead
    [SerializeField] private Transform groundCheck;  // feet level, slightly ahead
    [SerializeField] private float wallCheckDistance = 0.25f;
    [SerializeField] private float groundCheckDistance = 0.35f;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    [Header("Player Detection")]
    [Tooltip("Layer mask used to identify the player for detection queries.")]
    [SerializeField] private LayerMask playerMask;
    [Tooltip("Radius around the enemy within which it will detect and chase the player.")]
    [SerializeField] private float detectionRadius = 4f;
    [Tooltip("Vertical tolerance for detection; the player must be within this vertical distance to be considered.")]
    [SerializeField] private float verticalTolerance = 1.5f;
    [Tooltip("Speed used when chasing the player.")]
    [SerializeField] private float chaseSpeed = 2f;
    bool wallAhead = false;

    protected override void OnEnable()
    {
        base.OnEnable();
        SetState(EnemyState.Patrol);
        if ((direction > 0 && !facingRight) || (direction < 0 && facingRight)) Flip();
    }

    private void FixedUpdate()
    {
        if (state == EnemyState.Dead) return;

        // Detect the player within range each frame.  This does not maintain a persistent reference – it only
        // finds a target if the player is currently inside the detection sphere.  This prevents the enemy
        // from having to keep a direct reference to the player object.
        Transform target = DetectPlayer();

        // State transitions between patrol and chase
        if (state == EnemyState.Patrol && target != null)
        {
            SetState(EnemyState.Chase);
        }
        else if (state == EnemyState.Chase && target == null)
        {
            SetState(EnemyState.Patrol);
        }

        switch (state)
        {
            case EnemyState.Patrol:
                Patrol();
                break;
            case EnemyState.Chase:
                // Use the current target to chase; if target is lost mid-frame, DetectPlayer() returns null
                Chase(target);
                break;
            case EnemyState.Hurt:
                // After hurt, revert to patrol
                SetState(EnemyState.Patrol);
                break;
        }
    }

    private void Patrol()
    {
        Vector2 facing = facingRight ? Vector2.right : Vector2.left;
        RaycastHit2D hit = Physics2D.Raycast(frontCheck.position, facing, wallCheckDistance, groundLayer);
        RaycastHit2D hit2 = Physics2D.Raycast(frontCheck.position, facing, wallCheckDistance, enemyLayer);

        if (hit != false)
        {
            
            wallAhead = hit;
            if (hit.transform.tag == "Enemey")
            {
                wallAhead = true;
            }

            if (hit.transform.tag == "Player")
            {
                wallAhead = false;
            }
        }
        else
        {
            wallAhead = hit;
        }
        if (hit2 != false)
        {
            Debug.Log("AHHHHHHHHHHHH GET OUT OF THE WAY!");
            wallAhead = true;
        }


        bool groundAhead = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckDistance, groundLayer);

        if (wallAhead || !groundAhead)
        {
            
            direction *= -1;
            Flip();
            // Mirror sensor positions when turning around so raycasts still originate from the front
            if (frontCheck) frontCheck.localPosition = new Vector3(-frontCheck.localPosition.x, frontCheck.localPosition.y);
            if (groundCheck) groundCheck.localPosition = new Vector3(-groundCheck.localPosition.x, groundCheck.localPosition.y);
        }

        Move(direction * patrolSpeed);
    }

    /// <summary>
    /// Perform chasing behaviour when a player is detected.  Adjusts orientation toward the target and
    /// respects wall and ground sensors to avoid running off edges or into walls.
    /// </summary>
    private void Chase(Transform target)
    {
        if (target == null)
        {
            // No target – treat this as lost target; return to patrol next frame
            Move(0f);
            return;
        }

        // Determine desired horizontal direction relative to the target
        float diffX = target.position.x - transform.position.x;
        int desiredDir = diffX >= 0f ? 1 : -1;
        // Flip orientation if necessary
        if ((desiredDir > 0 && !facingRight) || (desiredDir < 0 && facingRight))
        {
            Flip();
            // Mirror sensor local positions for proper forward raycasts
            if (frontCheck) frontCheck.localPosition = new Vector3(-frontCheck.localPosition.x, frontCheck.localPosition.y);
            if (groundCheck) groundCheck.localPosition = new Vector3(-groundCheck.localPosition.x, groundCheck.localPosition.y);
        }
        // Use sensors to avoid walls and ledges while chasing
        Vector2 facingVec = facingRight ? Vector2.right : Vector2.left;
        bool wallAhead = Physics2D.Raycast(frontCheck.position, facingVec, wallCheckDistance, groundLayer);
        bool groundAhead = Physics2D.Raycast(groundCheck.position, Vector2.down, groundCheckDistance, groundLayer);
        if (wallAhead || !groundAhead)
        {
            // Stop at the edge or wall
            Move(0f);
            return;
        }
        Move(desiredDir * chaseSpeed);
    }

    /// <summary>
    /// Detects the player if they are within the defined radius and vertical tolerance.  Uses Physics2D.OverlapCircle
    /// to avoid maintaining a persistent player reference.  Returns the player's transform if found, else null.
    /// </summary>
    private Transform DetectPlayer()
    {
        // Check for any collider on the player layer within detectionRadius
        Collider2D hit = Physics2D.OverlapCircle(transform.position, detectionRadius, playerMask);
        if (hit == null) return null;
        Transform target = hit.transform;
        // Ensure vertical difference is within tolerance
        float yDiff = Mathf.Abs(target.position.y - transform.position.y);
        if (yDiff > verticalTolerance) return null;
        // Require the target be in front of the enemy
        float xDiff = target.position.x - transform.position.x;
        if (facingRight && xDiff < 0f) return null;
        if (!facingRight && xDiff > 0f) return null;
        return target;
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        if (frontCheck)
        {
            Vector2 dir = (sr && sr.flipX) ? Vector2.left : Vector2.right;
            Gizmos.DrawLine(frontCheck.position, frontCheck.position + (Vector3)(dir * wallCheckDistance));
        }
        if (groundCheck)
        {
            Gizmos.DrawLine(groundCheck.position, groundCheck.position + Vector3.down * groundCheckDistance);
        }
    }
}
