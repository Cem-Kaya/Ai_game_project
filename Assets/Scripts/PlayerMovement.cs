using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerMovement : MonoBehaviour
{
    [Header("Sprite")]
    [SerializeField] private Animator animator;
    [SerializeField] private SpriteRenderer spriteRenderer;

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

    [Header("Mapping (vy -> 0..1)")]
    [Tooltip("Approx max upward speed at jump start")]
    [SerializeField] private float maxUpwardSpeed = 12f;
    [Tooltip("Approx max downward (terminal) speed")]
    [SerializeField] private float maxDownwardSpeed = 20f;
    [Tooltip("Time to smooth parameter changes")]
    [SerializeField] private float smoothTime = 0.08f;
    private float velRef; // for SmoothDamp

    // required components
    private Rigidbody2D rb;
    private InputSystem_Actions inputs;

    // movement / facing
    private Vector2 moveInput;
    private float lastFacingX = 1f;

    // ground and jump state
    private bool isGrounded;
    private bool holdJump;
    private bool canHoldJump;
    private float holdTimer;

    // dash state
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
        HandleJumpingAnimation();
        HandleRunningAnimation();
    }

    private void HandleRunningAnimation()
    {
        animator.SetBool("Running", rb.linearVelocityX != 0f);
        animator.SetBool("ReturnIdle", rb.linearVelocityX == 0f);
    }

    private void FixedUpdate()
    {
        MovePlayer();
        HandleJumpHold();
        HandleDash();
    }

    void MovePlayer()
    {
        if (dashing) return;

        rb.linearVelocity = new Vector2(moveInput.x * speed, rb.linearVelocity.y);
        
        UpdateFacing(moveInput.x);
    }

    private void UpdateFacing(float x)
    {
        if (x == 0f) return;
        if (x > 0f) spriteRenderer.flipX = false;
        else spriteRenderer.flipX = true;
    }

    void OnJumpPressed()
    {
        if (isGrounded)
        {
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;

            canHoldJump = true;
            holdTimer = maxHoldTime;
            holdJump = true;

            animator.SetTrigger("Jump");
        }
    }

    void OnJumpReleased()
    {
        holdJump = false;
    }

    void HandleJumpHold()
    {
        // While rising, button held, and still within the hold window -> add gentle upward force
        if (canHoldJump && holdJump && rb.linearVelocity.y > 0f && holdTimer > 0f)
        {
            holdTimer -= Time.fixedDeltaTime;
        }
        else
        {
            canHoldJump = false;
        }
    }

    void HandleJumpingAnimation()
    {
        if (!isGrounded)
        {
            float vy = rb.linearVelocity.y;
            // Map vy to [0..1]: up (maxUp) -> 0, down (-maxDown) -> 1
            float t = Mathf.InverseLerp(maxUpwardSpeed, -maxDownwardSpeed, vy);
            animator.SetFloat("JumpBlend", t);

            return;
            /*
            float vy = rb.linearVelocityY;

            // InverseLerp: value==maxUp -> 0, value==-maxDown -> 1
            float t = Mathf.InverseLerp(maxUpwardSpeed, -maxDownwardSpeed, vy);
            t = Mathf.Clamp01(t);

            float current = animator.GetFloat("JumpBlend");
            float smoothed = Mathf.SmoothDamp(current, t, ref velRef, smoothTime);
            animator.SetFloat("JumpBlend", smoothed);*/
        }
    }

    void TuneGravityForFeel()
    {
        // Variable jump height + snappier fall
        if (rb.linearVelocity.y < 0f)
        {
            rb.gravityScale = fallScale;
        }
        else if (rb.linearVelocity.y > 0f && (!holdJump || !canHoldJump))
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
        if (dashing) return;

        animator.speed = 0.2f; //TODO: Smooth dashing anim, Change later
        dashing = true;
        dashTimer = dashDuration;

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
            animator.speed = 1f; //TODO: Smooth dashing anim, Change later
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
        {
            isGrounded = true;
            animator.SetBool("Grounded", true);
        }
    }

    private void OnCollisionExit2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = false;
    }
}
