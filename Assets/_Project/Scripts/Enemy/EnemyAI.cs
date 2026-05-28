using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AnimeFighter.Combat;
using AnimeFighter.Core;
using AnimeFighter.Skills;
using AnimeFighter.VFX;

namespace AnimeFighter.Enemy
{
    public enum EnemyAIState
    {
        Idle,
        Approach,
        Retreat,
        StrafeOrHoldDistance,
        Attack,
        Block,
        Dash,
        Skill,
        Stunned,
        Dead
    }

    /// <summary>
    /// Medium-difficulty finite state AI for Crimson Curse. The enemy uses
    /// readable timers and probabilities instead of perfect frame reads, while
    /// still reacting to the player's State Skill pressure.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class EnemyAI : MonoBehaviour, IBlockable
    {
        [Header("Anchors")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform groundCheck;
        [SerializeField] private Transform attackOrigin;
        [SerializeField] private Transform skillOrigin;

        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private HealthSystem targetHealth;
        [SerializeField] private CombatController targetCombat;
        [SerializeField] private StateSkillController targetStateSkill;

        [Header("Self Wiring")]
        [SerializeField] private HealthSystem healthSystem;
        [SerializeField] private CombatController combatController;
        [SerializeField] private VFXManager vfxManager;

        [Header("Decision Tuning")]
        [SerializeField] private float aggroRange = 9f;
        [SerializeField] private float reactionTime = 0.32f;
        [Range(0f, 1f)]
        [SerializeField] private float aggression = 0.58f;
        [SerializeField] private float preferredRange = 1.65f;
        [SerializeField] private float rangeTolerance = 0.35f;
        [SerializeField] private float retreatHealthThreshold = 0.32f;
        [Range(0f, 1f)]
        [SerializeField] private float blockChance = 0.28f;
        [Range(0f, 1f)]
        [SerializeField] private float skillUseChance = 0.34f;

        [Header("Movement")]
        [SerializeField] private float moveSpeed = 4f;
        [SerializeField] private float retreatSpeed = 4.6f;
        [SerializeField] private float laneZ = 0f;
        [SerializeField] private float facingTurnSpeed = 1440f;
        [SerializeField] private float facingDeadZone = 0.05f;

        [Header("Basic Attack")]
        [SerializeField] private float meleeRange = 1.35f;
        [SerializeField] private float attackCooldown = 1.05f;
        [SerializeField] private float attackWindup = 0.18f;
        [SerializeField] private float attackRecovery = 0.28f;
        [SerializeField] private float attackDamage = 10f;
        [SerializeField] private float attackKnockback = 4f;
        [SerializeField] private Vector3 attackBoxSize = new Vector3(1.45f, 1.35f, 0.8f);
        [SerializeField] private LayerMask hittableMask = ~0;

        [Header("Block")]
        [SerializeField] private float blockDuration = 0.45f;
        [SerializeField] private float releasePanicBlockDuration = 0.75f;
        [Range(0f, 1f)]
        [SerializeField] private float blockDamageMultiplier = 0.25f;
        [Range(0f, 1f)]
        [SerializeField] private float blockKnockbackMultiplier = 0.15f;

        [Header("Dash")]
        [SerializeField] private float dashSpeed = 13f;
        [SerializeField] private float dashDuration = 0.16f;
        [SerializeField] private float dashCooldown = 1.8f;

        [Header("Crimson Slash")]
        [SerializeField] private float skillRange = 4.5f;
        [SerializeField] private float skillCooldown = 3.2f;
        [SerializeField] private float skillWindup = 0.42f;
        [SerializeField] private float skillRecovery = 0.38f;
        [SerializeField] private float skillDamage = 18f;
        [SerializeField] private float skillKnockback = 7f;
        [SerializeField] private Vector3 skillHitboxSize = new Vector3(4.3f, 1.35f, 0.9f);

        [Header("Hit Reaction")]
        [SerializeField] private float lightHitStun = 0.14f;
        [SerializeField] private float heavyHitStun = 0.28f;
        [SerializeField] private float heavyHitDamageThreshold = 22f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        public event Action OnEnemyAttackStarted;
        public event Action OnEnemySkillStarted;
        public event Action OnEnemyBlocked;
        public event Action<Vector3> OnEnemyDash;
        public event Action<EnemyAIState, EnemyAIState> OnEnemyStateChanged;

        private static readonly Collider[] HitBuffer = new Collider[16];

        private readonly HashSet<GameObject> _hitTargets = new HashSet<GameObject>();
        private Rigidbody _rb;
        private Coroutine _actionRoutine;
        private float _reactionTimer;
        private float _attackCooldownTimer;
        private float _skillCooldownTimer;
        private float _dashCooldownTimer;
        private float _playerAttackThreatTimer;
        private float _blockTimer;
        private float _dashTimer;
        private Vector3 _dashDirection;
        private bool _isBlocking;

        public EnemyAIState CurrentState { get; private set; } = EnemyAIState.Idle;
        public bool IsBlocking => _isBlocking;
        public int FacingDirection { get; private set; } = -1;
        public Transform VisualRoot => visualRoot;
        public Transform AttackOrigin => attackOrigin;
        public Transform SkillOrigin => skillOrigin;
        public Transform Target { get => target; set => target = value; }

        private void Reset()
        {
            healthSystem = GetComponent<HealthSystem>();
            combatController = GetComponent<CombatController>();
        }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb.interpolation == RigidbodyInterpolation.None)
                _rb.interpolation = RigidbodyInterpolation.Interpolate;

            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            SubscribeEvents();
            ResetReactionTimer();
        }

