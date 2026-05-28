using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AnimeFighter.Combat;
using AnimeFighter.Core;
using AnimeFighter.Player;
using AnimeFighter.UI;

namespace AnimeFighter.Skills
{
    /// <summary>
    /// Executes the Void Sorcerer's three-step State Skill. Input matching is
    /// delegated to InputSequenceDetector; this class owns energy, major state,
    /// release damage, cooldowns, and feedback events.
    /// </summary>
    public sealed class StateSkillController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private InputSequenceDetector sequenceDetector;
        [SerializeField] private PlayerController playerController;
        [SerializeField] private HealthSystem playerHealth;
        [SerializeField] private EnergySystem energySystem;
        [SerializeField] private CombatController combatController;
        [SerializeField] private Transform skillOrigin;
        [SerializeField] private Transform releaseTarget;
        [SerializeField] private UIManager uiManager;

        [Header("Sequences")]
        [Min(0.05f)]
        [SerializeField] private float sequenceWindowSeconds = 1.5f;
        [SerializeField] private KeyCode[] activationSequence = { KeyCode.Q, KeyCode.W, KeyCode.E };
        [SerializeField] private KeyCode[] chargeSequence = { KeyCode.W, KeyCode.E, KeyCode.Q };
        [SerializeField] private KeyCode[] releaseSequence = { KeyCode.E, KeyCode.Q, KeyCode.W };

        [Header("Energy")]
        [SerializeField] private float activationEnergyCost = 10f;
        [SerializeField] private float chargeEnergyCost = 20f;
        [SerializeField] private float releaseEnergyCost = 35f;
        [SerializeField] private float wrongInputPenaltyEnergy = 3f;
        [SerializeField] private float timeoutPenaltyEnergy = 0f;

        [Header("Timings")]
        [SerializeField] private float activationHoldTime = 0.35f;
        [SerializeField] private float chargeHoldTime = 0.5f;
        [SerializeField] private float releaseWindup = 0.35f;
        [SerializeField] private float releaseRecovery = 0.45f;
        [SerializeField] private float releaseCooldown = 3f;
        [SerializeField] private float failDelay = 0.3f;

        [Header("Release Attack")]
        [SerializeField] private float releaseDamage = 70f;
        [SerializeField] private float releaseKnockbackForce = 16f;
        [SerializeField] private float releaseRange = 6.5f;
        [SerializeField] private Vector3 releaseHitboxSize = new Vector3(6.5f, 2.25f, 1.2f);
        [SerializeField] private LayerMask releaseHitMask = ~0;
        [SerializeField] private bool releaseCanBeBlocked;

        [Header("Rules")]
        [SerializeField] private bool allowSkillInputWhileMovementLocked;
        [SerializeField] private bool allowReleaseWhileMovementLocked;
        [SerializeField] private bool debugLogs = true;

        public event Action<StateSkillState, StateSkillState> OnStateChanged;
        public event Action<IReadOnlyList<KeyCode>, int, float> OnInputSequenceProgress;
        public event Action<StateSkillState, KeyCode> OnInputSequenceFailed;
        public event Action<StateSkillState, int> OnInputSequenceTimedOut;
        public event Action OnActivationStarted;
        public event Action OnChargeStarted;
        public event Action OnReleaseReady;
        public event Action OnReleaseStarted;
        public event Action<DamageInfo, GameObject> OnReleaseHit;
        public event Action OnReleaseFinished;
        public event Action<float> OnSkillCooldownStarted;
        public event Action OnSkillCooldownEnded;

        private static readonly Collider[] ReleaseOverlapBuffer = new Collider[24];

        private readonly HashSet<GameObject> _releaseHitTargets = new HashSet<GameObject>();
        private Coroutine _releaseRoutine;
        private Coroutine _cooldownRoutine;
        private Coroutine _failDelayRoutine;
        private float _cooldownRemaining;
        private bool _inputSuspended;
        private bool _forcedFacingDuringRelease;

