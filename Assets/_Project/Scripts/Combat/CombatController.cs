using System;
using System.Collections.Generic;
using UnityEngine;
using AnimeFighter.Player;

namespace AnimeFighter.Combat
{
    /// <summary>
    /// Drives basic attacks for one character. Reads input only when
    /// <see cref="readKeyboardInput"/> is true (so enemy instances stay quiet),
    /// otherwise external systems (EnemyAI, StateSkillController) call
    /// <see cref="RequestAttack"/> directly.
    /// </summary>
    public sealed class CombatController : MonoBehaviour
    {
        // ============ Inspector ============

        [Header("Owner Wiring")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private EnergySystem energySystem;
        [Tooltip("Y reference for the hitbox vertical centre. X comes from facing direction.")]
        [SerializeField] private Transform attackOrigin;
        [Tooltip("Parent for any spawned hitbox prefabs (VFX, projectiles).")]
        [SerializeField] private Transform hitboxRoot;

        [Header("Input")]
        [Tooltip("If false, this controller ignores the keyboard — drive via RequestAttack().")]
        [SerializeField] private bool readKeyboardInput = true;
        [SerializeField] private KeyCode attackKey = KeyCode.J;

        [Header("Attack")]
        [SerializeField] private float attackDamage = 8f;
        [SerializeField] private float attackKnockback = 4f;
        [Tooltip("Horizontal distance from owner centre to the hitbox centre.")]
        [SerializeField] private float attackRange = 1.0f;
        [Tooltip("Full size of the OverlapBox used for hit detection.")]
        [SerializeField] private Vector3 attackBoxSize = new Vector3(1.6f, 1.4f, 0.8f);
        [SerializeField] private float attackCooldown = 0.35f;
        [SerializeField] private AttackType defaultAttackType = AttackType.Light;
        [SerializeField] private bool defaultAttackBlockable = true;

        [Header("Combo")]
        [Tooltip("Time after an attack during which the next press continues the combo.")]
        [SerializeField] private float comboWindow = 0.45f;
        [Tooltip("Time after which the combo counter resets back to 0.")]
        [SerializeField] private float comboResetTime = 0.9f;
        [SerializeField] private int maxCombo = 3;
        [Tooltip("Damage scalar applied per combo step beyond the first.")]
        [SerializeField] private float comboDamageMultiplier = 1.15f;
        [Tooltip("Knockback scalar applied per combo step beyond the first.")]
        [SerializeField] private float comboKnockbackMultiplier = 1.25f;

        [Header("Block Interaction")]
        [Range(0f, 1f)] [SerializeField] private float blockDamageMultiplier = 0.25f;
        [Range(0f, 1f)] [SerializeField] private float blockKnockbackMultiplier = 0.15f;

        [Header("Owner Lockout")]
        [Tooltip("How long the owner's movement is locked when an attack starts.")]
        [SerializeField] private float attackMovementLock = 0.18f;
        [SerializeField] private bool canAttackWhileDashing = false;

        [Header("Targeting")]
        [SerializeField] private LayerMask hittableMask = ~0;

        // ============ Public state ============

        public int CurrentCombo { get; private set; }
        public bool IsOnCooldown => _cooldownTimer > 0f;

        // ============ Events ============

        public event Action<int> OnAttackStarted;
        public event Action<DamageInfo, GameObject> OnAttackHit;
        public event Action OnAttackWhiffed;
        public event Action<DamageInfo, GameObject> OnBlocked;
        public event Action<int> OnComboChanged;

        // ============ Internal ============

        private float _cooldownTimer;
        private float _comboWindowTimer;
        private float _comboResetTimer;
        private readonly HashSet<GameObject> _hitThisAttack = new HashSet<GameObject>();
        private static readonly Collider[] _overlapBuffer = new Collider[16];

        // ============ Lifecycle ============

        private void Reset()
        {
            playerController = GetComponent<PlayerController>();
            energySystem = GetComponent<EnergySystem>();
        }

        private void Update()
        {
            TickTimers();
            if (readKeyboardInput && Input.GetKeyDown(attackKey) && CanAttack())
                RequestAttack();
        }

        private void TickTimers()
        {
            float dt = Time.deltaTime;
            if (_cooldownTimer > 0f) _cooldownTimer -= dt;
            if (_comboWindowTimer > 0f) _comboWindowTimer -= dt;
            if (_comboResetTimer > 0f)
            {
                _comboResetTimer -= dt;
                if (_comboResetTimer <= 0f) ResetCombo();
            }
        }

        // ============ Public API ============

        /// <summary>Returns true if an attack can start right now.</summary>
        public bool CanAttack()
        {
            if (_cooldownTimer > 0f) return false;
            if (playerController != null)
            {
                if (playerController.IsMovementLocked) return false;
                if (playerController.IsDashing && !canAttackWhileDashing) return false;
            }
            return true;
        }

        /// <summary>Trigger an attack on demand (AI, scripted sequences, debug).</summary>
        public bool RequestAttack()
        {
            if (!CanAttack()) return false;
            PerformAttack();
            return true;
        }

        // ============ Attack execution ============

        private void PerformAttack()
        {
            AdvanceCombo();

            _cooldownTimer = attackCooldown;
            _comboWindowTimer = comboWindow;
            _comboResetTimer = comboResetTime;
            _hitThisAttack.Clear();

            // Brief owner movement lock so the attack reads as committed.
            if (playerController != null && attackMovementLock > 0f)
                playerController.LockMovement(attackMovementLock);

            OnAttackStarted?.Invoke(CurrentCombo);

            ResolveOverlap();
        }

        private void AdvanceCombo()
        {
            bool insideWindow = CurrentCombo > 0
                                && _comboWindowTimer > 0f
                                && CurrentCombo < maxCombo;
            CurrentCombo = insideWindow ? CurrentCombo + 1 : 1;
            OnComboChanged?.Invoke(CurrentCombo);
        }

        private void ResolveOverlap()
        {
            int facingDir = GetOwnerFacing();
            Vector3 center = ComputeHitboxCenter(facingDir);
            Vector3 halfExtents = attackBoxSize * 0.5f;

            int hitCount = Physics.OverlapBoxNonAlloc(
                center, halfExtents, _overlapBuffer,
                Quaternion.identity, hittableMask, QueryTriggerInteraction.Collide);

            float damageMul = Mathf.Pow(comboDamageMultiplier, Mathf.Max(0, CurrentCombo - 1));
            float knockbackMul = Mathf.Pow(comboKnockbackMultiplier, Mathf.Max(0, CurrentCombo - 1));
            Vector3 knockbackDir = Vector3.right * facingDir;
            bool anyHit = false;

            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null) continue;
                GameObject target = col.attachedRigidbody != null
                    ? col.attachedRigidbody.gameObject
                    : col.gameObject;

                if (target == gameObject) continue;          // never hit self
                if (!_hitThisAttack.Add(target)) continue;   // already hit this swing

                IDamageable damageable = target.GetComponent<IDamageable>();
                if (damageable == null || !damageable.IsAlive) continue;

                DamageInfo info = new DamageInfo(
                    damageAmount: attackDamage * damageMul,
                    hitPoint: col.ClosestPoint(center),
                    knockbackDirection: knockbackDir,
                    knockbackForce: attackKnockback * knockbackMul,
                    source: gameObject,
                    canBeBlocked: defaultAttackBlockable,
                    attackType: defaultAttackType);

                bool blocked = IsTargetBlocking(target, info);
                if (blocked)
                {
                    info.damageAmount *= blockDamageMultiplier;
                    info.knockbackForce *= blockKnockbackMultiplier;
                }

                damageable.TakeDamage(info);

                if (blocked) OnBlocked?.Invoke(info, target);
                else OnAttackHit?.Invoke(info, target);

                if (energySystem != null)
                {
                    if (blocked) energySystem.NotifyBlocked();
                    else energySystem.NotifyHitLanded();
                }

                anyHit = true;
            }

