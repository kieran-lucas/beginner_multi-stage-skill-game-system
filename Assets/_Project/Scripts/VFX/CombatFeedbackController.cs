using UnityEngine;
using AnimeFighter.Combat;
using AnimeFighter.Core;
using AnimeFighter.Player;
using AnimeFighter.Skills;
using AnimeFighter.UI;

namespace AnimeFighter.VFX
{
    /// <summary>
    /// Event bridge for game-feel feedback. It observes gameplay events and
    /// delegates to VFXManager, CameraEffects, and UIManager without changing
    /// combat, health, energy, or skill outcomes.
    /// </summary>
    public sealed class CombatFeedbackController : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private PlayerController playerController;
        [SerializeField] private CombatController playerCombat;
        [SerializeField] private StateSkillController stateSkill;
        [SerializeField] private EnergySystem playerEnergy;
        [SerializeField] private HealthSystem playerHealth;
        [SerializeField] private HealthSystem enemyHealth;
        [SerializeField] private VFXManager vfxManager;
        [SerializeField] private CameraEffects cameraEffects;
        [SerializeField] private UIManager uiManager;

        [Header("Basic Hit Feedback")]
        [SerializeField] private float basicHitStop = 0.045f;
        [SerializeField] private float basicHitShakeIntensity = 0.075f;
        [SerializeField] private float basicHitShakeDuration = 0.11f;
        [SerializeField] private float comboShakeStep = 0.025f;
        [SerializeField] private Color hitFlashColor = new Color(0.7f, 0.85f, 1f, 1f);
        [Range(0f, 1f)]
        [SerializeField] private float hitFlashAlpha = 0.18f;

        [Header("Block Feedback")]
        [SerializeField] private float blockHitStop = 0.028f;
        [SerializeField] private float blockShakeIntensity = 0.045f;
        [SerializeField] private float blockShakeDuration = 0.075f;
        [SerializeField] private Color blockFlashColor = new Color(0.5f, 0.95f, 1f, 1f);

        [Header("Dash Feedback")]
        [SerializeField] private float dashShakeIntensity = 0.025f;
        [SerializeField] private float dashShakeDuration = 0.06f;

        [Header("State Skill Feedback")]
        [SerializeField] private float activationZoomFov = 34f;
        [SerializeField] private float activationZoomDuration = 0.16f;
        [SerializeField] private float chargeShakeIntensity = 0.05f;
        [SerializeField] private float chargeShakeDuration = 0.18f;
        [SerializeField] private float releaseWindupDuration = 0.35f;
        [SerializeField] private float releaseImpactDuration = 0.35f;
        [SerializeField] private float releaseHitStop = 0.08f;
        [SerializeField] private float releaseImpactShakeIntensity = 0.3f;
        [SerializeField] private float releaseImpactShakeDuration = 0.28f;
        [SerializeField] private Color releaseFlashColor = new Color(0.75f, 0.45f, 1f, 1f);
        [Range(0f, 1f)]
        [SerializeField] private float releaseFlashAlpha = 0.42f;

        [Header("Energy Feedback")]
        [SerializeField] private float energyGainVisualThreshold = 2f;

        private bool _subscribed;
        private int _currentCombo = 1;
        private float _previousEnergy;

        private void Start()
        {
            ResolveReferences();
            Subscribe();
            if (playerEnergy != null)
                _previousEnergy = playerEnergy.Current;
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void ResolveReferences()
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager != null)
            {
                if (vfxManager == null) vfxManager = gameManager.VFX;
                if (cameraEffects == null) cameraEffects = gameManager.Camera;
                if (uiManager == null) uiManager = gameManager.UI;

                if (gameManager.Player != null)
                {
                    if (playerController == null) playerController = gameManager.Player.GetComponent<PlayerController>();
                    if (playerCombat == null) playerCombat = gameManager.Player.GetComponent<CombatController>();
                    if (stateSkill == null) stateSkill = gameManager.Player.GetComponent<StateSkillController>();
                    if (playerEnergy == null) playerEnergy = gameManager.Player.GetComponent<EnergySystem>();
                    if (playerHealth == null) playerHealth = gameManager.Player.GetComponent<HealthSystem>();
                }

                if (gameManager.Enemy != null && enemyHealth == null)
                    enemyHealth = gameManager.Enemy.GetComponent<HealthSystem>();
            }