        public InputSequenceDetector SequenceDetector => sequenceDetector;
        public Transform SkillOrigin => skillOrigin;
        public PlayerController PlayerController => playerController;
        public EnergySystem EnergySystem => energySystem;
        public CombatController CombatController => combatController;
        public StateSkillState CurrentState { get; private set; } = StateSkillState.Normal;
        public int CurrentStage { get; private set; }
        public int TotalStages { get; private set; } = 3;
        public float CooldownRemaining => _cooldownRemaining;
        public float SequenceWindowSeconds => sequenceWindowSeconds;
        public float ActivationHoldTime => activationHoldTime;
        public float ChargeHoldTime => chargeHoldTime;
        public float ReleaseWindup => releaseWindup;
        public float ReleaseRecovery => releaseRecovery;
        public float ReleaseCooldown => releaseCooldown;
        public bool IsInCooldown => CurrentState == StateSkillState.Cooldown;

        private void Reset()
        {
            sequenceDetector = GetComponent<InputSequenceDetector>();
            playerController = GetComponent<PlayerController>();
            playerHealth = GetComponent<HealthSystem>();
            energySystem = GetComponent<EnergySystem>();
            combatController = GetComponent<CombatController>();
            if (playerController != null)
                skillOrigin = playerController.SkillOrigin;
        }

        private void Awake()
        {
            ResolveLocalReferences();
        }

        private void OnEnable()
        {
            SubscribeSequenceDetector();
            ArmSequenceForCurrentState();
            PushUiState();
        }

        private void Start()
        {
            ResolveSceneReferences();
            ArmSequenceForCurrentState();
            PushUiState();
        }

        private void Update()
        {
            if (sequenceDetector != null)
                sequenceDetector.InputEnabled = !_inputSuspended && CanAcceptSkillInput();
        }

        private void OnDisable()
        {
            UnsubscribeSequenceDetector();
            if (_releaseRoutine != null) StopCoroutine(_releaseRoutine);
            if (_cooldownRoutine != null) StopCoroutine(_cooldownRoutine);
            if (_failDelayRoutine != null) StopCoroutine(_failDelayRoutine);

            _releaseRoutine = null;
            _cooldownRoutine = null;
            _failDelayRoutine = null;
            _inputSuspended = false;
            _cooldownRemaining = 0f;

            if (_forcedFacingDuringRelease && playerController != null)
                playerController.ClearForcedFace();
            _forcedFacingDuringRelease = false;
        }

        /// <summary>
        /// Debug/recovery hook for prototype testing. Clears transient sequence,
        /// release, fail-delay, and cooldown state without changing HP or energy.
        /// </summary>
        public void ResetToNormal()
        {
            bool wasCoolingDown = CurrentState == StateSkillState.Cooldown;

            if (_releaseRoutine != null) StopCoroutine(_releaseRoutine);
            if (_cooldownRoutine != null) StopCoroutine(_cooldownRoutine);
            if (_failDelayRoutine != null) StopCoroutine(_failDelayRoutine);

            _releaseRoutine = null;
            _cooldownRoutine = null;
            _failDelayRoutine = null;
            _inputSuspended = false;
            _cooldownRemaining = 0f;

            if (playerController != null)
            {
                playerController.ClearForcedFace();
                playerController.SetMovementLocked(false);
            }
            _forcedFacingDuringRelease = false;

            if (sequenceDetector != null)
            {
                sequenceDetector.InputEnabled = true;
                sequenceDetector.ResetAttempt();
            }

            if (wasCoolingDown)
                OnSkillCooldownEnded?.Invoke();

            if (uiManager != null)
                uiManager.SetSkillCooldown(0f, releaseCooldown);

            ChangeState(StateSkillState.Normal);
            ArmSequenceForCurrentState();
            PushUiState();
            DebugLog("State Skill reset to Normal");
        }

        private void ResolveLocalReferences()
        {
            if (sequenceDetector == null) sequenceDetector = GetComponent<InputSequenceDetector>();
            if (playerController == null) playerController = GetComponent<PlayerController>();
            if (playerHealth == null) playerHealth = GetComponent<HealthSystem>();
            if (energySystem == null) energySystem = GetComponent<EnergySystem>();
            if (combatController == null) combatController = GetComponent<CombatController>();
            if (skillOrigin == null && playerController != null) skillOrigin = playerController.SkillOrigin;
            if (releaseTarget == null && playerController != null) releaseTarget = playerController.EnemyTarget;
        }

        private void ResolveSceneReferences()
        {
            ResolveLocalReferences();

            GameManager gameManager = GameManager.Instance;
            if (gameManager == null)
                return;

            if (releaseTarget == null) releaseTarget = gameManager.Enemy;
            if (uiManager == null) uiManager = gameManager.UI;
        }

