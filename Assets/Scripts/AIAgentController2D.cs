using System;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Policies;
using UnityEngine;

/// ML-Agents 2D platformer agent with dash + jump-hold cut + directional attack + pogo.
/// Adds per-episode goal-progress milestones and 3-hit death on enemy body contact.
[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
[RequireComponent(typeof(BehaviorParameters))]
public class AIAgentController2D : Agent
{
    // ===== Heuristic control gate =====
    [Header("Heuristic Control")]
    [Tooltip("When ON, keyboard controls are used in Heuristic() (only if behavior type uses heuristics). When OFF, Heuristic returns zero actions.")]
    [SerializeField] private bool enableKeyboardControl = false;

    [Tooltip("For Heuristic Only mode: request a decision every N frames to avoid missed inputs.")]
    [SerializeField] private int heuristicDecisionInterval = 1; // 1 = every frame

    // ===== Heuristic keybinds =====
    [Header("Heuristic Keys")]
    [SerializeField] private KeyCode leftKey = KeyCode.A;
    [SerializeField] private KeyCode rightKey = KeyCode.D;
    [SerializeField] private KeyCode upKey = KeyCode.W;
    [SerializeField] private KeyCode downKey = KeyCode.S;
    [SerializeField] private KeyCode jumpKey = KeyCode.Space;
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift; // Shift to dash
    [SerializeField] private KeyCode attackKey = KeyCode.Z;       // Z to attack

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
    [SerializeField] private string gemTag = "Gem";
    [SerializeField] private string goalTag = "";
    [SerializeField] private string hazardTag = "";

    // ===== Damage rules =====
    [Header("Damage Rules")]
    [Tooltip("Tag on enemy BODY colliders that should damage the agent when touched.")]
    [SerializeField] private string enemyBodyTag = "Enemy";
    [Tooltip("How many enemy body touches before death.")]
    [SerializeField] private int maxEnemyBodyTouches = 3;
    [Tooltip("Seconds of invulnerability after a touch to prevent rapid re-hits from the same overlap.")]
    [SerializeField] private float touchInvulnerabilitySeconds = 0.5f;
    [Tooltip("Optional small penalty per enemy body touch.")]
    [SerializeField] private float perTouchPenalty = -0.05f;

    // ===== Observations / Arena =====
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

        [Header("Milestones")]
        public float goalMilestoneReward = 10f;   // reward per milestone
    }
    [Header("Reward Weights")]
    public RewardWeights R = new RewardWeights();

    [Header("Milestone Settings")]
    [Tooltip("Meters toward the goal between milestone rewards.")]
    [SerializeField] private float goalMilestoneMeters = 2f;
    [Tooltip("Enable per-episode goal progress milestones.")]
    [SerializeField] private bool enableGoalMilestones = true;

    [Header("Fail-safe")]
    public float autoRespawnY = -20f;

    // ===== Episode / Timeout =====
    [Header("Episode / Timeout")]
    [SerializeField] private float episodeTimeLimit = 60f;  // seconds
    [SerializeField] private float timeoutPenalty = 0f;     // optional penalty on timeout
    private float episodeStartTime;

    // ===== internals =====
    private Rigidbody2D rb;
    private BoxCollider2D box;
    private GameObject attackArea;
    private BehaviorParameters behaviorParameters;

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

    public bool attackDownward;
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

    // shaping caches
    private float prevGoalDist;
    private float prevNearestGemDist;
    private float prevNearestEnemyDist;

    // milestone caches
    private float startGoalDist;
    private float nextGoalMilestoneProgress;

    // damage caches
    private int enemyBodyTouches;
    private float invulnerableUntil;

    // heuristic decision scheduler
    private int heuristicFrameCountdown;

    private new void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        box = GetComponent<BoxCollider2D>();
        behaviorParameters = GetComponent<BehaviorParameters>();
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
                attackArea.AddComponent<AttackAreaAgent>();
        }

        playerSizeX = box.size.x;
        playerSizeY = box.size.y;

        ResolveFinalGoalByTagIfNeeded();

        heuristicFrameCountdown = 0; // start requesting immediately if in heuristic mode
    }

    private void Update()
    {
        // Gravity feel
        TuneGravityForFeel();

        // Ensure Heuristic requests decisions regularly to avoid missed key presses.
        // Works when Behavior Type = Heuristic Only (or when Inference + Heuristic).
        if (enableKeyboardControl && behaviorParameters != null &&
            behaviorParameters.BehaviorType == BehaviorType.HeuristicOnly)
        {
            if (heuristicDecisionInterval < 1) heuristicDecisionInterval = 1;
            if (heuristicFrameCountdown <= 0)
            {
                RequestDecision();                 // schedule an action update
                heuristicFrameCountdown = heuristicDecisionInterval;
            }
            else
            {
                heuristicFrameCountdown--;
            }
        }
    }
    private void CheckFallFailSafeSimple()
    {
        // Use rb.position for world Y (same as transform.position, but already cached)
        if (rb.position.y < autoRespawnY)   // autoRespawnY is serialized; set it to -20 in Inspector
        {
            if (R.deathPenalty != 0f) AddReward(R.deathPenalty);
            EndEpisode();
        }
    }

    private void FixedUpdate()
    {
        // ---- FALL FAIL-SAFE FIRST (no other logic before this) ----
        float y = rb.position.y;
        // NaN/Inf guard
        if (!float.IsFinite(y) || !float.IsFinite(autoRespawnY))
        {
            //Debug.LogWarning($"[FailSafe:{name}#{GetInstanceID()}] Non-finite values (y={y}, autoRespawnY={autoRespawnY}). Ending episode.");
            if (R.deathPenalty != 0f) AddReward(R.deathPenalty);
            EndEpisode();
            return;
        }

        if (y < autoRespawnY)
        {
            //Debug.Log($"[FailSafe TRIP:{name}#{GetInstanceID()}] y={y:F2} < {autoRespawnY:F2}");
            if (R.deathPenalty != 0f) AddReward(R.deathPenalty);
            EndEpisode();
            return;
        }
        else
        {
            // Only keep while debugging; comment out later
            // Debug.Log($"[FailSafe NO-TRIP:{name}#{GetInstanceID()}] y={y:F2} >= {autoRespawnY:F2}");
        }

        // ---- NORMAL STEP LOGIC ----
        MovePlayer();
        HandleHoldTimer();
        HandlePogoTimer();
        HandleAirTime();
        HandleDash();
        HandleAttack();

        ApplyStepRewards();
        ApplyMilestoneRewards();

        // Timeout: end episode if time limit exceeded
        if (Time.time - episodeStartTime >= episodeTimeLimit)
        {
            if (timeoutPenalty != 0f) AddReward(timeoutPenalty);
            EndEpisode();
            return;
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

    // ===== Grounding =====
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
        // Enemy body touch -> damage count
        if (HasTag(go, enemyBodyTag))
        {
            RegisterEnemyBodyTouch(other);
            return;
        }
    }

    private void RegisterEnemyBodyTouch(Collider2D source)
    {
        if (Time.time < invulnerableUntil) return;
        invulnerableUntil = Time.time + Mathf.Max(0f, touchInvulnerabilitySeconds);

        enemyBodyTouches = Mathf.Max(0, enemyBodyTouches + 1);
        if (perTouchPenalty != 0f) AddReward(perTouchPenalty);

        if (enemyBodyTouches >= Mathf.Max(1, maxEnemyBodyTouches))
        {
            OnDeath(source);
        }
    }

    // ===== Heuristic (keyboard) =====
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var da = actionsOut.DiscreteActions;

        // If keyboard control is disabled, output neutral actions.
        if (!enableKeyboardControl)
        {
            for (int i = 0; i < da.Length; i++) da[i] = 0;
            return;
        }

        // Horizontal (use GetKey, not GetKeyDown)
        int h = 0;
        if (Input.GetKey(leftKey)) h -= 1;
        if (Input.GetKey(rightKey)) h += 1;
        da[0] = h < 0 ? 1 : (h > 0 ? 2 : 0);   // 0 none, 1 left, 2 right

        // Vertical aim
        int v = 0;
        if (Input.GetKey(upKey)) v += 1;
        if (Input.GetKey(downKey)) v -= 1;
        da[4] = v > 0 ? 1 : (v < 0 ? 2 : 0);   // 0 neutral, 1 up, 2 down

        // Hold/presses (edge detection is done in OnActionReceived via prev* vars)
        da[1] = Input.GetKey(jumpKey) ? 1 : 0;    // jump held
        da[2] = Input.GetKey(dashKey) ? 1 : 0;    // dash press/hold
        da[3] = Input.GetKey(attackKey) ? 1 : 0;  // attack press/hold
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
        ));
        sensor.AddObservation(new Vector2(
            Mathf.Clamp(vel.x / 20f, -1f, 1f),
            Mathf.Clamp(vel.y / 20f, -1f, 1f)
        ));
        sensor.AddObservation(isGrounded ? 1f : 0f);
        sensor.AddObservation(dashing ? 1f : 0f);
        sensor.AddObservation(attacking ? 1f : 0f);
        sensor.AddObservation(lastFacingX >= 0f ? 1f : -1f);

        // Camera (optional vector obs)
        if (includeCameraObs && obsCamera != null)
        {
            var cpos = obsCamera.transform.position;
            float halfH = obsCamera.orthographicSize;
            float halfW = halfH * obsCamera.aspect;

            sensor.AddObservation(new Vector2(
                (cpos.x - minXY.x) / Mathf.Max(0.001f, arena.x),
                (cpos.y - minXY.y) / Mathf.Max(0.001f, arena.y)
            ));
            sensor.AddObservation(halfW / Mathf.Max(0.001f, arena.x));
            sensor.AddObservation(halfH / Mathf.Max(0.001f, arena.y));
        }

        // Final goal (optional)
        if (finalGoal != null)
        {
            Vector2 toGoal = (Vector2)finalGoal.position - pos;
            sensor.AddObservation(new Vector2(
                Mathf.Clamp(toGoal.x / scanRadius, -1f, 1f),
                Mathf.Clamp(toGoal.y / scanRadius, -1f, 1f)
            ));
            sensor.AddObservation(Mathf.Clamp01(toGoal.magnitude / scanRadius));
        }
        else
        {
            sensor.AddObservation(Vector2.zero);
            sensor.AddObservation(1f);
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
                ));
                sensor.AddObservation(d / scanRadius);
                sensor.AddObservation(1f);
            }
            else
            {
                sensor.AddObservation(Vector2.zero);
                sensor.AddObservation(1f);
                sensor.AddObservation(0f);
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

    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;                 // optional (assign in Inspector)
    [SerializeField] private Vector2 defaultSpawn = new Vector2(1.5f, 0f); // fallback if no spawnPoint

    public override void OnEpisodeBegin()
    {
        ResolveFinalGoalByTagIfNeeded();

        // ---- set position FIRST ----
        Vector2 spawn = spawnPoint ? (Vector2)spawnPoint.position : defaultSpawn;
        rb.position = spawn;
        transform.position = spawn; // keep Transform & RB in sync (important if interpolation is on)

        // ---- reset dynamics ----
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.gravityScale = baseGravityScale;

        // ---- reset gameplay state ----
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

        // timers
        episodeStartTime = Time.time;

        // init shaping caches
        prevGoalDist = DistanceTo(finalGoal);
        prevNearestGemDist = FindNearestDistance(gemMask);
        prevNearestEnemyDist = FindNearestDistance(enemyMask);

        // milestones
        startGoalDist = prevGoalDist;
        nextGoalMilestoneProgress = goalMilestoneMeters;

        // damage state
        enemyBodyTouches = 0;
        invulnerableUntil = 0f;

        // heuristic scheduler
        heuristicFrameCountdown = 0;
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

    // per-episode record-based goal milestones
    private void ApplyMilestoneRewards()
    {
        if (!enableGoalMilestones || finalGoal == null || goalMilestoneMeters <= 0f || R.goalMilestoneReward == 0f)
            return;

        float currentDist = DistanceTo(finalGoal);
        float progress = Mathf.Max(0f, startGoalDist - currentDist); // meters improved vs episode start

        while (progress + 1e-6f >= nextGoalMilestoneProgress)
        {
            AddReward(R.goalMilestoneReward);
            nextGoalMilestoneProgress += goalMilestoneMeters;
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
        // Destroy(gem.gameObject);
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

    private void ResolveFinalGoalByTagIfNeeded()
    {
        if (finalGoal != null) return;
        if (string.IsNullOrEmpty(goalTag)) return;

        var all = GameObject.FindGameObjectsWithTag(goalTag);
        if (all == null || all.Length == 0) return;

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
