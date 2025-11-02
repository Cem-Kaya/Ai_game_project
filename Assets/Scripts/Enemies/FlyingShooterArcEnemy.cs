using UnityEngine;

/// <summary>
/// Detects the Player by tag within detectionRange and switches to Attack state.
/// While attacking:
/// - Shoots every fireInterval if within shootRangeMax and LoS
/// - Backs off if too close (< shootRangeMin)
/// - Moves in if too far (> shootRangeMax)
/// Reverts to Patrol (wander) when the player leaves detection (with hysteresis).
/// Unity 6 safe (uses Rigidbody2D.linearVelocity / MovePosition).
/// </summary>
public class FlyingShooterArcEnemy : EnemyBase
{
    [Header("Idle Wander")]
    [SerializeField] private Vector2 wanderArea = new Vector2(5f, 3f);
    [SerializeField] private float wanderSpeed = 2.5f;
    [SerializeField] private float pauseDuration = 0.4f;

    [Header("Detection")]
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private LayerMask playerMask;
    [Tooltip("Radius for acquiring the Player.")]
    [SerializeField] private float detectionRange = 10f;
    [Tooltip("Extra buffer so the target doesn't flicker at the edge.")]
    [SerializeField] private float detectionHysteresis = 1.0f;
    [Tooltip("Absolute vertical tolerance for detection (|dy|).")]
    [SerializeField] private float verticalTolerance = 3.5f;

    [Header("Range Keeping")]
    [Tooltip("Too close -> back away")]
    [SerializeField] private float shootRangeMin = 4f;
    [Tooltip("Can shoot up to this distance; too far -> move closer")]
    [SerializeField] private float shootRangeMax = 6.5f;
    [SerializeField] private float approachSpeed = 3.0f;
    [SerializeField] private float retreatSpeed = 3.25f;
    [Tooltip("Sideways drift while in the good band to keep motion feeling alive.")]
    [SerializeField] private float strafeSpeed = 1.2f;