        private void SubscribeSequenceDetector()
        {
            if (sequenceDetector == null)
                return;

            sequenceDetector.OnSequenceProgress += HandleSequenceProgress;
            sequenceDetector.OnSequenceCompleted += HandleSequenceCompleted;
            sequenceDetector.OnSequenceFailed += HandleSequenceFailed;
            sequenceDetector.OnSequenceTimedOut += HandleSequenceTimedOut;
        }

        private void UnsubscribeSequenceDetector()
        {
            if (sequenceDetector == null)
                return;

            sequenceDetector.OnSequenceProgress -= HandleSequenceProgress;
            sequenceDetector.OnSequenceCompleted -= HandleSequenceCompleted;
            sequenceDetector.OnSequenceFailed -= HandleSequenceFailed;
            sequenceDetector.OnSequenceTimedOut -= HandleSequenceTimedOut;
        }

        private void HandleSequenceProgress(IReadOnlyList<KeyCode> sequence, int completedInputs, float remainingTime)
        {
            OnInputSequenceProgress?.Invoke(sequence, completedInputs, remainingTime);
            if (uiManager != null)
                uiManager.SetSkillSequenceProgress(completedInputs, sequence.Count, remainingTime, sequenceWindowSeconds);
        }

        private void HandleSequenceCompleted(IReadOnlyList<KeyCode> sequence)
        {
            if (!CanAcceptSkillInput())
                return;

            switch (CurrentState)
            {
                case StateSkillState.Normal:
                    TryEnterActivation();
                    break;
                case StateSkillState.Activation:
                    TryEnterCharge();
                    break;
                case StateSkillState.Charge:
                    TryEnterReleaseReady();
                    break;
                default:
                    ArmSequenceForCurrentState();
                    break;
            }
        }

        private void HandleSequenceFailed(IReadOnlyList<KeyCode> sequence, KeyCode wrongKey, int failedIndex)
        {
            FailCurrentSequence(wrongKey, true, $"wrong input at {failedIndex}");
        }

        private void HandleSequenceTimedOut(IReadOnlyList<KeyCode> sequence, int completedInputs)
        {
            DrainEnergy(timeoutPenaltyEnergy);

            OnInputSequenceTimedOut?.Invoke(CurrentState, completedInputs);
            if (uiManager != null)
            {
                uiManager.SetSkillSequenceProgress(0, sequence.Count, sequenceWindowSeconds, sequenceWindowSeconds);
                uiManager.ShowSkillTimeoutFeedback();
            }

            DebugLog($"Sequence timed out in {CurrentState} after {completedInputs}/{sequence.Count}");
            ArmSequenceForCurrentState();
        }

        private void TryEnterActivation()
        {
            if (!TrySpendEnergy(activationEnergyCost, KeyCode.None))
                return;

            ChangeState(StateSkillState.Activation);
            OnActivationStarted?.Invoke();

            DebugLog("Activation started");
            ArmSequenceForCurrentState();
        }

        private void TryEnterCharge()
        {
            if (!TrySpendEnergy(chargeEnergyCost, KeyCode.None))
                return;

            ChangeState(StateSkillState.Charge);
            OnChargeStarted?.Invoke();

            DebugLog("Charge started");
            ArmSequenceForCurrentState();
        }

        private void TryEnterReleaseReady()
        {
            if (!CanExecuteRelease())
            {
                FailCurrentSequence(KeyCode.None, false, "release blocked");
                return;
            }

            if (!TrySpendEnergy(releaseEnergyCost, KeyCode.None))
                return;

            ChangeState(StateSkillState.ReleaseReady);
            OnReleaseReady?.Invoke();
            DebugLog("Release ready");

            if (sequenceDetector != null)
                sequenceDetector.ClearSequence();

            if (_releaseRoutine != null)
                StopCoroutine(_releaseRoutine);
            _releaseRoutine = StartCoroutine(ReleaseRoutine());
        }

