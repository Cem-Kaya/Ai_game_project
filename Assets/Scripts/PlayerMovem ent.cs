using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using static UnityEngine.GraphicsBuffer;

public class PlayerMovement : Agent
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

    private Rigidbody2D rb;
    private float moveInput;

    private bool isGrounded;
    private bool pressJump;     // pressed this frame
    private bool holdJump;      // currently held
    private bool canHoldJump;   // we're in the "hold to rise more" window
    private float holdTimer;
    private int aMove = 0;   // -1,0,1 from policy
    private int aJump = 0;   // 0/1  from policy am not sure this will work for press stuff tho ??? 
    public bool agentControls = true; // turn off to play with keyboard
    private int prevAJump = 0;   // for edge-detect "press"


    void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = baseGravityScale;

        ComputeBoundsFromCamera();
    }

    void Update()
    {
        GetInput();
        HandleJumpPress();
        TuneGravityForFeel();
    }

    private void FixedUpdate()
    {
        MovePlayer();
        HandleJumpHold();
    }

    void GetInput()
    {
        if (!agentControls)
        {
            
            moveInput = Input.GetAxisRaw("Horizontal");
            pressJump = Input.GetButtonDown("Jump");
            holdJump = Input.GetButton("Jump");
        }
        else
        {
            // policy controls
            moveInput = aMove;                           // -1,0,1

            // convert 0/1 action to press + hold
            bool jumpHeldNow = (aJump == 1);
            bool jumpPressedEdge = (prevAJump == 0 && aJump == 1);
            pressJump = jumpPressedEdge && isGrounded;   // only “press” once when grounded
            holdJump = jumpHeldNow;                     // keep holding while action == 1

            prevAJump = aJump; // update for next frame
        }
    }

    void MovePlayer()
    {
        rb.linearVelocity = new Vector2(moveInput * speed, rb.linearVelocity.y);
    }

    void HandleJumpPress()
    {
        if (pressJump && isGrounded)
        {
            // Initial launch
            rb.linearVelocity = new Vector2(rb.linearVelocity.x, jumpForce);
            isGrounded = false;

            // Enable the hold window
            canHoldJump = true;
            holdTimer   = 0f;
        }
    }

    void HandleJumpHold()
    {
        // While rising, button held, and still within the hold window -> add gentle upward force
        if (canHoldJump && holdJump && rb.linearVelocity.y > 0f && holdTimer < maxHoldTime)
        {
            holdTimer += Time.fixedDeltaTime;
        }
        else
        {
            canHoldJump = false; // stop extending once released or timer elapsed
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

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
    }





    /// ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
 
    // observations
    [Header("Observations")]
    public Transform target;                       // optional
    public Vector2 minXY;                          // auto-filled from camera
    public Vector2 maxXY;                          // auto-filled from camera
    public bool autoBoundsFromCamera = true;
    public float cameraPadding = 0.5f;             // expand a bit


    void ComputeBoundsFromCamera()
    {
        var cam = Camera.main;
        if (cam == null || !cam.orthographic)
        {
            // fallback hardcoded box if no ortho camera
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

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var da = actionsOut.DiscreteActions;
        // Branch 0: move
        da[0] = Input.GetAxisRaw("Horizontal") switch { < 0 => 1, > 0 => 2, _ => 0 };
        // Branch 1: jump
        da[1] = (Input.GetKey(KeyCode.Space) || Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) ? 1 : 0;
    }


    public override void CollectObservations(VectorSensor sensor)
    {
        // Self (normalized)
        Vector2 pos = transform.position;
        Vector2 vel = rb.linearVelocity;

        Vector2 arena = (maxXY - minXY);
        sensor.AddObservation(new Vector2(
            (pos.x - minXY.x) / Mathf.Max(0.001f, arena.x),
            (pos.y - minXY.y) / Mathf.Max(0.001f, arena.y)
        ));                                            // +2
        sensor.AddObservation(new Vector2(
            Mathf.Clamp(vel.x / 12f, -1f, 1f),
            Mathf.Clamp(vel.y / 20f, -1f, 1f)
        ));                                            // +2
        sensor.AddObservation(isGrounded ? 1f : 0f);   // +1

        // Target (optional)
        if (target != null)
        {
            Vector2 toT = (Vector2)target.position - pos;
            sensor.AddObservation(new Vector2(
                Mathf.Clamp(toT.x / 20f, -1f, 1f),
                Mathf.Clamp(toT.y / 20f, -1f, 1f)
            ));                                        // +2
            sensor.AddObservation(Mathf.Clamp01(toT.magnitude / 25f)); // +1
        }
        else
        {
            sensor.AddObservation(Vector2.zero);        // +2
            sensor.AddObservation(1f);                  // +1
        }

        // Do NOT add manual Physics2D raycasts here.
        // The RayPerceptionSensor2D components you add in the Editor will auto-append their observations.
    }


    // agent action
    public override void OnActionReceived(ActionBuffers actions)
    {
        // Branch 0: 0 idle, 1 left, 2 right
        int m = actions.DiscreteActions[0];
        aMove = (m == 1) ? -1 : (m == 2 ? 1 : 0);

        // Branch 1: 0 none, 1 press/hold jump
        aJump = actions.DiscreteActions[1];

        // step cost
        AddReward(stepPenalty);
    }

    [Header("Episode / rewards")]
    public float stepPenalty = -0.001f;
    public float goalReward = 1.0f;
    public float deathPenalty = -1.0f;

    private void OnTriggerEnter2D(Collider2D other)
    {

        if (other.CompareTag("Star")) { AddReward(goalReward); EndEpisode();  Debug.Log("hit a Star");  }
        if (other.CompareTag("BAD"))  { AddReward(deathPenalty); EndEpisode(); }
    }

    public override void OnEpisodeBegin()
    {
        // reset velocity and optionally reposition
        rb.linearVelocity = Vector2.zero;
        // teleport to random pos in arena
        rb.position = new Vector2(
            Random.Range(minXY.x, maxXY.x),
            Random.Range(minXY.y, maxXY.y)
        );
    }
    [Header("Fail-safe")]
    public float autoRespawnY = -20f; // WTF 
    void LateUpdate()
    {
        if (transform.position.y < autoRespawnY)
        {
            AddReward(deathPenalty);
            EndEpisode();
        }
    }

}