    [Header("Shooting (Arc)")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private Transform shootPoint;
    [SerializeField] private float projectileSpeed = 4.5f;
    [SerializeField] private float arcVerticalFactor = 0.55f;
    [Tooltip("Fixed cadence while the player is within shootRangeMax & LoS.")]
    [SerializeField] private float fireInterval = 0.8f;
    [SerializeField] private LayerMask obstacleMask;

    [Header("Debug")]
    [SerializeField] private bool drawGizmos = true;


    [Header("Audio")]
    [SerializeField] private AudioClip ShootSound; // assign the .wav file here

    // internals
    private Vector2 idleCenter;
    private Vector2 wanderTarget;
    private float pauseTimer;
    private float fireTimer;
    private Transform currentTarget; // sticky while inside detection window
    private static readonly Collider2D[] buf = new Collider2D[2];
    private int strafeDir = 1;
    private AudioSource audioSource;

    private void Awake()
    {
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        audioSource.playOnAwake = false;
    }
    protected override void OnEnable()
    {
        base.OnEnable();
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;

        idleCenter = transform.position;
        ChooseNewWander();
        pauseTimer = 0f;
        fireTimer = 0f;
        strafeDir = Random.value < 0.5f ? 1 : -1;

        SetState(EnemyState.Idle);
    }

    private void FixedUpdate()
    {
        if (state == EnemyState.Dead) return;

        // update timers
        if (fireTimer > 0f) fireTimer -= Time.fixedDeltaTime;

        // maintain sticky detection
        UpdateStickyTarget();

        if (currentTarget != null)
        {
            // Make sure we're in Attack state while we have a target
            if (state != EnemyState.Attack) SetState(EnemyState.Attack);
            AttackBehaviour(currentTarget);
        }
        else
        {
            // No target: patrol/wander
            if (state != EnemyState.Patrol && state != EnemyState.Idle) SetState(EnemyState.Patrol);
            Wander();
        }
    }

    // ------------------ Detection ------------------

    private void UpdateStickyTarget()
    {
        // keep while inside exit radius + vertical tolerance
        if (currentTarget != null)
        {
            if (!currentTarget.gameObject.activeInHierarchy)
            {
                currentTarget = null;
                return;
            }
            Vector2 d = (Vector2)currentTarget.position - (Vector2)transform.position;
            float exitR = detectionRange + detectionHysteresis;
            bool inExit = d.sqrMagnitude <= exitR * exitR;
            bool inVert = Mathf.Abs(d.y) <= verticalTolerance;

            if (inExit && inVert) return; // keep
            currentTarget = null;
        }

        // acquire
        int n = Physics2D.OverlapCircleNonAlloc(transform.position, detectionRange, buf, playerMask);
        for (int i = 0; i < n; i++)
        {
            var c = buf[i];
            if (c == null || !c.CompareTag(playerTag)) continue;
            float yDiff = Mathf.Abs(c.transform.position.y - transform.position.y);
            if (yDiff > verticalTolerance) continue;

            currentTarget = c.transform;
            // randomize strafe direction on lock
            strafeDir = Random.value < 0.5f ? 1 : -1;
            break;
        }
    }

    // ------------------ Attack behaviour ------------------

    private void AttackBehaviour(Transform target)
    {
        Vector2 to = (Vector2)target.position - (Vector2)transform.position;
        float dist = to.magnitude;

        // 1) Movement: keep a good distance band
        Vector2 desiredVel;

        if (dist < shootRangeMin)
        {
            // too close -> back away
            Vector2 away = (-to / Mathf.Max(dist, 0.001f)) * retreatSpeed;
            desiredVel = away;
        }
        else if (dist > shootRangeMax)
        {
            // too far -> approach
            Vector2 toward = (to / Mathf.Max(dist, 0.001f)) * approachSpeed;
            desiredVel = toward;
        }
        else
        {
            // in the band -> hover with slight strafe
            Vector2 dir = to / Mathf.Max(dist, 0.001f);
            Vector2 tangent = new Vector2(-dir.y, dir.x) * strafeSpeed * strafeDir;
            desiredVel = tangent; // tiny sideways drift
            if (Random.value < 0.002f) strafeDir *= -1; // occasional flip for variety
        }

        Vector2 next = (Vector2)transform.position + desiredVel * Time.fixedDeltaTime;
        rb.MovePosition(next);

        if ((desiredVel.x > 0f && !facingRight) || (desiredVel.x < 0f && facingRight))
            Flip();

        // 2) Shooting: constant cadence while the player is within shoot range and LoS
        if (dist <= shootRangeMax && fireTimer <= 0f && HasLineOfSight(target))
        {
            ShootArcProjectile(target);
            audioSource.PlayOneShot(ShootSound);
            fireTimer = fireInterval; // fixed 0.8s cadence
        }
    }

    private bool HasLineOfSight(Transform t)
    {
        Vector2 to = (Vector2)t.position - (Vector2)transform.position;
        var hit = Physics2D.Raycast(transform.position, to.normalized, to.magnitude, obstacleMask);
        return hit.collider == null;
    }

    private void ShootArcProjectile(Transform t)
    {
        if (!projectilePrefab) return;

        Vector3 spawn = shootPoint ? shootPoint.position : transform.position;
        GameObject proj = Instantiate(projectilePrefab, spawn, Quaternion.identity);
        var prb = proj.GetComponent<Rigidbody2D>();
        if (prb)
        {
            // horizontal toward target + vertical component for arc
            float dir = Mathf.Sign(t.position.x - spawn.x);
            Vector2 vel = new Vector2(dir * projectileSpeed, projectileSpeed * arcVerticalFactor);
            prb.linearVelocity = vel;
        }
    }

    // ------------------ Wander (when no target) ------------------

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
        Vector2 move = (to / Mathf.Max(d, 0.001f)) * wanderSpeed * Time.fixedDeltaTime;
        rb.MovePosition(pos + move);

        if ((move.x > 0f && !facingRight) || (move.x < 0f && facingRight)) Flip();
    }

    private void ChooseNewWander()
    {
        float hw = wanderArea.x * 0.5f;
        float hh = wanderArea.y * 0.5f;
        wanderTarget = idleCenter + new Vector2(Random.Range(-hw, hw), Random.Range(-hh, hh));
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos) return;

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = new Color(1f, 0.5f, 0.2f, 1f);
        Gizmos.DrawWireSphere(transform.position, detectionRange + detectionHysteresis);

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(transform.position, shootRangeMin);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, shootRangeMax);

        Gizmos.color = Color.cyan;
        Gizmos.DrawWireCube(Application.isPlaying ? (Vector3)idleCenter : transform.position,
            new Vector3(wanderArea.x, wanderArea.y, 0.1f));
    }
}