        private void Update()
        {
            TickTimers();
            UpdateFacing();

            if (CurrentState == EnemyAIState.Dead)
                return;

            if (healthSystem != null && !healthSystem.IsAlive)
            {
                EnterDeadState();
                return;
            }

            if (IsBusyState(CurrentState))
                return;

            _reactionTimer -= Time.deltaTime;
            if (_reactionTimer <= 0f)
            {
                DecideNextState();
                ResetReactionTimer();
            }
        }

        private void FixedUpdate()
        {
            if (_rb == null)
                return;

            switch (CurrentState)
            {
                case EnemyAIState.Approach:
                    MoveHorizontal(GetDirectionToTarget(), moveSpeed);
                    break;
                case EnemyAIState.Retreat:
                    MoveHorizontal(-GetDirectionToTarget(), retreatSpeed);
                    break;
                case EnemyAIState.StrafeOrHoldDistance:
                    HoldPreferredRange();
                    break;
                case EnemyAIState.Dash:
                    AdvanceDash();
                    break;
                case EnemyAIState.Attack:
                case EnemyAIState.Block:
                case EnemyAIState.Skill:
                case EnemyAIState.Dead:
                    StopHorizontalMotion();
                    break;
            }

            LockToLane();
        }

        private void OnDisable()
        {
            UnsubscribeEvents();
            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            _isBlocking = false;
        }

        private void ResolveReferences()
        {
            if (healthSystem == null) healthSystem = GetComponent<HealthSystem>();
            if (combatController == null) combatController = GetComponent<CombatController>();

            GameManager gameManager = GameManager.Instance;
            if (gameManager != null)
            {
                if (target == null) target = gameManager.Player;
                if (vfxManager == null) vfxManager = gameManager.VFX;
            }

            if (target != null)
            {
                if (targetHealth == null) targetHealth = target.GetComponent<HealthSystem>();
                if (targetCombat == null) targetCombat = target.GetComponent<CombatController>();
                if (targetStateSkill == null) targetStateSkill = target.GetComponent<StateSkillController>();
            }
        }

