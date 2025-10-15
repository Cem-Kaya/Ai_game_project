using UnityEngine;

/// <summary>
/// Flies around the player while in detection range and dashes at intervals if LoS is clear.
/// Falls back to wandering when the player leaves the detection area.
/// </summary>
public class FlyingDashEnemy : EnemyBase
{
    [Header("Movement Area (idle/wander)")]
    [SerializeField] private Vector2 wanderArea = new Vector2(5f, 3f);
    [SerializeField] private float flySpeed = 2.5f;
    [SerializeField] private float pauseDuration = 0.5f;

    [Header("Detection")]
    [SerializeField] private LayerMask playerMask;
    [SerializeField] private float sightRange = 7f;                // acquire/keep radius
    [SerializeField] private float sightVerticalTolerance = 3f;     // +/- Y tolerance
    [Tooltip("Extra radius used to keep the target a tiny bit longer to prevent flicker at the edge.")]
    [SerializeField] private float hysteresis = 0.75f;              // 'exit' radius = sightRange + hysteresis

    [Header("Orbit / Keep-Close")]
    [Tooltip("Desired orbit radius around the player when not dashing.")]
    [SerializeField] private float orbitRadius = 3f;
    [Tooltip("How tightly to correct toward the orbit radius (0 = loose, 1 = snappy).")]
    [SerializeField, Range(0f, 1f)] private float radiusGain = 0.35f;
    [Tooltip("Tangential speed around the player while orbiting.")]
    [SerializeField] private float orbitTangentialSpeed = 2.5f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 10f;
    [SerializeField] private float dashDuration = 0.28f;
    [SerializeField] private float dashCooldown = 1.25f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;

    // --- internals ---
    private Vector2 initialPosition;
    private Vector2 wanderTarget;
    private float pauseTimer;
    private float dashTimer;
    private float cooldownTimer;

    private Transform currentTarget;                        // sticky while in range
    private static readonly Collider2D[] buf = new Collider2D[2];
    private int orbitDir = 1;                               // +1 cw / -1 ccw; flipped occasionally

    protected override void OnEnable()
    {
        base.OnEnable();
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;

        initialPosition = transform.position;
        ChooseNewWander();
        pauseTimer = 0f;
        dashTimer = 0f;
        cooldownTimer = 0f;
        orbitDir = Random.value < 0.5f ? 1 : -1;

        SetState(EnemyState.Patrol);
    }

    private void FixedUpdate()
    {
        if (state == EnemyState.Dead) return;

        // timers
        if (dashTimer > 0f)
        {
            dashTimer -= Time.fixedDeltaTime;
            if (dashTimer <= 0f)
            {
                rb.linearVelocity = Vector2.zero;
                SetState(EnemyState.Patrol);
                cooldownTimer = dashCooldown;
            }
        }
        if (cooldownTimer > 0f) cooldownTimer -= Time.fixedDeltaTime;

        // sticky target maintenance
        UpdateStickyTarget();

        if (currentTarget != null)
        {
            // Player locked: circle around and dash on cooldown
            if (state != EnemyState.Attack)
            {
                OrbitAround(currentTarget);

                if (cooldownTimer <= 0f && HasLineOfSight(currentTarget))
                    StartDash(currentTarget);
            }
        }
        else
        {
            // No target: simple wander
            Wander();
        }
    }

    // ---------- Targeting ----------

    private void UpdateStickyTarget()
    {
        // Keep target while inside exit radius
        if (currentTarget != null)
        {
            if (!currentTarget.gameObject.activeInHierarchy)
            {
                currentTarget = null;
                return;
            }
            Vector2 d = (Vector2)currentTarget.position - (Vector2)transform.position;
            float sqrExit = (sightRange + hysteresis) * (sightRange + hysteresis);
            bool inExit = d.sqrMagnitude <= sqrExit;
            bool inVertical = Mathf.Abs(d.y) <= sightVerticalTolerance;

            if (inExit && inVertical) return;   // still valid
            currentTarget = null;
        }

        // Acquire
        int n = Physics2D.OverlapCircleNonAlloc(transform.position, sightRange, buf, playerMask);
        for (int i = 0; i < n; i++)
        {
            var c = buf[i];
            if (c == null || !c.CompareTag("Player")) continue;
            float yDiff = Mathf.Abs(c.transform.position.y - transform.position.y);
            if (yDiff > sightVerticalTolerance) continue;
            currentTarget = c.transform;
            // Randomize orbit direction on new lock
            orbitDir = Random.value < 0.5f ? 1 : -1;
            break;
        }
    }

    private bool HasLineOfSight(Transform t)
    {
        Vector2 to = (Vector2)t.position - (Vector2)transform.position;
        var hit = Physics2D.Raycast(transform.position, to.normalized, to.magnitude, obstacleMask);
        return hit.collider == null;
    }

    // ---------- Behaviours ----------

    private void OrbitAround(Transform t)
    {
        // vector to player and distance
        Vector2 to = (Vector2)t.position - (Vector2)transform.position;
        float dist = to.magnitude;
        if (dist < 0.001f) return;

        // radial correction to maintain orbit radius
        float radialError = orbitRadius - dist;                         // + if we are outside desired radius
        Vector2 radialVel = to.normalized * (radialError * radiusGain * flySpeed);

        // tangential velocity (perpendicular to 'to')
        Vector2 tangent = new Vector2(-to.y, to.x).normalized * orbitTangentialSpeed * orbitDir;

        Vector2 desired = radialVel + tangent;
        Vector2 next = (Vector2)transform.position + desired * Time.fixedDeltaTime;
        rb.MovePosition(next);

        // face movement
        if ((desired.x > 0f && !facingRight) || (desired.x < 0f && facingRight)) Flip();

        // Occasionally flip orbit direction to feel less robotic
        if (Random.value < 0.002f) orbitDir *= -1;
    }

    private void StartDash(Transform target)
    {
        SetState(EnemyState.Attack);
        dashTimer = dashDuration;

        Vector2 to = ((Vector2)target.position - (Vector2)transform.position).normalized;
        if ((to.x > 0f && !facingRight) || (to.x < 0f && facingRight)) Flip();

        rb.linearVelocity = to * dashSpeed;
    }

    private void Wander()
    {
        if (pauseTimer > 0f)
        {
            pauseTimer -= Time.fixedDeltaTime;
            return;
        }

        Vector2 pos = transform.position;
        Vector2 to = wanderTarget - pos;
        float d = to.magnitude;

        if (d < 0.1f)
        {
            pauseTimer = pauseDuration;
            ChooseNewWander();
            return;
        }

        Vector2 move = (to / d) * flySpeed * Time.fixedDeltaTime;
        rb.MovePosition(pos + move);

        if ((move.x > 0f && !facingRight) || (move.x < 0f && facingRight)) Flip();
    }

    private void ChooseNewWander()
    {
        float hw = wanderArea.x * 0.5f;
        float hh = wanderArea.y * 0.5f;
        wanderTarget = initialPosition + new Vector2(Random.Range(-hw, hw), Random.Range(-hh, hh));
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Application.isPlaying ? (Vector3)initialPosition : transform.position,
            new Vector3(wanderArea.x, wanderArea.y, 0.1f));

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, sightRange);
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, sightRange + hysteresis);

        Gizmos.color = Color.magenta;
        if (currentTarget != null) Gizmos.DrawWireSphere(currentTarget.position, orbitRadius);
    }
}
