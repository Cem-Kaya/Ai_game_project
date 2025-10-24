using System;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

/// ML-Agents 2D platformer agent with dash + jump-hold cut + directional attack + pogo.
/// Vector observations include camera, nearest enemies/gems, and final goal.
/// Rewards are modular via RewardWeights. Uses safe tag string equality (no CompareTag).
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class AIAgentController2D : Agent
{
    // ===== Movement =====
    [Header("Move")]
    [SerializeField] private float speed = 7f;

    [Header("Jump (press + hold for higher)")]
    [SerializeField] private float jumpForce = 11f;
    [SerializeField] private float maxHoldTime = 0.3f;

    [Header("Gravity tuning (feel)")]
    [SerializeField] private float baseGravityScale = 1f;
    [SerializeField] private float fallGravityScale = 3f;
    [SerializeField] private float jumpCutGravityScale = 6f;

    [Header("Dash")]
    [SerializeField] private float dashSpeed = 20f;
    [SerializeField] private float dashDuration = 0.2f;

    [Header("Attack")]
    [SerializeField] private float attackDuration = 0.2f;
    [SerializeField] private float attackCooldown = 0.4f;
    [SerializeField] private float pogoVelocity = 8f;
    [SerializeField] private float pogoTime = 0.2f;
    [SerializeField] private float attackDirectionYThreshold = 0.5f;

    // ===== World query (for observations) =====
    [Header("World Query (Observations)")]
    [SerializeField] private Transform finalGoal;      // optional
    [SerializeField] private LayerMask enemyMask;      // enemies for observations & pogo
    [SerializeField] private LayerMask gemMask;        // collectibles (obs only)
    [SerializeField] private int maxEnemies = 5;
    [SerializeField] private int maxGems = 5;
    [SerializeField] private float scanRadius = 25f;

    // ===== Trigger handling with tags (configurable, no CompareTag used) =====
    [Header("Trigger Tags (leave empty to disable)")]
    [SerializeField] private string gemTag = "Gem";   // collectable
    [SerializeField] private string goalTag = "";      // e.g., "Goal" (was "Star"); empty = disabled
    [SerializeField] private string hazardTag = "";     // e.g., "Hazard" (was "BAD"); empty = disabled

    // ===== Arena / camera =====
    [Header("Observations / Arena")]
    public bool autoBoundsFromCamera = true;
    public float cameraPadding = 0.5f;
    public Vector2 minXY;
    public Vector2 maxXY;

    [Header("Camera Observations")]
    [SerializeField] private Camera obsCamera;
    [SerializeField] private bool includeCameraObs = true;

    // ===== Rewards =====
    [Serializable]
    public class RewardWeights
    {
        [Header("Per-step")]
        public float stepPenalty = -0.001f;

        [Header("Shaping (distance deltas per meter)")]
        public float progressToGoal = 0f;
        public float progressToNearestGem = 0f;
        public float progressToNearestEnemy = 0f;

        [Header("Events")]
        public float collectGem = 0.2f;
        public float hitEnemy = 0.1f;
        public float pogoFromEnemy = 0.3f;
        public float reachGoal = 1.0f;
        public float deathPenalty = -1.0f;
    }
    [Header("Reward Weights")]
    public RewardWeights R = new RewardWeights();

    [Header("Fail-safe")]
    public float autoRespawnY = -20f;

    // ===== internals =====
    private Rigidbody2D rb;
    private BoxCollider2D box;
    private GameObject attackArea;

    private Vector2 moveInput;
    private float lastFacingX = 1f;
    private bool movementLocked = false;

    private bool isGrounded;
    [SerializeField] private float groundContactNormalThreshold = 0.7f;
    private bool holdJump;
    private float holdTimer;
    private bool jumpCut;

    private bool canDash = true;
    private bool dashing;
    private float dashTimer;

    public bool attackDownward;  // read by AttackAreaAgent
    private bool attacking = false;
    private float attackTimer;
    private float attackCDTimer;
    private float playerSizeX;
    private float playerSizeY;
    private Vector3 attackAreaDefaultPos;
    private float pogoTimer;

    // edge detectors
    private int prevJumpHold = 0;
    private int prevDash = 0;
    private int prevAttack = 0;

    // cached arrays
    private Collider2D[] enemyBuf;
    private Collider2D[] gemBuf;

    // shaping caches
    private float prevGoalDist;
    private float prevNearestGemDist;
    private float prevNearestEnemyDist;

    // episode spawn region
    private Vector2 spawnMinXY;
    private Vector2 spawnMaxXY;

    private new void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
        rb.gravityScale = baseGravityScale;

        if (obsCamera == null) obsCamera = Camera.main;
        if (autoBoundsFromCamera) ComputeBoundsFromCamera();

        attackArea = transform.Find("AttackArea")?.gameObject;
        if (attackArea == null)
        {
            Debug.LogError("AttackArea child not found. Create a child named 'AttackArea' with a 2D Trigger collider.");
        }
        else
        {
            attackAreaDefaultPos = attackArea.transform.localPosition;
            attackArea.SetActive(false);

            if (attackArea.GetComponent<AttackAreaAgent>() == null)
                attackArea.AddComponent<AttackAreaAgent>(); // forwards hits, uses safe tag compare
        }

        playerSizeX = box.size.x;
        playerSizeY = box.size.y;

        spawnMinXY = minXY;
        spawnMaxXY = maxXY;

        enemyBuf = new Collider2D[Mathf.Max(1, maxEnemies * 2)];
        gemBuf = new Collider2D[Mathf.Max(1, maxGems * 2)];
        ResolveFinalGoalByTagIfNeeded();
    }

    private void Update() => TuneGravityForFeel();

    private void FixedUpdate()
    {
        MovePlayer();
        HandleHoldTimer();
        HandlePogoTimer();
        HandleAirTime();
        HandleDash();
        HandleAttack();

        ApplyStepRewards();

        if (transform.position.y < autoRespawnY)
        {
            AddReward(R.deathPenalty);
            EndEpisode();
        }
    }

    private void ComputeBoundsFromCamera()
    {
        var cam = obsCamera != null ? obsCamera : Camera.main;
        if (cam == null || !cam.orthographic)
        {
            minXY = new Vector2(-10f, -5f);
            maxXY = new Vector2(10f, 5f);
            return;
        }

        float halfH = cam.orthographicSize;
        float halfW = halfH * cam.aspect;
        Vector3 c = cam.transform.position;

        minXY = new Vector2(c.x - halfW, c.y - halfH) - Vector2.one * cameraPadding;
        maxXY = new Vector2(c.x + halfW, c.y + halfH) + Vector2.one * cameraPadding;
    }

    private void UnlockMovement() => movementLocked = false;

    // ===== Movement =====
    private void MovePlayer()
    {
        if (movementLocked) return;
        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
        if (Mathf.Abs(moveInput.x) > 0.01f) lastFacingX = Mathf.Sign(moveInput.x);
    }

    private void OnJumpPressed()
    {
        if (movementLocked) return;
        if (isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;
            holdTimer = maxHoldTime;
            holdJump = true;
        }
    }

    private void OnJumpReleased()
    {
        if (holdJump) rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        holdJump = false;
        jumpCut = false;
    }

    private void HandleHoldTimer()
    {
        if (holdJump && holdTimer > 0f) holdTimer -= Time.fixedDeltaTime;
        else holdTimer = 0f;
    }

    private void HandleAirTime()
    {
        if (rb.linearVelocity.y > 0f && !((holdJump && holdTimer > 0f) || pogoTimer > 0f))
            jumpCut = true;
        else if (rb.linearVelocity.y < 0f)
        {
            jumpCut = false;
            holdJump = false;
        }
    }

    private void TuneGravityForFeel()
    {
        if (rb.linearVelocity.y < 0f) rb.gravityScale = fallGravityScale;
        else if (jumpCut) rb.gravityScale = jumpCutGravityScale;
        else rb.gravityScale = baseGravityScale;
    }

    private void OnDashPressed()
    {
        if (movementLocked || !canDash) return;

        movementLocked = true;
        Invoke(nameof(UnlockMovement), dashDuration);
        dashing = true;
        dashTimer = dashDuration;
        canDash = false;

        float dir = (Mathf.Abs(moveInput.x) > 0.01f) ? Mathf.Sign(moveInput.x) : lastFacingX;
        rb.linearVelocity = new Vector2(dir * dashSpeed, 0f);
    }

    private void HandleDash()
    {
        if (!dashing) return;

        if (dashTimer > 0f)
        {
            dashTimer -= Time.fixedDeltaTime;
            rb.linearVelocity = new Vector2(Mathf.Sign(rb.linearVelocity.x) * dashSpeed, 0f);
        }
        else
        {
            dashing = false;
            if (isGrounded) canDash = true;
        }
    }

    private void OnAttackPressed()
    {
        if (movementLocked || attacking || attackCDTimer > 0f) return;

        attacking = true;
        attackTimer = attackDuration;
        attackCDTimer = attackCooldown;

        Vector3 pos;
        float angleZ;

        if (moveInput.y > attackDirectionYThreshold)
        {
            attackDownward = false;
            pos = new Vector3(0f, playerSizeY * 0.5f, 0f) + attackAreaDefaultPos;
            angleZ = 90f;
        }
        else if (moveInput.y < -attackDirectionYThreshold && !isGrounded)
        {
            attackDownward = true;
            pos = new Vector3(0f, -playerSizeY * 0.5f, 0f) + attackAreaDefaultPos;
            angleZ = -90f;
        }
        else
        {
            attackDownward = false;
            pos = new Vector3(playerSizeX * 0.5f * lastFacingX, 0f, 0f) + attackAreaDefaultPos;
            angleZ = (lastFacingX >= 0f) ? 0f : 180f;
        }

        if (attackArea != null)
        {
            attackArea.transform.localPosition = pos;
            attackArea.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
            attackArea.SetActive(true);
        }
    }

    private void HandleAttack()
    {
        if (attackCDTimer > 0f) attackCDTimer -= Time.fixedDeltaTime;
        else attackCDTimer = 0f;

        if (!attacking) return;

        if (attackTimer > 0f) attackTimer -= Time.fixedDeltaTime;
        else
        {
            attacking = false;
            if (attackArea != null) attackArea.SetActive(false);
        }
    }

    public void Pogo()
    {
        canDash = true;
        pogoTimer = pogoTime;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, pogoVelocity);
        AddReward(R.pogoFromEnemy);
    }

    private void HandlePogoTimer()
    {
        if (pogoTimer > 0f) pogoTimer -= Time.fixedDeltaTime;
        else pogoTimer = 0f;
    }

    // ===== Grounding (keep your existing tag/layer approach for ground if desired) =====
    private void OnCollisionEnter2D(Collision2D collision)
    {
        foreach (var c in collision.contacts)
        {
            if (c.normal.y >= groundContactNormalThreshold)
            {
                isGrounded = true;
                canDash = true;
                break;
            }
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        foreach (var c in collision.contacts)
        {
            if (c.normal.y >= groundContactNormalThreshold)
            {
                isGrounded = true;
                canDash = true;
                break;
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        isGrounded = false;
    }

    // ===== Trigger events via SAFE tag equality (no CompareTag) =====
    private static bool HasTag(GameObject go, string tagName)
        => !string.IsNullOrEmpty(tagName) && go.tag == tagName;

    private void OnTriggerEnter2D(Collider2D other)
    {
        var go = other.gameObject;

        if (HasTag(go, gemTag))
        {
            OnCollectGem(other);
            return;
        }
        if (HasTag(go, goalTag))
        {
            OnReachGoal(other);
            return;
        }
        if (HasTag(go, hazardTag))
        {
            OnDeath(other);
            return;
        }
    }

    // ===== Heuristic (keyboard) =====
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var da = actionsOut.DiscreteActions;
        float h = Input.GetAxisRaw("Horizontal");
        da[0] = h < 0 ? 1 : (h > 0 ? 2 : 0);

        float v = Input.GetAxisRaw("Vertical");
        da[4] = v > 0.2f ? 1 : (v < -0.2f ? 2 : 0);

        bool jumpHeld = Input.GetKey(KeyCode.Space);
        da[1] = jumpHeld ? 1 : 0;

        da[2] = Input.GetKeyDown(KeyCode.LeftShift) ? 1 : 0; // dash
        da[3] = Input.GetKeyDown(KeyCode.J) ? 1 : 0;         // attack
    }

    // ===== Observations =====
    public override void CollectObservations(VectorSensor sensor)
    {
        Vector2 pos = transform.position;
        Vector2 vel = rb.linearVelocity;
        Vector2 arena = (maxXY - minXY);

        // Self
        sensor.AddObservation(new Vector2(
            (pos.x - minXY.x) / Mathf.Max(0.001f, arena.x),
            (pos.y - minXY.y) / Mathf.Max(0.001f, arena.y)
        ));                                 // +2
        sensor.AddObservation(new Vector2(
            Mathf.Clamp(vel.x / 20f, -1f, 1f),
            Mathf.Clamp(vel.y / 20f, -1f, 1f)
        ));                                 // +2
        sensor.AddObservation(isGrounded ? 1f : 0f); // +1
        sensor.AddObservation(dashing ? 1f : 0f);    // +1
        sensor.AddObservation(attacking ? 1f : 0f);  // +1
        sensor.AddObservation(lastFacingX >= 0f ? 1f : -1f); // +1

        // Camera (optional vector obs)
        if (includeCameraObs && obsCamera != null)
        {
            var cpos = obsCamera.transform.position;
            float halfH = obsCamera.orthographicSize;
            float halfW = halfH * obsCamera.aspect;

            sensor.AddObservation(new Vector2(
                (cpos.x - minXY.x) / Mathf.Max(0.001f, arena.x),
                (cpos.y - minXY.y) / Mathf.Max(0.001f, arena.y)
            )); // +2
            sensor.AddObservation(halfW / Mathf.Max(0.001f, arena.x)); // +1
            sensor.AddObservation(halfH / Mathf.Max(0.001f, arena.y)); // +1
        }

        // Final goal (optional)
        if (finalGoal != null)
        {
            Vector2 toGoal = (Vector2)finalGoal.position - pos;
            sensor.AddObservation(new Vector2(
                Mathf.Clamp(toGoal.x / scanRadius, -1f, 1f),
                Mathf.Clamp(toGoal.y / scanRadius, -1f, 1f)
            )); // +2
            sensor.AddObservation(Mathf.Clamp01(toGoal.magnitude / scanRadius)); // +1
        }
        else
        {
            sensor.AddObservation(Vector2.zero); // +2
            sensor.AddObservation(1f);           // +1
        }

        // Nearest enemies
        AddNearestObjectsObservations(sensor, enemyMask, maxEnemies);

        // Nearest gems
        AddNearestObjectsObservations(sensor, gemMask, maxGems);
    }

    private void AddNearestObjectsObservations(VectorSensor sensor, LayerMask mask, int maxCount)
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, scanRadius, mask);

        int n = hits.Length;
        int use = Mathf.Min(maxCount, n);
        Vector2 me = transform.position;

        for (int i = 0; i < use; i++)
        {
            int best = i;
            float bestD2 = float.PositiveInfinity;

            for (int j = i; j < n; j++)
            {
                if (!hits[j]) continue;
                float d2 = ((Vector2)hits[j].transform.position - me).sqrMagnitude;
                if (d2 < bestD2) { bestD2 = d2; best = j; }
            }
            (hits[i], hits[best]) = (hits[best], hits[i]);
        }

        for (int k = 0; k < maxCount; k++)
        {
            if (k < use && hits[k] != null)
            {
                Vector2 rel = (Vector2)hits[k].transform.position - me;
                float d = Mathf.Min(rel.magnitude, scanRadius);
                sensor.AddObservation(new Vector2(
                    Mathf.Clamp(rel.x / scanRadius, -1f, 1f),
                    Mathf.Clamp(rel.y / scanRadius, -1f, 1f)
                ));               // +2
                sensor.AddObservation(d / scanRadius); // +1
                sensor.AddObservation(1f);             // +1 exists
            }
            else
            {
                sensor.AddObservation(Vector2.zero);   // +2
                sensor.AddObservation(1f);             // +1
                sensor.AddObservation(0f);             // +1 exists = 0
            }
        }
    }

    // ===== Actions =====
    public override void OnActionReceived(ActionBuffers actions)
    {
        int aMove = actions.DiscreteActions[0];     // 0 none, 1 left, 2 right
        int aJumpHold = actions.DiscreteActions[1]; // 0 up, 1 held
        int aDash = actions.DiscreteActions[2];     // 0/1 press
        int aAttack = actions.DiscreteActions[3];   // 0/1 press
        int aAimY = actions.DiscreteActions[4];     // 0 neutral, 1 up, 2 down

        moveInput.x = (aMove == 1) ? -1f : (aMove == 2 ? 1f : 0f);
        moveInput.y = (aAimY == 1) ? 1f : (aAimY == 2 ? -1f : 0f);

        bool jumpPressedEdge = (prevJumpHold == 0 && aJumpHold == 1 && isGrounded);
        bool jumpReleasedEdge = (prevJumpHold == 1 && aJumpHold == 0);
        bool dashPress = (prevDash == 0 && aDash == 1);
        bool attackPress = (prevAttack == 0 && aAttack == 1);

        if (jumpPressedEdge) OnJumpPressed();
        if (jumpReleasedEdge) OnJumpReleased();
        if (dashPress) OnDashPressed();
        if (attackPress) OnAttackPressed();

        prevJumpHold = aJumpHold;
        prevDash = aDash;
        prevAttack = aAttack;

        if (R.stepPenalty != 0f) AddReward(R.stepPenalty);
    }

    public override void OnEpisodeBegin()
    {
        ResolveFinalGoalByTagIfNeeded();

        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.gravityScale = baseGravityScale;

        movementLocked = false;
        holdJump = false;
        holdTimer = 0f;
        jumpCut = false;
        canDash = true;
        dashing = false;
        dashTimer = 0f;
        attackDownward = false;
        attacking = false;
        attackTimer = 0f;
        attackCDTimer = 0f;
        if (attackArea) attackArea.SetActive(false);
        isGrounded = false;
        pogoTimer = 0f;

        prevJumpHold = 0;
        prevDash = 0;
        prevAttack = 0;

        Vector2 spawn = new Vector2(1.5f, 0.0f);
        rb.position = spawn;

        prevGoalDist = DistanceTo(finalGoal);
        prevNearestGemDist = FindNearestDistance(gemMask);
        prevNearestEnemyDist = FindNearestDistance(enemyMask);
    }

    // ===== Reward helpers =====
    private void ApplyStepRewards()
    {
        if (R.progressToGoal != 0f && finalGoal != null)
        {
            float d = DistanceTo(finalGoal);
            float delta = prevGoalDist - d;
            AddReward(delta * R.progressToGoal);
            prevGoalDist = d;
        }

        if (R.progressToNearestGem != 0f)
        {
            float d = FindNearestDistance(gemMask);
            float delta = prevNearestGemDist - d;
            AddReward(delta * R.progressToNearestGem);
            prevNearestGemDist = d;
        }

        if (R.progressToNearestEnemy != 0f)
        {
            float d = FindNearestDistance(enemyMask);
            float delta = prevNearestEnemyDist - d;
            AddReward(delta * R.progressToNearestEnemy);
            prevNearestEnemyDist = d;
        }
    }

    private float DistanceTo(Transform t)
    {
        if (t == null) return scanRadius;
        return Mathf.Min(((Vector2)t.position - (Vector2)transform.position).magnitude, scanRadius);
    }

    private float FindNearestDistance(LayerMask mask)
    {
        var hits = Physics2D.OverlapCircleAll(transform.position, scanRadius, mask);
        float best = scanRadius;
        Vector2 me = transform.position;

        for (int i = 0; i < hits.Length; i++)
        {
            if (!hits[i]) continue;
            float d = ((Vector2)hits[i].transform.position - me).magnitude;
            if (d < best) best = d;
        }
        return best;
    }

    // public hooks for hitbox / world
    public void OnAttackHitEnemy(Collider2D enemy)
    {
        AddReward(R.hitEnemy);
        if (attackDownward) Pogo();
    }

    public void OnCollectGem(Collider2D gem)
    {
        AddReward(R.collectGem);
        // Destroy(gem.gameObject); // if you want to consume it here
    }

    public void OnReachGoal(Collider2D goal)
    {
        AddReward(R.reachGoal);
        EndEpisode();
    }

    public void OnDeath(Collider2D hazard)
    {
        AddReward(R.deathPenalty);
        EndEpisode();
    }

    // convenience
    public void SetSpawnRegion(Vector2 min, Vector2 max)
    {
        spawnMinXY = min; spawnMaxXY = max;
    }


    // Add this field if not present

    private void ResolveFinalGoalByTagIfNeeded()
    {
        if (finalGoal != null) return;
        if (string.IsNullOrEmpty(goalTag)) return;

        var all = GameObject.FindGameObjectsWithTag(goalTag);
        if (all == null || all.Length == 0) return;

        // pick the closest to the agent
        Transform best = all[0].transform;
        float bestD2 = ((Vector2)best.position - (Vector2)transform.position).sqrMagnitude;
        for (int i = 1; i < all.Length; i++)
        {
            float d2 = ((Vector2)all[i].transform.position - (Vector2)transform.position).sqrMagnitude;
            if (d2 < bestD2) { bestD2 = d2; best = all[i].transform; }
        }
        finalGoal = best;
    }
}