            if (vfxManager == null) vfxManager = GetComponent<VFXManager>();
            if (cameraEffects == null) cameraEffects = GetComponent<CameraEffects>();
            if (uiManager == null) uiManager = GetComponent<UIManager>();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            if (playerController != null)
                playerController.OnDashStart += HandleDashStarted;

            if (playerCombat != null)
            {
                playerCombat.OnAttackStarted += HandleAttackStarted;
                playerCombat.OnAttackHit += HandleAttackHit;
                playerCombat.OnBlocked += HandleBlockedHit;
            }

            if (stateSkill != null)
            {
                stateSkill.OnActivationStarted += HandleActivationStarted;
                stateSkill.OnChargeStarted += HandleChargeStarted;
                stateSkill.OnReleaseReady += HandleReleaseReady;
                stateSkill.OnReleaseStarted += HandleReleaseStarted;
                stateSkill.OnReleaseHit += HandleReleaseHit;
                stateSkill.OnInputSequenceFailed += HandleSkillInputFailed;
                stateSkill.OnSkillCooldownStarted += HandleSkillCooldownStarted;
            }

            if (playerEnergy != null)
                playerEnergy.OnEnergyChanged += HandleEnergyChanged;

            if (playerHealth != null)
                playerHealth.OnDamaged += HandlePlayerDamaged;

            if (enemyHealth != null)
                enemyHealth.OnDamaged += HandleEnemyDamaged;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (playerController != null)
                playerController.OnDashStart -= HandleDashStarted;

            if (playerCombat != null)
            {
                playerCombat.OnAttackStarted -= HandleAttackStarted;
                playerCombat.OnAttackHit -= HandleAttackHit;
                playerCombat.OnBlocked -= HandleBlockedHit;
            }

            if (stateSkill != null)
            {
                stateSkill.OnActivationStarted -= HandleActivationStarted;
                stateSkill.OnChargeStarted -= HandleChargeStarted;
                stateSkill.OnReleaseReady -= HandleReleaseReady;
                stateSkill.OnReleaseStarted -= HandleReleaseStarted;
                stateSkill.OnReleaseHit -= HandleReleaseHit;
                stateSkill.OnInputSequenceFailed -= HandleSkillInputFailed;
                stateSkill.OnSkillCooldownStarted -= HandleSkillCooldownStarted;
            }

            if (playerEnergy != null)
                playerEnergy.OnEnergyChanged -= HandleEnergyChanged;

            if (playerHealth != null)
                playerHealth.OnDamaged -= HandlePlayerDamaged;

            if (enemyHealth != null)
                enemyHealth.OnDamaged -= HandleEnemyDamaged;