        private void SubscribeEvents()
        {
            if (healthSystem != null)
            {
                healthSystem.OnDamaged += HandleDamaged;
                healthSystem.OnDeath += EnterDeadState;
            }

            if (targetCombat != null)
                targetCombat.OnAttackStarted += HandlePlayerAttackStarted;

            if (targetStateSkill != null)
            {
                targetStateSkill.OnChargeStarted += HandlePlayerChargeStarted;
                targetStateSkill.OnReleaseReady += HandlePlayerReleaseReady;
                targetStateSkill.OnReleaseStarted += HandlePlayerReleaseStarted;
            }
        }

        private void UnsubscribeEvents()
        {
            if (healthSystem != null)
            {
                healthSystem.OnDamaged -= HandleDamaged;
                healthSystem.OnDeath -= EnterDeadState;
            }

            if (targetCombat != null)
                targetCombat.OnAttackStarted -= HandlePlayerAttackStarted;

            if (targetStateSkill != null)
            {
                targetStateSkill.OnChargeStarted -= HandlePlayerChargeStarted;
                targetStateSkill.OnReleaseReady -= HandlePlayerReleaseReady;
                targetStateSkill.OnReleaseStarted -= HandlePlayerReleaseStarted;
            }
        }

        private void TickTimers()
        {
            float dt = Time.deltaTime;
            if (_attackCooldownTimer > 0f) _attackCooldownTimer -= dt;
            if (_skillCooldownTimer > 0f) _skillCooldownTimer -= dt;
            if (_dashCooldownTimer > 0f) _dashCooldownTimer -= dt;
            if (_playerAttackThreatTimer > 0f) _playerAttackThreatTimer -= dt;

            if (_blockTimer > 0f)
            {
                _blockTimer -= dt;
                if (_blockTimer <= 0f && CurrentState == EnemyAIState.Block)
                    EndBlock();
            }
        }

        private void DecideNextState()
        {
            if (target == null || (targetHealth != null && !targetHealth.IsAlive))
            {
                ChangeState(EnemyAIState.Idle);
                return;
            }

            float distance = DistanceToTarget();
            StateSkillState playerSkillState = targetStateSkill != null
                ? targetStateSkill.CurrentState
                : StateSkillState.Normal;

            if (ReactToPlayerSkill(playerSkillState, distance))
                return;

            if (ShouldBlockIncomingPressure(playerSkillState))
            {
                StartBlock(blockDuration);
                return;
            }

            if (ShouldRetreat(distance))
            {
                if (distance < meleeRange && CanDash() && Roll(0.45f))
                    StartDash(awayFromPlayer: true);
                else
                    ChangeState(EnemyAIState.Retreat);
                return;
            }

            if (distance > aggroRange)
            {
                if (CanDash() && Roll(aggression * 0.45f))
                    StartDash(awayFromPlayer: false);
                else
                    ChangeState(EnemyAIState.Approach);
                return;
            }

            if (distance <= meleeRange && CanAttack() && Roll(aggression))
            {
                StartBasicAttack();
                return;
            }

            if (distance <= skillRange && distance > meleeRange && CanUseSkill() && Roll(skillUseChance))
            {
                StartCrimsonSlash();
                return;
            }

            if (distance > preferredRange + rangeTolerance)
                ChangeState(EnemyAIState.Approach);
            else if (distance < preferredRange - rangeTolerance)
                ChangeState(EnemyAIState.Retreat);
            else
                ChangeState(EnemyAIState.StrafeOrHoldDistance);
        }

