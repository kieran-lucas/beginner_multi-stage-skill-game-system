using System;
using UnityEngine;
using AnimeFighter.Combat;

namespace AnimeFighter.Player
{
    /// <summary>
    /// Responsive Rigidbody controller for the Void Sorcerer. Drives ground
    /// movement, jump (with coyote time + buffered press), dash, block, and
    /// auto-facing. State + events are exposed so CombatController,
    /// EnergySystem, VFXManager, CameraEffects, and a future Animator can
    /// react without this class needing to know about them.
    /// Implements <see cref="IBlockable"/> so attackers can query block state
    /// through the interface instead of casting to PlayerController.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class PlayerController : MonoBehaviour, IBlockable
    {
        // ============ Inspector ============

        [Header("Anchors")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform attackOrigin;
        [SerializeField] private Transform skillOrigin;
        [SerializeField] private Transform enemyTarget;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 6f;
        [SerializeField] private float acceleration = 60f;
        [SerializeField] private float deceleration = 50f;
        [Tooltip("Z position the character is locked to (the fighting lane).")]
        [SerializeField] private float laneZ = 0f;

        [Header("Jump")]
        [SerializeField] private float jumpForce = 9f;
        [Tooltip("Grace window after walking off ground in which a jump still works.")]
        [SerializeField] private float coyoteTime = 0.1f;
        [Tooltip("Press-jump-just-before-landing grace window.")]
        [SerializeField] private float jumpBufferTime = 0.12f;
        [Tooltip("Extra gravity scale while airborne. >1 makes falls snappier.")]
        [SerializeField] private float gravityMultiplier = 1.6f;

        [Header("Dash")]
        [SerializeField] private float dashSpeed = 22f;
        [SerializeField] private float dashDuration = 0.18f;
        [SerializeField] private float dashCooldown = 0.55f;
        [Tooltip("Cursed Energy cost emitted via DashRequested. Subscribers spend.")]
        [SerializeField] private float dashEnergyCost = 0f;

        [Header("Block")]
        [Range(0f, 1f)]
        [SerializeField] private float blockMoveMultiplier = 0.35f;

        [Header("Ground Check")]
        [SerializeField] private float groundCheckRadius = 0.22f;
        [SerializeField] private LayerMask groundMask = ~0;

        [Header("Facing")]
        [Tooltip("Degrees per second when rotating visualRoot toward the target facing.")]
        [SerializeField] private float facingTurnSpeed = 1440f;
        [Tooltip("Horizontal distance below which facing direction stays sticky (anti-jitter).")]
        [SerializeField] private float facingDeadZone = 0.05f;

        [Header("Input Bindings")]
        [SerializeField] private KeyCode jumpKey = KeyCode.Space;
        [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
        [SerializeField] private KeyCode blockKey = KeyCode.I;
        [Tooltip("Mouse button for block. 0=LMB, 1=RMB, 2=MMB.")]
        [SerializeField] private int blockMouseButton = 1;

        // ============ Public state ============

        public bool IsGrounded { get; private set; }
        public bool IsDashing { get; private set; }
        public bool IsBlocking { get; private set; }
        public bool IsMovementLocked { get; private set; }
        public bool IsAirborne => !IsGrounded;

        /// <summary>+1 = facing world +X, -1 = facing world -X.</summary>
        public int FacingDirection { get; private set; } = 1;

        public Vector3 CurrentVelocity => _rb != null ? _rb.linearVelocity : Vector3.zero;
        public float MoveInput => _moveInput;

        public Transform VisualRoot => visualRoot;
        public Transform AttackOrigin => attackOrigin;
        public Transform SkillOrigin => skillOrigin;
        public Transform EnemyTarget { get => enemyTarget; set => enemyTarget = value; }

        // ============ Events ============

        public event Action OnJump;
        /// <summary>Argument is the dash direction unit vector (always along ±X).</summary>
        public event Action<Vector3> OnDashStart;
        public event Action OnDashEnd;
        public event Action OnBlockStart;
        public event Action OnBlockEnd;
        public event Action OnLand;

        /// <summary>
        /// Fired when a dash begins. Argument is the configured energy cost.
        /// EnergySystem subscribes to spend; the dash proceeds regardless for now
        /// — a later prompt can add a validator that cancels insufficient-energy dashes.
        /// </summary>
        public event Action<float> DashRequested;

        // ============ Internal ============

        private Rigidbody _rb;
        private float _moveInput;
        private float _coyoteCounter;
        private float _jumpBufferCounter;
        private float _dashTimer;
        private float _dashCooldownTimer;
        private Vector3 _dashDirection;
        private float _movementLockTimer;
        private Transform _facingOverride;

        // ============ Lifecycle ============

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb.interpolation == RigidbodyInterpolation.None)
                _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void Update()
        {
            TickTimers();
            ReadInput();
            UpdateFacing();
        }

        private void FixedUpdate()
        {
            UpdateGrounded();

            if (IsDashing) AdvanceDash();
            else
            {
                ApplyHorizontalMovement();
                ApplyExtraGravity();
            }

            LockToLane();
        }

        // ============ Timers ============

        private void TickTimers()
        {
            float dt = Time.deltaTime;
            if (_coyoteCounter > 0f) _coyoteCounter -= dt;
            if (_jumpBufferCounter > 0f) _jumpBufferCounter -= dt;
            if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;
            if (_movementLockTimer > 0f)
            {
                _movementLockTimer -= dt;
                if (_movementLockTimer <= 0f) IsMovementLocked = false;
            }
        }

        // ============ Input ============

        private void ReadInput()
        {
            float horizontalRaw = Input.GetAxisRaw("Horizontal");
            _moveInput = (IsDashing || IsMovementLocked) ? 0f : horizontalRaw;

            // Jump: buffer the press, fire when grounded (or within coyote window).
            if (Input.GetKeyDown(jumpKey)) _jumpBufferCounter = jumpBufferTime;
            if (_jumpBufferCounter > 0f && _coyoteCounter > 0f && !IsDashing && !IsMovementLocked)
            {
                PerformJump();
            }

            // Dash: pick direction from current input, fall back to facing.
            if (Input.GetKeyDown(dashKey) && CanDash())
            {
                int dir = (Mathf.Abs(horizontalRaw) > 0.01f)
                    ? (int)Mathf.Sign(horizontalRaw)
                    : FacingDirection;
                StartDash(dir);
            }

            // Block: held key OR mouse button. Auto-releases if movement gets locked.
            bool blockHeld = Input.GetKey(blockKey) || Input.GetMouseButton(blockMouseButton);
            bool canBlock = !IsDashing && !IsMovementLocked;
            if (blockHeld && canBlock && !IsBlocking)
            {
                IsBlocking = true;
                OnBlockStart?.Invoke();
            }
            else if ((!blockHeld || !canBlock) && IsBlocking)
            {
                IsBlocking = false;
                OnBlockEnd?.Invoke();
            }
        }

        // ============ Grounded ============

        private void UpdateGrounded()
        {
            bool wasGrounded = IsGrounded;
            IsGrounded = groundCheck != null && Physics.CheckSphere(
                groundCheck.position, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);

            if (IsGrounded)
            {
                _coyoteCounter = coyoteTime;
                if (!wasGrounded) OnLand?.Invoke();
            }
            // If we just left the ground, coyoteCounter ticks down in TickTimers().
        }

        // ============ Horizontal movement ============

        private void ApplyHorizontalMovement()
        {
            float speedMul = IsBlocking ? blockMoveMultiplier : 1f;
            float targetX = _moveInput * moveSpeed * speedMul;
            // MoveTowards-style accel/decel gives crisp control without ever overshooting.
            float rate = (Mathf.Abs(targetX) > 0.01f) ? acceleration : deceleration;

            Vector3 v = _rb.linearVelocity;
            v.x = Mathf.MoveTowards(v.x, targetX, rate * Time.fixedDeltaTime);
            _rb.linearVelocity = v;
        }

        private void ApplyExtraGravity()
        {
            if (IsGrounded || gravityMultiplier <= 1f) return;
            Vector3 v = _rb.linearVelocity;
            v.y += Physics.gravity.y * (gravityMultiplier - 1f) * Time.fixedDeltaTime;
            _rb.linearVelocity = v;
        }

        private void LockToLane()
        {
            // Snap Z back to lane if drifted (knockback, scripted forces, etc.).
            Vector3 p = _rb.position;
            if (Mathf.Abs(p.z - laneZ) > 0.0001f)
            {
                p.z = laneZ;
                _rb.position = p;
            }
            Vector3 v = _rb.linearVelocity;
            if (v.z != 0f)
            {
                v.z = 0f;
                _rb.linearVelocity = v;
            }
        }

        // ============ Jump ============

        private void PerformJump()
        {
            // Clear both windows so we don't fire twice on a single press.
            _jumpBufferCounter = 0f;
            _coyoteCounter = 0f;
            Vector3 v = _rb.linearVelocity;
            v.y = jumpForce;
            _rb.linearVelocity = v;
            OnJump?.Invoke();
        }

        // ============ Dash ============

        private bool CanDash()
        {
            return !IsDashing && !IsMovementLocked && _dashCooldownTimer <= 0f;
        }

        private void StartDash(int direction)
        {
            IsDashing = true;
            _dashTimer = dashDuration;
            _dashDirection = new Vector3(direction, 0f, 0f);

            // A dash cancels block — committing to the move.
            if (IsBlocking)
            {
                IsBlocking = false;
                OnBlockEnd?.Invoke();
            }

            DashRequested?.Invoke(dashEnergyCost);
            OnDashStart?.Invoke(_dashDirection);
        }

        private void AdvanceDash()
        {
            // Flat anime-style dash: zero Y velocity, fixed horizontal speed.
            Vector3 v = _dashDirection * dashSpeed;
            v.y = 0f;
            _rb.linearVelocity = v;

            _dashTimer -= Time.fixedDeltaTime;
            if (_dashTimer <= 0f) EndDash();
        }

        private void EndDash()
        {
            IsDashing = false;
            _dashCooldownTimer = dashCooldown;
            OnDashEnd?.Invoke();
        }

        // ============ Facing ============

        private void UpdateFacing()
        {
            Transform target = _facingOverride != null ? _facingOverride : enemyTarget;
            if (target == null) return;
            if (IsDashing) return; // freeze facing during dash for visual stability

            float dx = target.position.x - transform.position.x;
            if (Mathf.Abs(dx) >= facingDeadZone)
            {
                FacingDirection = (dx >= 0f) ? 1 : -1;
            }
            ApplyFacingRotation();
        }

        private void ApplyFacingRotation()
        {
            if (visualRoot == null) return;
            // World yaw 90° = visual faces +X, -90° = visual faces -X.
            // Setting world rotation keeps the physics root completely untouched,
            // so movement in world-space X stays unaffected by the flip.
            float yaw = (FacingDirection == 1) ? 90f : -90f;
            Quaternion targetWorld = Quaternion.Euler(0f, yaw, 0f);
            visualRoot.rotation = Quaternion.RotateTowards(
                visualRoot.rotation, targetWorld, facingTurnSpeed * Time.deltaTime);
        }

        // ============ Public API ============

        /// <summary>Lock movement for <paramref name="duration"/> seconds. Extends if already locked.</summary>
        public void LockMovement(float duration)
        {
            if (duration <= 0f) return;
            IsMovementLocked = true;
            if (duration > _movementLockTimer) _movementLockTimer = duration;
        }

        /// <summary>Toggle the movement lock without a timer.</summary>
        public void SetMovementLocked(bool locked)
        {
            IsMovementLocked = locked;
            if (!locked) _movementLockTimer = 0f;
        }

        /// <summary>Force facing toward <paramref name="target"/> until <see cref="ClearForcedFace"/>.</summary>
        public void ForceFaceTarget(Transform target) => _facingOverride = target;

        public void ClearForcedFace() => _facingOverride = null;

        /// <summary>Teleport (e.g. for skill repositions). Z is forced back to the lane.</summary>
        public void TeleportToLane(Vector3 worldPosition)
        {
            worldPosition.z = laneZ;
            _rb.position = worldPosition;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (groundCheck != null)
            {
                Gizmos.color = IsGrounded ? Color.green : Color.red;
                Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
            }
        }
#endif
    }
}