            _subscribed = false;
        }

        private void HandleAttackStarted(int comboIndex)
        {
            _currentCombo = Mathf.Max(1, comboIndex);
            if (vfxManager != null && playerController != null)
                vfxManager.PlaySlashTrail(playerController.AttackOrigin);
        }

        private void HandleAttackHit(DamageInfo info, GameObject target)
        {
            float comboBoost = Mathf.Max(0, _currentCombo - 1) * comboShakeStep;

            if (vfxManager != null)
                vfxManager.PlayHitImpact(info.hitPoint, info.attackType);

            if (cameraEffects != null)
            {
                cameraEffects.HitStop(basicHitStop + comboBoost * 0.25f);
                cameraEffects.Shake(basicHitShakeIntensity + comboBoost, basicHitShakeDuration);
            }

            if (uiManager != null)
                uiManager.FlashImpact(hitFlashColor, Mathf.Clamp01(hitFlashAlpha + comboBoost), 0.1f);
        }

        private void HandleBlockedHit(DamageInfo info, GameObject target)
        {
            if (vfxManager != null)
                vfxManager.PlayBlockImpact(info.hitPoint);

            if (cameraEffects != null)
            {
                cameraEffects.HitStop(blockHitStop);
                cameraEffects.Shake(blockShakeIntensity, blockShakeDuration);
            }

            if (uiManager != null)
                uiManager.FlashImpact(blockFlashColor, 0.16f, 0.08f);
        }

        private void HandleDashStarted(Vector3 direction)
        {
            if (vfxManager != null && playerController != null)
                vfxManager.PlayDashAfterimage(playerController.VisualRoot);

            if (cameraEffects != null)
                cameraEffects.Shake(dashShakeIntensity, dashShakeDuration);
        }

        private void HandleActivationStarted()
        {
            Transform target = playerController != null ? playerController.transform : null;
            if (vfxManager != null)
                vfxManager.PlayActivationAura(target);

            if (cameraEffects != null)
                cameraEffects.ZoomPulse(activationZoomFov, activationZoomDuration);
        }

        private void HandleChargeStarted()
        {
            Transform target = playerController != null ? playerController.transform : null;
            if (vfxManager != null)
                vfxManager.PlayChargeAura(target);

            if (cameraEffects != null)
                cameraEffects.Shake(chargeShakeIntensity, chargeShakeDuration);
        }

        private void HandleReleaseReady()
        {
            Transform target = playerController != null ? playerController.transform : null;
            if (vfxManager != null)
                vfxManager.PlayChargeAura(target);

            if (cameraEffects != null)
                cameraEffects.ZoomPulse(activationZoomFov - 2f, activationZoomDuration);
        }

        private void HandleReleaseStarted()
        {
            Vector3 origin = GetSkillOrigin();
            Vector3 direction = GetPlayerFacingDirection();

            if (vfxManager != null)
                vfxManager.PlayReleaseBlast(origin, direction);

            if (cameraEffects != null)
                cameraEffects.ReleaseCinematic(releaseWindupDuration, releaseImpactDuration);

            if (uiManager != null)
                uiManager.ShowSkillScreenDarken(releaseWindupDuration + releaseImpactDuration);
        }

        private void HandleReleaseHit(DamageInfo info, GameObject target)
        {
            if (vfxManager != null)
                vfxManager.PlayHitImpact(info.hitPoint, AttackType.Ultimate);

            if (cameraEffects != null)
            {
                cameraEffects.HitStop(releaseHitStop);
                cameraEffects.Shake(releaseImpactShakeIntensity, releaseImpactShakeDuration);
                cameraEffects.CameraPunch(0.18f, releaseImpactShakeDuration);
            }

            if (uiManager != null)
                uiManager.FlashImpact(releaseFlashColor, releaseFlashAlpha, 0.16f);
        }

        private void HandleSkillInputFailed(StateSkillState state, KeyCode wrongKey)
        {
            Transform target = playerController != null ? playerController.transform : null;
            if (vfxManager != null)
                vfxManager.PlayWrongInputFlicker(target);

            if (cameraEffects != null)
                cameraEffects.Shake(0.045f, 0.08f);

            if (uiManager != null)
                uiManager.ShowSkillFailureFeedback();
        }

        private void HandleSkillCooldownStarted(float cooldownDuration)
        {
            if (vfxManager != null)
                vfxManager.StopStateAura();
        }

        private void HandleEnergyChanged(float current, float max)
        {
            if (current - _previousEnergy >= energyGainVisualThreshold)
            {
                Transform target = playerController != null ? playerController.transform : null;
                if (vfxManager != null)
                    vfxManager.PlayEnergyGain(target);
            }

            _previousEnergy = current;
        }

        private void HandlePlayerDamaged(DamageInfo info)
        {
            if (cameraEffects != null)
                cameraEffects.Shake(0.12f, 0.12f);

            if (uiManager != null)
                uiManager.FlashImpact(new Color(1f, 0.12f, 0.12f, 1f), 0.22f, 0.12f);
        }

        private void HandleEnemyDamaged(DamageInfo info)
        {
            if (vfxManager != null)
                vfxManager.PlayEnemyHitReaction(info.hitPoint);
        }

        private Vector3 GetSkillOrigin()
        {
            if (stateSkill != null && stateSkill.SkillOrigin != null)
                return stateSkill.SkillOrigin.position;

            if (playerController != null && playerController.SkillOrigin != null)
                return playerController.SkillOrigin.position;

            return playerController != null ? playerController.transform.position + Vector3.up : transform.position;
        }

        private Vector3 GetPlayerFacingDirection()
        {
            if (playerController != null)
                return Vector3.right * playerController.FacingDirection;

            return Vector3.right;
        }
    }
}