        private bool ReactToPlayerSkill(StateSkillState playerSkillState, float distance)
        {
            switch (playerSkillState)
            {
                case StateSkillState.Activation:
                    if (ShouldBlockIncomingPressure(playerSkillState) || Roll(blockChance + 0.18f))
                    {
                        StartBlock(blockDuration + 0.15f);
                        return true;
                    }
                    break;
                case StateSkillState.Charge:
                    if (distance <= meleeRange && CanAttack() && Roll(aggression + 0.2f))
                    {
                        StartBasicAttack();
                        return true;
                    }

                    if (distance <= skillRange && CanUseSkill() && Roll(skillUseChance + 0.25f))
                    {
                        StartCrimsonSlash();
                        return true;
                    }

                    if (distance > meleeRange && CanDash() && Roll(0.45f))
                    {
                        StartDash(awayFromPlayer: false);
                        return true;
                    }
                    break;
                case StateSkillState.ReleaseReady:
                    if (CanDash() && Roll(0.72f))
                    {
                        StartDash(awayFromPlayer: true);
                        return true;
                    }

                    if (Roll(blockChance + 0.35f))
                    {
                        StartBlock(releasePanicBlockDuration);
                        return true;
                    }

                    ChangeState(EnemyAIState.Retreat);
                    return true;
                case StateSkillState.Releasing:
                    StartBlock(releasePanicBlockDuration);
                    return true;
            }

            return false;
        }

        private bool ShouldBlockIncomingPressure(StateSkillState playerSkillState)
        {
            if (_playerAttackThreatTimer <= 0f)
                return false;

            float chance = blockChance;
            if (playerSkillState == StateSkillState.Activation) chance += 0.18f;
            if (playerSkillState == StateSkillState.Charge) chance += 0.25f;
            if (_attackCooldownTimer > attackCooldown * 0.5f) chance *= 0.65f;

            return Roll(chance);
        }

        private bool ShouldRetreat(float distance)
        {
            bool lowHealth = healthSystem != null && healthSystem.Normalised <= retreatHealthThreshold;
            if (lowHealth && distance < preferredRange + 0.75f)
                return Roll(0.72f);

            return distance < preferredRange - rangeTolerance && Roll(0.55f - aggression * 0.25f);
        }

        private void StartBasicAttack()
        {
            if (_actionRoutine != null)
                StopCoroutine(_actionRoutine);

            _actionRoutine = StartCoroutine(BasicAttackRoutine());
        }

        private IEnumerator BasicAttackRoutine()
        {
            ChangeState(EnemyAIState.Attack);
            _attackCooldownTimer = attackCooldown;
            OnEnemyAttackStarted?.Invoke();

            if (attackWindup > 0f)
                yield return new WaitForSeconds(attackWindup);

            ResolveOverlapAttack(attackDamage, attackKnockback, meleeRange, attackBoxSize, AttackType.Light);

            if (attackRecovery > 0f)
                yield return new WaitForSeconds(attackRecovery);

            _actionRoutine = null;
            if (CurrentState != EnemyAIState.Dead)
                ChangeState(EnemyAIState.StrafeOrHoldDistance);
        }

        private void StartCrimsonSlash()
        {
            if (_actionRoutine != null)
                StopCoroutine(_actionRoutine);

            _actionRoutine = StartCoroutine(CrimsonSlashRoutine());
        }

        private IEnumerator CrimsonSlashRoutine()
        {
            ChangeState(EnemyAIState.Skill);
            _skillCooldownTimer = skillCooldown;
            OnEnemySkillStarted?.Invoke();

            if (vfxManager != null)
                vfxManager.PlayCrimsonSlash(GetSkillOrigin(), Vector3.right * FacingDirection);

            if (skillWindup > 0f)
                yield return new WaitForSeconds(skillWindup);

            ResolveOverlapAttack(skillDamage, skillKnockback, skillRange, skillHitboxSize, AttackType.Skill);

            if (skillRecovery > 0f)
                yield return new WaitForSeconds(skillRecovery);

            _actionRoutine = null;
            if (CurrentState != EnemyAIState.Dead)
                ChangeState(EnemyAIState.StrafeOrHoldDistance);
        }

