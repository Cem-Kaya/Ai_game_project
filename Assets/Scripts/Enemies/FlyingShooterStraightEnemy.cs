using UnityEngine;

/// <summary>
/// A flying enemy that roams within a defined area and fires projectiles directly at the player on a straight trajectory.
/// The enemy itself ignores gravity but fired projectiles can be subject to physics.
/// </summary>
public class FlyingShooterStraightEnemy : EnemyBase
{
    [Header("Movement Area")]
    [SerializeField] private Vector2 wanderArea = new Vector2(5f, 3f);
    [SerializeField] private float flySpeed = 2f;
    [SerializeField] private float pauseDuration = 0.5f;

    [Header("Shooting")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform shootPoint;
    [SerializeField] private float fireCooldown = 2f;
    [SerializeField] private float projectileSpeed = 5f;
    [Tooltip("Maximum distance horizontally to begin firing.")]
    [SerializeField] private float sightRange = 6f;
    [Tooltip("Vertical tolerance for seeing the player.")]
    [SerializeField] private float sightVerticalTolerance = 2f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Player Detection")]
    [Tooltip("Layer mask used to identify the player for detection queries.")]
    [SerializeField] private LayerMask playerMask;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    private Vector2 initialPosition;
    private Vector2 targetPosition;
    private float pauseTimer;
    private float fireTimer;

    protected override void OnEnable()
    {
        base.OnEnable();
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        initialPosition = transform.position;
        ChooseNewTarget();
        pauseTimer = 0f;
        fireTimer = 0f;
    }

    private void FixedUpdate()
    {
        if (state == EnemyState.Dead) return;
        // wander
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
        }
        else
        {
            MoveTowardsTarget();
        }
        // shooting
        if (fireTimer > 0f) fireTimer -= Time.fixedDeltaTime;
        Transform target = DetectTarget();
        if (target != null && fireTimer <= 0f)
        {
            if (HasLineOfSight(target))
            {
                ShootStraightProjectile(target);
                fireTimer = fireCooldown;
            }
        }
    }

    private void MoveTowardsTarget()
    {
        Vector2 current = transform.position;
        Vector2 toTarget = targetPosition - current;
        float distance = toTarget.magnitude;
        if (distance < 0.1f)
        {
            pauseTimer = pauseDuration;
            ChooseNewTarget();
            return;
        }
        Vector2 dir = toTarget / distance;
        if ((dir.x > 0f && !facingRight) || (dir.x < 0f && facingRight))
        {
            Flip();
        }
        Vector2 move = dir * flySpeed * Time.fixedDeltaTime;
        rb.MovePosition(current + move);
    }

    private void ChooseNewTarget()
    {
        float halfW = wanderArea.x * 0.5f;
        float halfH = wanderArea.y * 0.5f;
        float tx = Random.Range(-halfW, halfW);
        float ty = Random.Range(-halfH, halfH);
        targetPosition = initialPosition + new Vector2(tx, ty);
    }

    /// <summary>
    /// Detect a target player within range using OverlapCircle. Ensures the vertical difference is within sightVerticalTolerance.
    /// </summary>
    private Transform DetectTarget()
    {
        Collider2D hit = Physics2D.OverlapCircle(transform.position, sightRange, playerMask);
        if (hit == null) return null;
        Transform t = hit.transform;
        float yDiff = Mathf.Abs(t.position.y - transform.position.y);
        if (yDiff > sightVerticalTolerance) return null;
        return t;
    }

    /// <summary>
    /// Checks if there is a clear path to the specified target using a raycast against the obstacleMask.
    /// </summary>
    private bool HasLineOfSight(Transform target)
    {
        Vector2 toTarget = (Vector2)target.position - (Vector2)transform.position;
        RaycastHit2D hit = Physics2D.Raycast(transform.position, toTarget.normalized, toTarget.magnitude, obstacleMask);
        return hit.collider == null;
    }

    private void ShootStraightProjectile(Transform target)
    {
        if (projectilePrefab == null || target == null) return;
        Vector3 spawnPos = shootPoint != null ? shootPoint.position : transform.position;
        GameObject proj = Instantiate(projectilePrefab, spawnPos, Quaternion.identity);
        Rigidbody2D prb = proj.GetComponent<Rigidbody2D>();
        if (prb != null)
        {
            Vector2 direction = ((Vector2)target.position - (Vector2)spawnPos).normalized;
            prb.linearVelocity = direction * projectileSpeed;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Application.isPlaying ? (Vector3)initialPosition : transform.position,
            new Vector3(wanderArea.x, wanderArea.y, 0.1f));
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(transform.position,
            new Vector3(sightRange * 2f, sightVerticalTolerance * 2f, 0.1f));
    }
}