        private IEnumerator ReleaseRoutine()
        {
            ChangeState(StateSkillState.Releasing);
            OnReleaseStarted?.Invoke();
            TriggerReleaseStartFeedback();

            float movementLockDuration = Mathf.Max(0f, releaseWindup) + Mathf.Max(0f, releaseRecovery);
            if (playerController != null)
            {
                playerController.LockMovement(movementLockDuration);
                if (releaseTarget != null)
                {
                    playerController.ForceFaceTarget(releaseTarget);
                    _forcedFacingDuringRelease = true;
                }
            }

            if (releaseWindup > 0f)
                yield return new WaitForSeconds(releaseWindup);

            ResolveReleaseHit();

            if (releaseRecovery > 0f)
                yield return new WaitForSeconds(releaseRecovery);

            if (_forcedFacingDuringRelease && playerController != null)
                playerController.ClearForcedFace();
            _forcedFacingDuringRelease = false;

            OnReleaseFinished?.Invoke();
            DebugLog("Release finished");

            _releaseRoutine = null;
            BeginCooldown();
        }

        private void BeginCooldown()
        {
            if (_cooldownRoutine != null)
                StopCoroutine(_cooldownRoutine);
            _cooldownRoutine = StartCoroutine(CooldownRoutine());
        }

        private IEnumerator CooldownRoutine()
        {
            ChangeState(StateSkillState.Cooldown);
            _cooldownRemaining = Mathf.Max(0f, releaseCooldown);
            OnSkillCooldownStarted?.Invoke(_cooldownRemaining);
            if (uiManager != null)
                uiManager.SetSkillCooldown(_cooldownRemaining, releaseCooldown);

            DebugLog($"Cooldown started for {_cooldownRemaining:0.00}s");

            while (_cooldownRemaining > 0f)
            {
                _cooldownRemaining -= Time.deltaTime;
                if (_cooldownRemaining < 0f)
                    _cooldownRemaining = 0f;

                if (uiManager != null)
                    uiManager.SetSkillCooldown(_cooldownRemaining, releaseCooldown);

                yield return null;
            }

            OnSkillCooldownEnded?.Invoke();
            if (uiManager != null)
                uiManager.SetSkillCooldown(0f, releaseCooldown);

            ChangeState(StateSkillState.Normal);
            DebugLog("Cooldown ended");
            _cooldownRoutine = null;
            ArmSequenceForCurrentState();
        }

        private void ResolveReleaseHit()
        {
            _releaseHitTargets.Clear();

            int facing = playerController != null ? playerController.FacingDirection : 1;
            Vector3 origin = skillOrigin != null ? skillOrigin.position : transform.position;
            Vector3 center = origin + Vector3.right * facing * (releaseRange * 0.5f);
            center.z = transform.position.z;

            Vector3 size = releaseHitboxSize;
            if (size.x <= 0f) size.x = releaseRange;
            if (size.y <= 0f) size.y = 2f;
            if (size.z <= 0f) size.z = 1f;

            int hitCount = Physics.OverlapBoxNonAlloc(
                center,
                size * 0.5f,
                ReleaseOverlapBuffer,
                Quaternion.identity,
                releaseHitMask,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = ReleaseOverlapBuffer[i];
                if (hitCollider == null)
                    continue;

                GameObject targetObject = hitCollider.attachedRigidbody != null
                    ? hitCollider.attachedRigidbody.gameObject
                    : hitCollider.gameObject;

                if (targetObject == gameObject || !_releaseHitTargets.Add(targetObject))
                    continue;

                IDamageable damageable = targetObject.GetComponent<IDamageable>();
                if (damageable == null)
                    damageable = targetObject.GetComponentInParent<IDamageable>();

                if (damageable == null || !damageable.IsAlive)
                    continue;

                Vector3 knockbackDirection = Vector3.right * facing;
                DamageInfo info = new DamageInfo(
                    releaseDamage,
                    hitCollider.ClosestPoint(center),
                    knockbackDirection,
                    releaseKnockbackForce,
                    gameObject,
                    releaseCanBeBlocked,
                    AttackType.Ultimate);

                damageable.TakeDamage(info);
                OnReleaseHit?.Invoke(info, targetObject);
                DebugLog($"Release hit {targetObject.name} for {releaseDamage:0.#}");
            }
        }

        private void TriggerReleaseStartFeedback()
        {
            // Visual systems subscribe to OnReleaseStarted and handle camera,
            // VFX, and screen overlays without coupling them to skill logic.
        }

        private void FailCurrentSequence(KeyCode wrongKey, bool drainPenalty, string reason)
        {
            if (drainPenalty)
                DrainEnergy(wrongInputPenaltyEnergy);

            OnInputSequenceFailed?.Invoke(CurrentState, wrongKey);

            DebugLog($"Sequence failed in {CurrentState}: {reason}");

            if (_failDelayRoutine != null)
                StopCoroutine(_failDelayRoutine);
            _failDelayRoutine = StartCoroutine(FailDelayRoutine());
        }