        private void ResolveOverlapAttack(
            float damage,
            float knockback,
            float range,
            Vector3 boxSize,
            AttackType attackType)
        {
            _hitTargets.Clear();

            Vector3 origin = attackType == AttackType.Skill ? GetSkillOrigin() : GetAttackOrigin();
            Vector3 center = origin + Vector3.right * FacingDirection * (range * 0.5f);
            center.z = transform.position.z;

            Vector3 size = boxSize;
            if (size.x <= 0f) size.x = range;
            if (size.y <= 0f) size.y = 1.2f;
            if (size.z <= 0f) size.z = 0.8f;

            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                size * 0.5f,
                HitBuffer,
                Quaternion.identity,
                hittableMask,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = HitBuffer[i];
                if (hitCollider == null)
                    continue;

                GameObject targetObject = hitCollider.attachedRigidbody != null
                    ? hitCollider.attachedRigidbody.gameObject
                    : hitCollider.gameObject;

                if (targetObject == gameObject || !_hitTargets.Add(targetObject))
                    continue;

                IDamageable damageable = targetObject.GetComponent<IDamageable>();
                if (damageable == null)
                    damageable = targetObject.GetComponentInParent<IDamageable>();

                if (damageable == null || !damageable.IsAlive)
                    continue;

                bool blocked = IsTargetBlocking(targetObject);
                DamageInfo info = new DamageInfo(
                    damage,
                    hitCollider.ClosestPoint(center),
                    Vector3.right * FacingDirection,
                    knockback,
                    gameObject,
                    canBeBlocked: true,
                    attackType);

                if (blocked)
                {
                    info.damageAmount *= blockDamageMultiplier;
                    info.knockbackForce *= blockKnockbackMultiplier;
                    EnergySystem targetEnergy = targetObject.GetComponent<EnergySystem>();
                    if (targetEnergy == null) targetEnergy = targetObject.GetComponentInParent<EnergySystem>();
                    if (targetEnergy != null) targetEnergy.NotifyBlocked();
                }

                damageable.TakeDamage(info);

                if (vfxManager != null)
                {
                    if (blocked) vfxManager.PlayBlockImpact(info.hitPoint);
                    else vfxManager.PlayEnemyHitReaction(info.hitPoint);
                }
            }
        }

        private bool IsTargetBlocking(GameObject targetObject)
        {
            IBlockable blockable = targetObject.GetComponent<IBlockable>();
            if (blockable == null)
                blockable = targetObject.GetComponentInParent<IBlockable>();

            return blockable != null && blockable.IsBlocking;
        }

        private void StartBlock(float duration)
        {
            if (duration <= 0f || CurrentState == EnemyAIState.Dead)
                return;

            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            _isBlocking = true;
            _blockTimer = duration;
            ChangeState(EnemyAIState.Block);
            OnEnemyBlocked?.Invoke();
        }

        private void EndBlock()
        {
            _isBlocking = false;
            if (CurrentState == EnemyAIState.Block)
                ChangeState(EnemyAIState.StrafeOrHoldDistance);
        }

        private void StartDash(bool awayFromPlayer)
        {
            if (!CanDash() || CurrentState == EnemyAIState.Dead)
                return;

            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            _isBlocking = false;
            int direction = awayFromPlayer ? -GetDirectionToTarget() : GetDirectionToTarget();
            if (direction == 0) direction = awayFromPlayer ? -FacingDirection : FacingDirection;

            _dashDirection = Vector3.right * direction;
            _dashTimer = dashDuration;
            _dashCooldownTimer = dashCooldown;
            ChangeState(EnemyAIState.Dash);
            OnEnemyDash?.Invoke(_dashDirection);

            if (vfxManager != null)
                vfxManager.PlayEnemyDashAfterimage(visualRoot);
        }

        private void AdvanceDash()
        {
            if (_dashTimer <= 0f)
            {
                ChangeState(EnemyAIState.StrafeOrHoldDistance);
                return;
            }

            _rb.linearVelocity = new Vector3(_dashDirection.x * dashSpeed, _rb.linearVelocity.y, 0f);
            _dashTimer -= Time.fixedDeltaTime;
        }

        private void StartStun(float duration)
        {
            if (CurrentState == EnemyAIState.Dead || duration <= 0f)
                return;

            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            _isBlocking = false;
            _blockTimer = 0f;
            _actionRoutine = StartCoroutine(StunRoutine(duration));
        }

        private IEnumerator StunRoutine(float duration)
        {
            ChangeState(EnemyAIState.Stunned);
            yield return new WaitForSeconds(duration);

            _actionRoutine = null;
            if (CurrentState != EnemyAIState.Dead)
                ChangeState(EnemyAIState.StrafeOrHoldDistance);
        }

        private void EnterDeadState()
        {
            if (CurrentState == EnemyAIState.Dead)
                return;

            if (_actionRoutine != null)
            {
                StopCoroutine(_actionRoutine);
                _actionRoutine = null;
            }

            _isBlocking = false;
            _blockTimer = 0f;
            ChangeState(EnemyAIState.Dead);
            StopHorizontalMotion();
            enabled = false;
        }

        private void HandleDamaged(DamageInfo info)
        {
            if (healthSystem != null && !healthSystem.IsAlive)
            {
                EnterDeadState();
                return;
            }

            if (_isBlocking && info.canBeBlocked)
            {
                OnEnemyBlocked?.Invoke();
                return;
            }

            float stun = info.damageAmount >= heavyHitDamageThreshold || info.attackType == AttackType.Ultimate
                ? heavyHitStun
                : lightHitStun;
            StartStun(stun);
        }

        private void HandlePlayerAttackStarted(int comboIndex)
        {
            _playerAttackThreatTimer = Mathf.Lerp(0.2f, 0.34f, Mathf.Clamp01(comboIndex / 3f));
        }

        private void HandlePlayerChargeStarted()
        {
            _reactionTimer = Mathf.Min(_reactionTimer, reactionTime * 0.4f);
        }

        private void HandlePlayerReleaseReady()
        {
            if (CurrentState == EnemyAIState.Dead || CurrentState == EnemyAIState.Stunned)
                return;

            if (CanDash() && Roll(0.55f))
            {
                StartDash(awayFromPlayer: true);
                return;
            }

            if (Roll(blockChance + 0.35f))
                StartBlock(releasePanicBlockDuration);
        }

        private void HandlePlayerReleaseStarted()
        {
            if (CurrentState == EnemyAIState.Dead || CurrentState == EnemyAIState.Stunned)
                return;

            if (CurrentState != EnemyAIState.Dash && Roll(blockChance + 0.45f))
                StartBlock(releasePanicBlockDuration);
        }

        private void ChangeState(EnemyAIState nextState)
        {
            if (CurrentState == nextState)
                return;

            EnemyAIState previousState = CurrentState;
            CurrentState = nextState;
            OnEnemyStateChanged?.Invoke(previousState, nextState);
            DebugLog($"State {previousState} -> {nextState}");
        }

        private bool IsBusyState(EnemyAIState state)
        {
            return state == EnemyAIState.Attack ||
                   state == EnemyAIState.Block ||
                   state == EnemyAIState.Dash ||
                   state == EnemyAIState.Skill ||
                   state == EnemyAIState.Stunned;
        }

        private bool CanAttack() => _attackCooldownTimer <= 0f;
        private bool CanUseSkill() => _skillCooldownTimer <= 0f;
        private bool CanDash() => _dashCooldownTimer <= 0f;

        private float DistanceToTarget()
        {
            if (target == null)
                return float.PositiveInfinity;

            return Mathf.Abs(target.position.x - transform.position.x);
        }

        private int GetDirectionToTarget()
        {
            if (target == null)
                return FacingDirection;

            float dx = target.position.x - transform.position.x;
            if (Mathf.Abs(dx) < 0.001f)
                return FacingDirection;

            return dx >= 0f ? 1 : -1;
        }

        private Vector3 GetAttackOrigin()
        {
            return attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up * 0.2f;
        }

        private Vector3 GetSkillOrigin()
        {
            return skillOrigin != null ? skillOrigin.position : transform.position + Vector3.up * 0.6f;
        }

        private void MoveHorizontal(int direction, float speed)
        {
            if (direction == 0)
            {
                StopHorizontalMotion();
                return;
            }

            _rb.linearVelocity = new Vector3(direction * speed, _rb.linearVelocity.y, 0f);
        }

        private void HoldPreferredRange()
        {
            float distance = DistanceToTarget();
            if (distance > preferredRange + rangeTolerance)
                MoveHorizontal(GetDirectionToTarget(), moveSpeed * 0.65f);
            else if (distance < preferredRange - rangeTolerance)
                MoveHorizontal(-GetDirectionToTarget(), retreatSpeed * 0.55f);
            else
                StopHorizontalMotion();
        }

        private void StopHorizontalMotion()
        {
            Vector3 velocity = _rb.linearVelocity;
            velocity.x = Mathf.MoveTowards(velocity.x, 0f, moveSpeed * 8f * Time.fixedDeltaTime);
            velocity.z = 0f;
            _rb.linearVelocity = velocity;
        }

        private void LockToLane()
        {
            Vector3 position = _rb.position;
            if (Mathf.Abs(position.z - laneZ) > 0.0001f)
            {
                position.z = laneZ;
                _rb.position = position;
            }

            Vector3 velocity = _rb.linearVelocity;
            if (velocity.z != 0f)
            {
                velocity.z = 0f;
                _rb.linearVelocity = velocity;
            }
        }

        private void UpdateFacing()
        {
            if (target == null || CurrentState == EnemyAIState.Dead)
                return;

            float dx = target.position.x - transform.position.x;
            if (Mathf.Abs(dx) >= facingDeadZone)
                FacingDirection = dx >= 0f ? 1 : -1;

            if (visualRoot == null)
                return;

            float yaw = FacingDirection == 1 ? 90f : -90f;
            Quaternion targetRotation = Quaternion.Euler(0f, yaw, 0f);
            visualRoot.rotation = Quaternion.RotateTowards(
                visualRoot.rotation,
                targetRotation,
                facingTurnSpeed * Time.deltaTime);
        }

        private void ResetReactionTimer()
        {
            _reactionTimer = Mathf.Max(0.05f, reactionTime + UnityEngine.Random.Range(-0.08f, 0.08f));
        }

        private bool Roll(float chance)
        {
            return UnityEngine.Random.value <= Mathf.Clamp01(chance);
        }

        private void DebugLog(string message)
        {
            if (debugLogs)
                Debug.Log($"[EnemyAI] {message}", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            int facing = Application.isPlaying ? FacingDirection : -1;
            Vector3 meleeOrigin = attackOrigin != null ? attackOrigin.position : transform.position + Vector3.up * 0.2f;
            Vector3 meleeCenter = meleeOrigin + Vector3.right * facing * (meleeRange * 0.5f);
            meleeCenter.z = transform.position.z;
            Gizmos.color = new Color(1f, 0.05f, 0.05f, 0.45f);
            Gizmos.DrawWireCube(meleeCenter, attackBoxSize);

            Vector3 slashOrigin = skillOrigin != null ? skillOrigin.position : transform.position + Vector3.up * 0.6f;
            Vector3 slashCenter = slashOrigin + Vector3.right * facing * (skillRange * 0.5f);
            slashCenter.z = transform.position.z;
            Gizmos.color = new Color(0.4f, 0f, 0f, 0.45f);
            Gizmos.DrawWireCube(slashCenter, skillHitboxSize);
        }
#endif
    }
}
