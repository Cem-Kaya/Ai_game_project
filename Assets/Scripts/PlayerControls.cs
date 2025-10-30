using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
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
    }

    private void OnEnable() => inputs.Player.Enable();
    private void OnDisable() => inputs.Player.Disable();

    private void Update()
    {
        TuneGravityForFeel();
    }

    private void FixedUpdate()
    {
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
}
