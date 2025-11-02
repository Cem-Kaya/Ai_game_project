using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{

    [Header("Trigger Tags")]
    [SerializeField] private string gemTag = "Gem";
    [SerializeField] private string goalTag = "final";
    [SerializeField] private string hazardTag = "Hazard"; // Add a tag for hazards

    private float lapStartTime;

    [Header("Move")]
    private float speed = 7f;

    [Header("Jump (press)")]
    private float jumpForce = 11f;

    [Header("Jump (hold for higher)")]
    private float maxHoldTime = 0.3f;

    [Header("Gravity tuning (feel)")]
    private float baseGravityScale = 1f;
    private float fallGravityScale = 3f;
    private float jumpCutGravityScale = 6f;

    [Header("Dash")]
    private float dashSpeed = 20f;
    private float dashDuration = 0.2f;

    [Header("Attack")]
    private float attackDuration = 0.2f;
    private float attackCooldown = 0.4f;
    private float pogoVelocity = 8f;
    private float pogoTime = 0.2f;

    [SerializeField] private PlayerStats playerStats;

    // required components
    private Rigidbody2D rb;
    private InputSystem_Actions inputs;
    private GameObject attackArea;

    // movement / facing
    private Vector2 moveInput;
    private float lastFacingX = 1f;
    private bool movementLocked = false;

    // ground and jump state
    private bool isGrounded;
    private float groundContactNormalThreshold = 0.7f;
    private bool holdJump;
    private float holdTimer;
    private bool jumpCut;

    // dash state
    private bool canDash = true;
    private bool dashing;
    private float dashTimer;

    // attack state
    private bool attacking = false;
    private float attackTimer;
    private float attackCDTimer;
    private float attackDirectionYThreshold = 0.5f;
    public bool attackDownward;
    private float playerSizeX;
    private float playerSizeY;
    private Vector3 attackAreaDefaultPos;
    private float pogoTimer;


    [Header("Fail-safe")]
    [SerializeField] private float autoRespawnY = -20f;

    [Header("Spawn")]
    [SerializeField] private Transform spawnPoint;                 // optional
    [SerializeField] private Vector2 defaultSpawn = new Vector2(1.5f, 0f); // fallback

    private void StartLapTimer()
    {
        lapStartTime = Time.time;
    }


    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = baseGravityScale;

        inputs = new InputSystem_Actions();

        inputs.Player.Move.performed += ctx =>
        {
            moveInput = ctx.ReadValue<Vector2>();
            moveInput.x = (moveInput.x != 0f) ? Mathf.Sign(moveInput.x) : 0f;
            lastFacingX = (moveInput.x != 0f) ? Mathf.Sign(moveInput.x) : lastFacingX;
        };
        inputs.Player.Move.canceled += ctx => moveInput = new Vector2(0f, 0f);

        inputs.Player.Jump.performed += _ => OnJumpPressed();
        inputs.Player.Jump.canceled += _ => OnJumpReleased();

        inputs.Player.Dash.performed += _ => OnDashPressed();

        inputs.Player.Attack.performed += _ => OnAttackPressed();
        attackArea = transform.Find("AttackArea").gameObject;
        attackAreaDefaultPos = attackArea.transform.localPosition;
        attackArea.SetActive(false);

        CapsuleCollider2D col = GetComponent<CapsuleCollider2D>();
        playerSizeX = col.size.x;
        playerSizeY = col.size.y;
        StartLapTimer();
        playerStats.spawnPoint = spawnPoint;
    }

    private void OnEnable() => inputs.Player.Enable();
    private void OnDisable() => inputs.Player.Disable();

    private void Update()
    {
        TuneGravityForFeel();
    }

    private void FixedUpdate()
    {
        // NULL guard 
        if (!float.IsFinite(rb.position.y) || !float.IsFinite(autoRespawnY))
        {
            // count as a death
            if (LevelRotationManager.Instance != null)
                LevelRotationManager.Instance.RegisterDeath(LevelRotationManager.Competitor.Human);

            RespawnToSpawn("non-finite");
            return;
        }

        // Fall fail-safe
        if (CheckFallFailSafeSimple())
            return;

        // Normal step
        MovePlayer();
        HandleHoldTimer();
        HandlePogoTimer();
        HandleAirTime();
        HandleDash();
        HandleAttack();
    }

    private void UnlockMovement()
    {
        movementLocked = false;
    }

    private void MovePlayer()
    {
        if (movementLocked) return;

        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
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
            SfxSimple.Instance.Play("jump", LevelRotationManager.Competitor.Human, transform.position);

        }
    }

    private void OnJumpReleased()
    {
        if (holdJump)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }

        holdJump = false;
        jumpCut = false;
    }

    private void HandleHoldTimer()
    {
        if (holdJump && holdTimer > 0f)
        {
            holdTimer -= Time.fixedDeltaTime;
        }
        else
        {
            holdTimer = 0f;
        }
    }

    private void HandleAirTime()
    {
        if (rb.linearVelocity.y > 0f && !((holdJump && holdTimer > 0f) || pogoTimer > 0f))
        {
            jumpCut = true;
        }
        else if (rb.linearVelocity.y < 0f)
        {
            jumpCut = false;
            holdJump = false;
        }
    }

    private void TuneGravityForFeel()
    {
        if (rb.linearVelocity.y < 0f)
        {
            rb.gravityScale = fallGravityScale;
        }
        else if (jumpCut)
        {
            rb.gravityScale = jumpCutGravityScale;
        }
        else
        {
            rb.gravityScale = baseGravityScale;
        }
    }

    private void OnDashPressed()
    {
        if (movementLocked) return;
        if (!canDash) return;

        movementLocked = true;
        Invoke(nameof(UnlockMovement), dashDuration);
        dashing = true;
        dashTimer = dashDuration;
        canDash = false;

        float dir = (moveInput.x != 0) ? Mathf.Sign(moveInput.x) : lastFacingX;
        rb.linearVelocity = new Vector2(dir * dashSpeed, 0f);
        SfxSimple.Instance.Play("dash", LevelRotationManager.Competitor.Human, transform.position);

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
        if (movementLocked) return;
        if (attacking) return;
        if (attackCDTimer > 0f) return;

        attacking = true;
        attackTimer = attackDuration;
        attackCDTimer = attackCooldown;
        SfxSimple.Instance.Play("attack", LevelRotationManager.Competitor.Human, transform.position);

        Vector3 position;
        float angleZ;
        if (moveInput.y > attackDirectionYThreshold)
        {
            attackDownward = false;
            position = new Vector3(0f, playerSizeY * 0.5f, 0f) + attackAreaDefaultPos;
            angleZ = 90f;
        }
        else if (moveInput.y < -attackDirectionYThreshold && !isGrounded)
        {
            attackDownward = true;
            position = new Vector3(0f, -playerSizeY * 0.5f, 0f) + attackAreaDefaultPos;
            angleZ = -90f;
        }
        else
        {
            attackDownward = false;
            position = new Vector3(playerSizeX * 0.5f * lastFacingX, 0f, 0f) + attackAreaDefaultPos;
            angleZ = (lastFacingX >= 0f) ? 0f : 180f;
        }

        attackArea.transform.localPosition = position;
        attackArea.transform.localRotation = Quaternion.Euler(0f, 0f, angleZ);
        attackArea.SetActive(true);
    }

    private void HandleAttack()
    {
        if (attackCDTimer > 0f)
        {
            attackCDTimer -= Time.fixedDeltaTime;
        }
        else
        {
            attackCDTimer = 0f;
        }

        if (!attacking) return;

        if (attackTimer > 0f)
        {
            attackTimer -= Time.fixedDeltaTime;
        }
        else
        {
            attacking = false;
            attackArea.SetActive(false);
        }
    }

    public void Pogo()
    {
        canDash = true;
        pogoTimer = pogoTime;
        rb.linearVelocity = new Vector2(rb.linearVelocity.x, pogoVelocity);
    }

    private void HandlePogoTimer()
    {
        if (pogoTimer > 0f)
        {
            pogoTimer -= Time.fixedDeltaTime;
        }
        else
        {
            pogoTimer = 0f;
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Ground")) return;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y >= groundContactNormalThreshold)
            {
                isGrounded = true;
                canDash = true;
                break;
            }
        }
    }

    private void RespawnToSpawn(string reason = null)
    {

        Vector2 spawn = spawnPoint ? (Vector2)spawnPoint.position : defaultSpawn;

        // Position sync
        rb.position = spawn;
        transform.position = spawn;

        // Dynamics reset
        rb.linearVelocity = Vector2.zero;
        rb.angularVelocity = 0f;
        rb.gravityScale = baseGravityScale;

        // Gameplay state reset
        movementLocked = false;
        isGrounded = false;

        canDash = true;
        dashing = false;
        dashTimer = 0f;

        attacking = false;
        attackTimer = 0f;
        attackCDTimer = 0f;
        if (attackArea) attackArea.SetActive(false);

        holdJump = false;
        holdTimer = 0f;
        jumpCut = false;

        pogoTimer = 0f;
        moveInput = Vector2.zero;

        StartLapTimer();
        SfxSimple.Instance.Play("death", LevelRotationManager.Competitor.Human, transform.position);

    }

    private bool CheckFallFailSafeSimple()
    {
        if (rb.position.y < autoRespawnY)
        {
            // count as a death
            if (LevelRotationManager.Instance != null)
                LevelRotationManager.Instance.RegisterDeath(LevelRotationManager.Competitor.Human);

            RespawnToSpawn("fell");
            return true;
        }
        return false;
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        if (!collision.gameObject.CompareTag("Ground")) return;

        foreach (ContactPoint2D contact in collision.contacts)
        {
            if (contact.normal.y >= groundContactNormalThreshold)
            {
                isGrounded = true;
                canDash = true;
                break;
            }
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = false;
        }
    }

    private void OnTriggerEnter2D(Collider2D collision)
    {
        // Check for Gem
        if (collision.CompareTag(gemTag))
        {
            // 1. Register score with the manager
            if (LevelRotationManager.Instance != null)
            {
                LevelRotationManager.Instance.RegisterGemCollected(
                    LevelRotationManager.Competitor.Human, 1);
            }

            // 2. Destroy the gem
            Destroy(collision.gameObject);
            SfxSimple.Instance.Play("gem", LevelRotationManager.Competitor.Human, transform.position);
        }
        // Check for Goal
        else if (collision.CompareTag(goalTag))
        {
            if (LevelRotationManager.Instance != null)
            {
                // 1. Calculate and register this lap's time
                float lapTime = Time.time - lapStartTime;
                LevelRotationManager.Instance.RegisterFinish(
                    LevelRotationManager.Competitor.Human, lapTime);

                // 2. THIS IS THE FIX: Register the Win to advance the level
                SfxSimple.Instance.Play("final", LevelRotationManager.Competitor.Human, transform.position);
                LevelRotationManager.Instance.RegisterWin();
            }

            // 3. Respawn player locally (so they don't sit on the goal)
            // The manager will handle the scene change.
            RespawnToSpawn("goal");
        }
        // Check for Hazard
        else if (collision.CompareTag(hazardTag))
        {
            // 1. Register death
            if (LevelRotationManager.Instance != null)
            {
                LevelRotationManager.Instance.RegisterDeath(
                    LevelRotationManager.Competitor.Human);
            }

            // 2. Respawn at start (without reloading scene)
            RespawnToSpawn("hazard");
        }
    }
}