        private IEnumerator FailDelayRoutine()
        {
            _inputSuspended = true;
            if (sequenceDetector != null)
                sequenceDetector.InputEnabled = false;

            if (failDelay > 0f)
                yield return new WaitForSeconds(failDelay);

            _inputSuspended = false;
            _failDelayRoutine = null;
            ArmSequenceForCurrentState();
        }

        private bool TrySpendEnergy(float amount, KeyCode failedKey)
        {
            if (energySystem == null || amount <= 0f)
                return true;

            if (!energySystem.HasEnoughEnergy(amount) || !energySystem.SpendEnergy(amount))
            {
                FailCurrentSequence(failedKey, false, $"not enough Cursed Energy for cost {amount:0.#}");
                return false;
            }

            return true;
        }

        private void DrainEnergy(float amount)
        {
            if (energySystem == null || amount <= 0f)
                return;

            float spendAmount = Mathf.Min(amount, energySystem.Current);
            if (spendAmount > 0f)
                energySystem.SpendEnergy(spendAmount);
        }

        private bool CanAcceptSkillInput()
        {
            if (_inputSuspended)
                return false;

            if (CurrentState == StateSkillState.Releasing ||
                CurrentState == StateSkillState.ReleaseReady ||
                CurrentState == StateSkillState.Cooldown)
                return false;

            if (playerHealth != null && !playerHealth.IsAlive)
                return false;

            if (playerController != null &&
                playerController.IsMovementLocked &&
                !allowSkillInputWhileMovementLocked)
                return false;

            return true;
        }

        private bool CanExecuteRelease()
        {
            if (playerHealth != null && !playerHealth.IsAlive)
                return false;

            if (playerController != null &&
                playerController.IsMovementLocked &&
                !allowReleaseWhileMovementLocked)
                return false;

            return true;
        }

        private void ArmSequenceForCurrentState()
        {
            if (sequenceDetector == null)
                return;

            switch (CurrentState)
            {
                case StateSkillState.Normal:
                    sequenceDetector.ConfigureSequence(activationSequence, sequenceWindowSeconds);
                    break;
                case StateSkillState.Activation:
                    sequenceDetector.ConfigureSequence(chargeSequence, sequenceWindowSeconds);
                    break;
                case StateSkillState.Charge:
                    sequenceDetector.ConfigureSequence(releaseSequence, sequenceWindowSeconds);
                    break;
                default:
                    sequenceDetector.ClearSequence();
                    break;
            }
        }

        private void ChangeState(StateSkillState nextState)
        {
            if (CurrentState == nextState)
                return;

            StateSkillState previousState = CurrentState;
            CurrentState = nextState;
            CurrentStage = StageForState(nextState);

            PushUiState();
            OnStateChanged?.Invoke(previousState, nextState);
            DebugLog($"State {previousState} -> {nextState}");
        }

        private void PushUiState()
        {
            if (uiManager == null)
                return;

            uiManager.SetSkillStage(CurrentStage, TotalStages);
            uiManager.SetSkillState(CurrentState.ToString());
        }

        private int StageForState(StateSkillState state)
        {
            switch (state)
            {
                case StateSkillState.Activation:
                    return 1;
                case StateSkillState.Charge:
                    return 2;
                case StateSkillState.ReleaseReady:
                case StateSkillState.Releasing:
                    return 3;
                default:
                    return 0;
            }
        }

        private void DebugLog(string message)
        {
            if (debugLogs)
                Debug.Log($"[StateSkillController] {message}", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            int facing = playerController != null ? playerController.FacingDirection : 1;
            Vector3 origin = skillOrigin != null ? skillOrigin.position : transform.position;
            Vector3 center = origin + Vector3.right * facing * (releaseRange * 0.5f);
            center.z = transform.position.z;

            Vector3 size = releaseHitboxSize;
            if (size.x <= 0f) size.x = releaseRange;
            if (size.y <= 0f) size.y = 2f;
            if (size.z <= 0f) size.z = 1f;

            Gizmos.color = new Color(0.45f, 0.1f, 1f, 0.35f);
            Gizmos.DrawWireCube(center, size);
        }
#endif
    }
}
