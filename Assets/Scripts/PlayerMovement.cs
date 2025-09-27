using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Move")]
    public float speed = 7f;

    [Header("Jump (press)")]
    public float jumpForce = 10f;

    [Header("Jump (hold for higher)")]
    public float maxHoldTime = 0.3f;

    [Header("Gravity tuning (feel)")]
    public float baseGravityScale = 1f;
    public float fallScale = 3.0f;
    public float lowJumpScale = 6.0f;

    [Header("Dash")]
    public float dashSpeed = 20f;
    public float dashDuration = 0.2f;

    // required components
    private Rigidbody2D rb;
    private InputSystem_Actions inputs;

    // movement / facing
    private Vector2 moveInput;
    private float lastFacingX = 1f;
    private bool movementLocked = false;

    // ground and jump state
    private bool isGrounded;
    private bool holdJump;
    private float holdTimer;

    // dash state
    private bool canDash = true;
    private bool dashing;
    private float dashTimer;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = baseGravityScale;

        inputs = new InputSystem_Actions();

        inputs.Player.Move.performed += ctx =>
        {
            moveInput = ctx.ReadValue<Vector2>();
            lastFacingX = (moveInput.x != 0) ? Mathf.Sign(moveInput.x) : lastFacingX;
        };
        inputs.Player.Move.canceled += ctx => moveInput.x = 0f;

        inputs.Player.Jump.performed += _ => OnJumpPressed();
        inputs.Player.Jump.canceled += _ => OnJumpReleased();

        inputs.Player.Dash.performed += _ => OnDashPressed();
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
        HandleJumpHold();
        HandleDash();
    }

    void UnlockMovement()
    {
        movementLocked = false;
    }

    void MovePlayer()
    {
        if (movementLocked) return;

        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
    }

    void OnJumpPressed()
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

    void OnJumpReleased()
    {
        if (holdJump)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, 0f);
        }

        holdJump = false;
    }

    void HandleJumpHold()
    {
        // While rising, button held, and still within the hold window -> add gentle upward force
        if (holdJump && rb.linearVelocity.y > 0f && holdTimer > 0f)
        {
            holdTimer -= Time.fixedDeltaTime;
        }
        else
        {
            holdJump = false;
        }
    }

    void TuneGravityForFeel()
    {
        // Variable jump height + snappier fall
        if (rb.linearVelocity.y < 0f)
        {
            rb.gravityScale = fallScale;
        }
        else if (rb.linearVelocity.y > 0f && (!holdJump))
        {
            rb.gravityScale = lowJumpScale;
        }
        else
        {
            rb.gravityScale = baseGravityScale;
        }
    }

    void OnDashPressed()
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

    void HandleDash()
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
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
            canDash = true;
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = false;
    }
}
