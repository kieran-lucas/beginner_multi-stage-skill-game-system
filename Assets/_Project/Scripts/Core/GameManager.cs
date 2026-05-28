using System;
using UnityEngine;
using AnimeFighter.Combat;
using AnimeFighter.Enemy;
using AnimeFighter.Player;
using AnimeFighter.Skills;
using AnimeFighter.VFX;
using AnimeFighter.UI;

namespace AnimeFighter.Core
{
    /// <summary>
    /// Top-level coordinator for the prototype. Owns state transitions and
    /// holds references to the major subsystems that other scripts hook into.
    /// Future systems should fetch dependencies via <see cref="Instance"/>.
    /// </summary>
    public sealed class GameManager : MonoBehaviour
    {
        public enum GameState
        {
            Boot,
            Playing,
            Paused,
            GameOver
        }

        public static GameManager Instance { get; private set; }

        /// <summary>Fires whenever <see cref="State"/> changes. (previous, current)</summary>
        public event Action<GameState, GameState> StateChanged;

        [Header("Scene Actors")]
        [SerializeField] private Transform player;
        [SerializeField] private Transform enemy;

        [Header("Subsystems")]
        [SerializeField] private UIManager uiManager;
        [SerializeField] private VFXManager vfxManager;
        [SerializeField] private CameraEffects cameraEffects;

        [Header("Prototype Boot")]
        [SerializeField] private bool snapActorsToSpawnOnStart = true;
        [SerializeField] private Vector3 playerSpawnPosition = new Vector3(-3f, 1f, 0f);
        [SerializeField] private Vector3 enemySpawnPosition = new Vector3(3f, 1f, 0f);
        [SerializeField] private bool resetCameraOnStart = true;

        [Header("Debug Hotkeys")]
        [SerializeField] private bool enableDebugHotkeys = true;
        [SerializeField] private KeyCode addEnergyKey = KeyCode.F1;
        [SerializeField] private KeyCode damagePlayerKey = KeyCode.F2;
        [SerializeField] private KeyCode damageEnemyKey = KeyCode.F3;
        [SerializeField] private KeyCode resetSkillKey = KeyCode.F4;
        [SerializeField] private KeyCode forceEnemyDeathKey = KeyCode.F5;
        [SerializeField] private KeyCode resetEffectsKey = KeyCode.F6;
        [SerializeField] private float debugEnergyAmount = 35f;
        [SerializeField] private float debugDamageAmount = 25f;
        [SerializeField] private bool debugHotkeyLogs;

        public Transform Player => player;
        public Transform Enemy => enemy;
        public UIManager UI => uiManager;
        public VFXManager VFX => vfxManager;
        public CameraEffects Camera => cameraEffects;
        public GameState State { get; private set; } = GameState.Boot;

        private HealthSystem _playerHealth;
        private HealthSystem _enemyHealth;
        private EnergySystem _playerEnergy;
        private StateSkillController _playerStateSkill;
        private bool _deathEventsSubscribed;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            ResolveReferences();
        }

        private void Start()
        {
            ResolveReferences();
            ConfigureActors();
            SubscribeDeathEvents();

            if (resetCameraOnStart && cameraEffects != null)
                cameraEffects.ResetEffects();

            SetState(GameState.Playing);
        }

        private void Update()
        {
            if (enableDebugHotkeys)
                HandleDebugHotkeys();
        }

        public void SetState(GameState next)
        {
            if (next == State) return;
            GameState previous = State;
            State = next;
            StateChanged?.Invoke(previous, next);
        }

        public void Pause() => SetState(GameState.Paused);
        public void Resume() => SetState(GameState.Playing);
        public void EndGame() => SetState(GameState.GameOver);

        private void ResolveReferences()
        {
            if (uiManager == null) uiManager = GetComponent<UIManager>();
            if (vfxManager == null) vfxManager = GetComponent<VFXManager>();
            if (cameraEffects == null) cameraEffects = GetComponent<CameraEffects>();

            if (player != null)
            {
                _playerHealth = player.GetComponent<HealthSystem>();
                _playerEnergy = player.GetComponent<EnergySystem>();
                _playerStateSkill = player.GetComponent<StateSkillController>();
            }

            if (enemy != null)
                _enemyHealth = enemy.GetComponent<HealthSystem>();
        }