            if (!anyHit) OnAttackWhiffed?.Invoke();
        }

        private bool IsTargetBlocking(GameObject target, DamageInfo info)
        {
            if (!info.canBeBlocked) return false;
            IBlockable blockable = target.GetComponent<IBlockable>();
            return blockable != null && blockable.IsBlocking;
        }

        // ============ Geometry ============

        private int GetOwnerFacing()
        {
            return playerController != null ? playerController.FacingDirection : 1;
        }

        private Vector3 ComputeHitboxCenter(int facingDir)
        {
            // Y comes from the AttackOrigin anchor (so designers can move the
            // hitbox up/down by dragging the anchor). X is locked to facing
            // direction so the hitbox flips with the player regardless of how
            // the visual is mirrored.
            float y = (attackOrigin != null) ? attackOrigin.position.y : transform.position.y;
            return new Vector3(
                transform.position.x + facingDir * attackRange,
                y,
                transform.position.z);
        }

        // ============ Combo reset ============

        private void ResetCombo()
        {
            if (CurrentCombo == 0) return;
            CurrentCombo = 0;
            _comboResetTimer = 0f;
            _comboWindowTimer = 0f;
            OnComboChanged?.Invoke(0);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            int dir = (playerController != null && Application.isPlaying)
                ? playerController.FacingDirection : 1;
            Vector3 center = ComputeHitboxCenter(dir);
            Gizmos.color = new Color(1f, 0.3f, 0.3f, 0.55f);
            Gizmos.DrawWireCube(center, attackBoxSize);
        }
#endif
    }
}