        private void ConfigureActors()
        {
            if (snapActorsToSpawnOnStart)
            {
                PlaceActor(player, playerSpawnPosition);
                PlaceActor(enemy, enemySpawnPosition);
            }

            if (player != null)
            {
                PlayerController playerController = player.GetComponent<PlayerController>();
                if (playerController != null && enemy != null)
                    playerController.EnemyTarget = enemy;
            }

            if (enemy != null)
            {
                EnemyAI enemyAI = enemy.GetComponent<EnemyAI>();
                if (enemyAI != null && player != null)
                    enemyAI.Target = player;
            }
        }

        private void PlaceActor(Transform actor, Vector3 position)
        {
            if (actor == null)
                return;

            PlayerController playerController = actor.GetComponent<PlayerController>();
            if (playerController != null)
            {
                playerController.TeleportToLane(position);
            }
            else
            {
                actor.position = position;
                Rigidbody body = actor.GetComponent<Rigidbody>();
                if (body != null)
                    body.position = position;
            }

            Rigidbody actorBody = actor.GetComponent<Rigidbody>();
            if (actorBody != null)
            {
                actorBody.linearVelocity = Vector3.zero;
                actorBody.angularVelocity = Vector3.zero;
            }
        }

        private void SubscribeDeathEvents()
        {
            if (_deathEventsSubscribed)
                return;

            if (_playerHealth != null)
                _playerHealth.OnDeath += HandlePlayerDeath;
            if (_enemyHealth != null)
                _enemyHealth.OnDeath += HandleEnemyDeath;

            _deathEventsSubscribed = true;
        }

        private void UnsubscribeDeathEvents()
        {
            if (!_deathEventsSubscribed)
                return;

            if (_playerHealth != null)
                _playerHealth.OnDeath -= HandlePlayerDeath;
            if (_enemyHealth != null)
                _enemyHealth.OnDeath -= HandleEnemyDeath;

            _deathEventsSubscribed = false;
        }

        private void HandlePlayerDeath()
        {
            FinishMatch(playerWon: false);
        }

        private void HandleEnemyDeath()
        {
            FinishMatch(playerWon: true);
        }

        private void FinishMatch(bool playerWon)
        {
            if (State == GameState.GameOver)
                return;

            if (cameraEffects != null)
                cameraEffects.ResetEffects();

            if (uiManager != null)
                uiManager.ShowGameOver(playerWon);

            SetState(GameState.GameOver);
        }

        private void HandleDebugHotkeys()
        {
            if (Input.GetKeyDown(addEnergyKey))
            {
                if (_playerEnergy != null)
                    _playerEnergy.AddEnergy(debugEnergyAmount);
                DebugLog($"Added {debugEnergyAmount:0.#} Cursed Energy");
            }

            if (Input.GetKeyDown(damagePlayerKey))
            {
                ApplyDebugDamage(_playerHealth, enemy != null ? enemy.gameObject : gameObject, debugDamageAmount, Vector3.left, AttackType.Skill);
                DebugLog($"Damaged player for {debugDamageAmount:0.#}");
            }

            if (Input.GetKeyDown(damageEnemyKey))
            {
                ApplyDebugDamage(_enemyHealth, player != null ? player.gameObject : gameObject, debugDamageAmount, Vector3.right, AttackType.Skill);
                DebugLog($"Damaged enemy for {debugDamageAmount:0.#}");
            }

            if (Input.GetKeyDown(resetSkillKey))
            {
                if (_playerStateSkill != null)
                    _playerStateSkill.ResetToNormal();
                DebugLog("Reset State Skill");
            }

            if (Input.GetKeyDown(forceEnemyDeathKey))
            {
                ApplyDebugDamage(_enemyHealth, player != null ? player.gameObject : gameObject, 9999f, Vector3.right, AttackType.Ultimate);
                DebugLog("Forced enemy death");
            }

            if (Input.GetKeyDown(resetEffectsKey))
            {
                if (cameraEffects != null)
                    cameraEffects.ResetEffects();
                Time.timeScale = 1f;
                DebugLog("Reset camera and time scale");
            }
        }

        private static void ApplyDebugDamage(HealthSystem targetHealth, GameObject source, float damage, Vector3 knockbackDirection, AttackType attackType)
        {
            if (targetHealth == null || !targetHealth.IsAlive || damage <= 0f)
                return;

            targetHealth.TakeDamage(new DamageInfo(
                damage,
                targetHealth.transform.position + Vector3.up,
                knockbackDirection,
                3f,
                source,
                canBeBlocked: true,
                attackType));
        }

        private void DebugLog(string message)
        {
            if (debugHotkeyLogs)
                Debug.Log($"[GameManager] {message}", this);
        }

        private void OnDestroy()
        {
            UnsubscribeDeathEvents();
            if (Instance == this) Instance = null;
        }
    }
}
