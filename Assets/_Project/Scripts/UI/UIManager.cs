using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using AnimeFighter.Combat;
using AnimeFighter.Core;
using AnimeFighter.Skills;

namespace AnimeFighter.UI
{
    /// <summary>
    /// Runtime HUD for prototype readability: HP, Cursed Energy, State Skill
    /// state, sequence progress, combo, cooldown, and screen feedback overlays.
    /// Inspector references can replace generated widgets later.
    /// </summary>
    public sealed class UIManager : MonoBehaviour
    {
        private sealed class BarView
        {
            public Image fill;
            public Image delayedFill;
            public float displayed = 1f;
            public float delayed = 1f;
            public float target = 1f;
        }

        [Header("Scene References")]
        [SerializeField] private HealthSystem playerHealth;
        [SerializeField] private HealthSystem enemyHealth;
        [SerializeField] private EnergySystem playerEnergy;
        [SerializeField] private CombatController playerCombat;
        [SerializeField] private StateSkillController stateSkillController;
        [SerializeField] private InputSequenceDetector inputSequenceDetector;

        [Header("HUD Roots")]
        [SerializeField] private Transform playerHudRoot;
        [SerializeField] private Transform enemyHudRoot;
        [SerializeField] private bool createHudAtRuntime = true;
        [SerializeField] private Canvas hudCanvas;
        [SerializeField] private int hudSortingOrder = 100;

        [Header("HUD Widgets")]
        [SerializeField] private Image playerHealthFill;
        [SerializeField] private Image playerHealthDelayFill;
        [SerializeField] private Image enemyHealthFill;
        [SerializeField] private Image enemyHealthDelayFill;
        [SerializeField] private Image energyFill;
        [SerializeField] private Text stateText;
        [SerializeField] private Text sequenceTimerText;
        [SerializeField] private Image sequenceTimerFill;
        [SerializeField] private Image cooldownFill;
        [SerializeField] private Text cooldownText;
        [SerializeField] private Text comboText;
        [SerializeField] private Text timerText;

        [Header("State Skill Sequence")]
        [SerializeField] private RectTransform inputSequenceRoot;
        [SerializeField] private float defaultSequenceWindow = 1.5f;
        [SerializeField] private float activationEnergyCost = 10f;
        [SerializeField] private float chargeEnergyCost = 20f;
        [SerializeField] private float releaseEnergyCost = 35f;

        [Header("Screen Feedback")]
        [SerializeField] private bool createOverlayAtRuntime = true;
        [SerializeField] private Canvas feedbackCanvas;
        [SerializeField] private Image darkenOverlay;
        [SerializeField] private Image flashOverlay;
        [SerializeField] private int feedbackSortingOrder = 500;
        [Range(0f, 1f)]
        [SerializeField] private float releaseDarkenAlpha = 0.48f;

        [Header("Tuning")]
        [SerializeField] private float barLerpSpeed = 12f;
        [SerializeField] private float delayedBarLerpSpeed = 4f;
        [SerializeField] private float comboFadeDelay = 0.85f;
        [SerializeField] private float inputShakeDistance = 10f;
        [SerializeField] private float inputShakeDuration = 0.22f;

        private readonly BarView _playerHealthBar = new BarView();
        private readonly BarView _enemyHealthBar = new BarView();
        private readonly BarView _energyBar = new BarView();
        private readonly List<Image> _sequenceBoxes = new List<Image>(3);
        private readonly List<Text> _sequenceLabels = new List<Text>(3);

        private Font _font;
        private RectTransform _statePanel;
        private RectTransform _comboRoot;
        private Image _statePanelBackground;
        private Image _energyGlow;
        private Image _cooldownBackplate;
        private Coroutine _darkenRoutine;
        private Coroutine _flashRoutine;
        private Coroutine _inputShakeRoutine;
        private Coroutine _comboRoutine;
        private Coroutine _comboPopRoutine;
        private Coroutine _energyPulseRoutine;
        private Coroutine _releaseReadyPulseRoutine;
        private StateSkillState _currentSkillState = StateSkillState.Normal;
        private KeyCode[] _currentSequence = { KeyCode.Q, KeyCode.W, KeyCode.E };
        private int _completedSequenceInputs;
        private float _sequenceRemaining;
        private float _sequenceWindow;
        private float _currentEnergy;
        private float _maxEnergy = 100f;
        private bool _subscribed;

        private static readonly Color PlayerBlue = new Color(0.15f, 0.75f, 1f, 1f);
        private static readonly Color PlayerPurple = new Color(0.62f, 0.22f, 1f, 1f);
        private static readonly Color EnemyRed = new Color(0.95f, 0.06f, 0.05f, 1f);
        private static readonly Color EnemyDark = new Color(0.08f, 0f, 0.02f, 1f);
        private static readonly Color PanelDark = new Color(0.03f, 0.035f, 0.055f, 0.86f);
        private static readonly Color MutedText = new Color(0.74f, 0.78f, 0.9f, 1f);

        private void Awake()
        {
            _font = ResolveFont();
            EnsureHudCanvas();
            EnsureOverlayCanvas();
            BindBarViews();
        }

        private void Start()
        {
            ResolveReferences();
            Subscribe();
            PushInitialValues();
        }

        private void Update()
        {
            TickBar(_playerHealthBar);
            TickBar(_enemyHealthBar);
            TickBar(_energyBar);
            TickSequenceTimer();
        }

        private void OnDisable()
        {
            Unsubscribe();
            StopOverlayRoutine(ref _darkenRoutine);
            StopOverlayRoutine(ref _flashRoutine);
            StopOverlayRoutine(ref _inputShakeRoutine);
            StopOverlayRoutine(ref _comboRoutine);
            StopOverlayRoutine(ref _comboPopRoutine);
            StopOverlayRoutine(ref _energyPulseRoutine);
            StopOverlayRoutine(ref _releaseReadyPulseRoutine);
            SetImageAlpha(darkenOverlay, 0f);
            SetImageAlpha(flashOverlay, 0f);
        }

        public void SetPlayerHealth(float normalised)
        {
            SetBarTarget(_playerHealthBar, normalised);
        }

        public void SetPlayerEnergy(float normalised)
        {
            SetBarTarget(_energyBar, normalised);
            UpdateEnergyReadyPulse();
        }

        public void SetEnemyHealth(float normalised)
        {
            SetBarTarget(_enemyHealthBar, normalised);
        }

        /// <summary>Pushes the current state-skill stage to a future stage indicator.</summary>
        public void SetSkillStage(int stageIndex, int totalStages)
        {
            if (cooldownText != null && _currentSkillState != StateSkillState.Cooldown)
                cooldownText.text = totalStages > 0 && stageIndex > 0 ? $"{stageIndex}/{totalStages}" : "";
        }

        public void SetSkillState(string stateName)
        {
            if (!System.Enum.TryParse(stateName, out StateSkillState parsedState))
                parsedState = StateSkillState.Normal;

            SetSkillState(parsedState);
        }

        public void SetSkillSequenceProgress(
            int completedInputs,
            int totalInputs,
            float remainingTime,
            float windowSeconds)
        {
            _completedSequenceInputs = Mathf.Clamp(completedInputs, 0, Mathf.Max(0, totalInputs));
            _sequenceRemaining = Mathf.Max(0f, remainingTime);
            _sequenceWindow = windowSeconds > 0f ? windowSeconds : defaultSequenceWindow;
            UpdateSequenceVisuals();
        }

        public void ShowSkillFailureFeedback()
        {
            FlashImpact(new Color(0.9f, 0.05f, 1f, 1f), 0.32f, 0.18f);
            ShakeInputIndicator();
        }

        public void ShowSkillTimeoutFeedback()
        {
            FlashImpact(new Color(0.2f, 0.55f, 1f, 1f), 0.18f, 0.12f);
            ResetSequenceProgress();
        }

        public void SetSkillCooldown(float remainingSeconds, float totalSeconds)
        {
            float total = Mathf.Max(0.01f, totalSeconds);
            float remaining = Mathf.Max(0f, remainingSeconds);
            float progress = Mathf.Clamp01(1f - remaining / total);

            if (cooldownFill != null)
                cooldownFill.fillAmount = progress;

            if (cooldownText != null)
                cooldownText.text = remaining > 0f ? $"{remaining:0.0}s" : "";
        }

        public void ShowSkillScreenDarken(float duration)
        {
            ShowScreenDarken(releaseDarkenAlpha, duration);
        }

        public void ShowScreenDarken(float alpha, float duration)
        {
            EnsureOverlayCanvas();
            if (darkenOverlay == null)
                return;

            StopOverlayRoutine(ref _darkenRoutine);
            _darkenRoutine = StartCoroutine(DarkenRoutine(Mathf.Clamp01(alpha), Mathf.Max(0.05f, duration)));
        }

        public void FlashScreen(Color color, float duration)
        {
            FlashImpact(color, Mathf.Clamp01(color.a > 0f ? color.a : 0.3f), duration);
        }

        public void FlashImpact(Color color, float peakAlpha, float duration)
        {
            EnsureOverlayCanvas();
            if (flashOverlay == null)
                return;

            StopOverlayRoutine(ref _flashRoutine);
            _flashRoutine = StartCoroutine(FlashRoutine(color, Mathf.Clamp01(peakAlpha), Mathf.Max(0.02f, duration)));
        }

        public void ShakeInputIndicator()
        {
            if (inputSequenceRoot == null)
                return;

            StopOverlayRoutine(ref _inputShakeRoutine);
            _inputShakeRoutine = StartCoroutine(ShakeRectRoutine(inputSequenceRoot, inputShakeDuration, inputShakeDistance));
        }

        public void ShowGameOver(bool playerWon)
        {
            if (timerText != null)
                timerText.text = playerWon ? "VICTORY" : "DEFEAT";
        }

        private void ResolveReferences()
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager == null)
                return;

            if (gameManager.Player != null)
            {
                if (playerHealth == null) playerHealth = gameManager.Player.GetComponent<HealthSystem>();
                if (playerEnergy == null) playerEnergy = gameManager.Player.GetComponent<EnergySystem>();
                if (playerCombat == null) playerCombat = gameManager.Player.GetComponent<CombatController>();
                if (stateSkillController == null) stateSkillController = gameManager.Player.GetComponent<StateSkillController>();
                if (inputSequenceDetector == null) inputSequenceDetector = gameManager.Player.GetComponent<InputSequenceDetector>();
            }

            if (gameManager.Enemy != null && enemyHealth == null)
                enemyHealth = gameManager.Enemy.GetComponent<HealthSystem>();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            if (playerHealth != null) playerHealth.OnHealthChanged += HandlePlayerHealthChanged;
            if (enemyHealth != null) enemyHealth.OnHealthChanged += HandleEnemyHealthChanged;
            if (playerEnergy != null) playerEnergy.OnEnergyChanged += HandleEnergyChanged;

            if (playerCombat != null)
            {
                playerCombat.OnAttackHit += HandlePlayerAttackHit;
                playerCombat.OnComboChanged += HandleComboChanged;
            }

            if (stateSkillController != null)
            {
                stateSkillController.OnStateChanged += HandleStateChanged;
                stateSkillController.OnInputSequenceFailed += HandleStateSkillFailed;
                stateSkillController.OnInputSequenceTimedOut += HandleStateSkillTimedOut;
                stateSkillController.OnSkillCooldownStarted += HandleSkillCooldownStarted;
                stateSkillController.OnSkillCooldownEnded += HandleSkillCooldownEnded;
            }

            if (inputSequenceDetector != null)
            {
                inputSequenceDetector.OnSequenceStarted += HandleSequenceStarted;
                inputSequenceDetector.OnSequenceProgress += HandleSequenceProgress;
                inputSequenceDetector.OnSequenceFailed += HandleSequenceFailed;
                inputSequenceDetector.OnSequenceTimedOut += HandleSequenceTimedOut;
                inputSequenceDetector.OnSequenceCompleted += HandleSequenceCompleted;
            }

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (playerHealth != null) playerHealth.OnHealthChanged -= HandlePlayerHealthChanged;
            if (enemyHealth != null) enemyHealth.OnHealthChanged -= HandleEnemyHealthChanged;
            if (playerEnergy != null) playerEnergy.OnEnergyChanged -= HandleEnergyChanged;

            if (playerCombat != null)
            {
                playerCombat.OnAttackHit -= HandlePlayerAttackHit;
                playerCombat.OnComboChanged -= HandleComboChanged;
            }

            if (stateSkillController != null)
            {
                stateSkillController.OnStateChanged -= HandleStateChanged;
                stateSkillController.OnInputSequenceFailed -= HandleStateSkillFailed;
                stateSkillController.OnInputSequenceTimedOut -= HandleStateSkillTimedOut;
                stateSkillController.OnSkillCooldownStarted -= HandleSkillCooldownStarted;
                stateSkillController.OnSkillCooldownEnded -= HandleSkillCooldownEnded;
            }

            if (inputSequenceDetector != null)
            {
                inputSequenceDetector.OnSequenceStarted -= HandleSequenceStarted;
                inputSequenceDetector.OnSequenceProgress -= HandleSequenceProgress;
                inputSequenceDetector.OnSequenceFailed -= HandleSequenceFailed;
                inputSequenceDetector.OnSequenceTimedOut -= HandleSequenceTimedOut;
                inputSequenceDetector.OnSequenceCompleted -= HandleSequenceCompleted;
            }

            _subscribed = false;
        }

        private void PushInitialValues()
        {
            if (playerHealth != null)
                HandlePlayerHealthChanged(playerHealth.Current, playerHealth.Max);
            if (enemyHealth != null)
                HandleEnemyHealthChanged(enemyHealth.Current, enemyHealth.Max);
            if (playerEnergy != null)
                HandleEnergyChanged(playerEnergy.Current, playerEnergy.Max);
            if (stateSkillController != null)
                SetSkillState(stateSkillController.CurrentState);
            else
                SetSkillState(StateSkillState.Normal);

            if (inputSequenceDetector != null && inputSequenceDetector.HasSequence)
                SetExpectedSequence(inputSequenceDetector.CurrentSequence);
            else
                SetExpectedSequence(_currentSequence);
        }

        private void HandlePlayerHealthChanged(float current, float max)
        {
            SetPlayerHealth(Normalise(current, max));
        }

        private void HandleEnemyHealthChanged(float current, float max)
        {
            SetEnemyHealth(Normalise(current, max));
        }

        private void HandleEnergyChanged(float current, float max)
        {
            _currentEnergy = current;
            _maxEnergy = Mathf.Max(0.01f, max);
            SetPlayerEnergy(Normalise(current, max));
        }

        private void HandlePlayerAttackHit(DamageInfo info, GameObject target)
        {
            if (playerCombat == null)
                return;

            int combo = playerCombat.CurrentCombo;
            if (combo > 1)
                ShowCombo(combo);
        }

        private void HandleComboChanged(int combo)
        {
            if (combo <= 0)
                HideCombo();
        }

        private void HandleStateChanged(StateSkillState previous, StateSkillState current)
        {
            SetSkillState(current);
        }

        private void HandleStateSkillFailed(StateSkillState state, KeyCode wrongKey)
        {
            ShowSkillFailureFeedback();
        }

        private void HandleStateSkillTimedOut(StateSkillState state, int completedInputs)
        {
            ShowSkillTimeoutFeedback();
        }

        private void HandleSkillCooldownStarted(float cooldownDuration)
        {
            SetSkillState(StateSkillState.Cooldown);
            SetSkillCooldown(cooldownDuration, cooldownDuration);
        }

        private void HandleSkillCooldownEnded()
        {
            SetSkillCooldown(0f, 1f);
            SetSkillState(StateSkillState.Normal);
        }

        private void HandleSequenceStarted(IReadOnlyList<KeyCode> sequence)
        {
            SetExpectedSequence(sequence);
            _completedSequenceInputs = 0;
            _sequenceRemaining = inputSequenceDetector != null ? inputSequenceDetector.ActiveWindowSeconds : defaultSequenceWindow;
            _sequenceWindow = _sequenceRemaining;
            UpdateSequenceVisuals();
        }

        private void HandleSequenceProgress(IReadOnlyList<KeyCode> sequence, int completedInputs, float remainingTime)
        {
            SetExpectedSequence(sequence);
            SetSkillSequenceProgress(completedInputs, sequence.Count, remainingTime, inputSequenceDetector != null ? inputSequenceDetector.ActiveWindowSeconds : defaultSequenceWindow);
        }

        private void HandleSequenceFailed(IReadOnlyList<KeyCode> sequence, KeyCode wrongKey, int failedIndex)
        {
            SetExpectedSequence(sequence);
            ShowSkillFailureFeedback();
            ResetSequenceProgress();
        }

        private void HandleSequenceTimedOut(IReadOnlyList<KeyCode> sequence, int completedInputs)
        {
            SetExpectedSequence(sequence);
            ShowSkillTimeoutFeedback();
        }

        private void HandleSequenceCompleted(IReadOnlyList<KeyCode> sequence)
        {
            SetSkillSequenceProgress(sequence.Count, sequence.Count, 0f, defaultSequenceWindow);
        }

        private void EnsureHudCanvas()
        {
            if (!createHudAtRuntime && hudCanvas == null)
                return;

            if (hudCanvas == null)
            {
                GameObject canvasObject = new GameObject("CombatHUDCanvas");
                canvasObject.transform.SetParent(transform, false);
                hudCanvas = canvasObject.AddComponent<Canvas>();
                hudCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                hudCanvas.sortingOrder = hudSortingOrder;
                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            CreateHudIfNeeded();
        }

        private void CreateHudIfNeeded()
        {
            if (hudCanvas == null || playerHealthFill != null)
                return;

            RectTransform root = hudCanvas.GetComponent<RectTransform>();
            CreateTopHud(root);
            CreateStateHud(root);
            CreateComboHud(root);
        }

        private void CreateTopHud(RectTransform root)
        {
            RectTransform playerPanel = CreatePanel("PlayerHUD", root, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(28f, -28f), new Vector2(440f, 118f), PanelDark);
            playerHudRoot = playerPanel;
            CreateText("VOID SORCERER", playerPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(250f, 26f), 18, TextAnchor.MiddleLeft, PlayerBlue);
            BarView playerHp = CreateBar("PlayerHP", playerPanel, new Vector2(18f, -50f), new Vector2(365f, 22f), PlayerBlue, new Color(0.12f, 0.02f, 0.18f, 1f), false);
            playerHealthFill = playerHp.fill;
            playerHealthDelayFill = playerHp.delayedFill;
            BarView energy = CreateBar("CursedEnergy", playerPanel, new Vector2(18f, -84f), new Vector2(285f, 14f), PlayerPurple, new Color(0.03f, 0.02f, 0.12f, 1f), false);
            energyFill = energy.fill;
            _energyGlow = CreateImage("EnergyReadyGlow", energy.fill.rectTransform.parent, new Color(PlayerPurple.r, PlayerPurple.g, PlayerPurple.b, 0f));
            CopyRect(energy.fill.rectTransform, _energyGlow.rectTransform);
            CreateText("CURSED ENERGY", playerPanel, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(318f, -76f), new Vector2(110f, 22f), 11, TextAnchor.MiddleLeft, MutedText);

            RectTransform enemyPanel = CreatePanel("EnemyHUD", root, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -28f), new Vector2(440f, 92f), PanelDark);
            enemyHudRoot = enemyPanel;
            CreateText("CRIMSON CURSE", enemyPanel, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -14f), new Vector2(260f, 26f), 18, TextAnchor.MiddleRight, EnemyRed);
            BarView enemyHp = CreateBar("EnemyHP", enemyPanel, new Vector2(-383f, -52f), new Vector2(365f, 22f), EnemyRed, EnemyDark, true);
            enemyHealthFill = enemyHp.fill;
            enemyHealthDelayFill = enemyHp.delayedFill;

            timerText = CreateText("99", root, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -38f), new Vector2(120f, 42f), 30, TextAnchor.MiddleCenter, Color.white);
            AddShadow(timerText.gameObject, new Color(0f, 0f, 0f, 0.7f), new Vector2(2f, -2f));
        }

        private void CreateStateHud(RectTransform root)
        {
            _statePanel = CreatePanel("StateSkillHUD", root, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 44f), new Vector2(560f, 148f), new Color(0.025f, 0.025f, 0.04f, 0.88f));
            _statePanelBackground = _statePanel.GetComponent<Image>();

            stateText = CreateText("NORMAL", _statePanel, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -22f), new Vector2(360f, 34f), 24, TextAnchor.MiddleCenter, Color.white);
            AddShadow(stateText.gameObject, PlayerPurple, new Vector2(0f, 0f));

            inputSequenceRoot = CreatePanel("InputSequence", _statePanel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 6f), new Vector2(260f, 48f), new Color(0f, 0f, 0f, 0f));

            for (int i = 0; i < 3; i++)
            {
                RectTransform keyBox = CreatePanel("KeyBox" + i, inputSequenceRoot, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(28f + i * 82f, 0f), new Vector2(54f, 42f), new Color(0.08f, 0.09f, 0.14f, 0.95f));
                Image image = keyBox.GetComponent<Image>();
                Text label = CreateText("-", keyBox, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 22, TextAnchor.MiddleCenter, Color.white);
                _sequenceBoxes.Add(image);
                _sequenceLabels.Add(label);
            }

            RectTransform timerBack = CreatePanel("SequenceTimerBack", _statePanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 22f), new Vector2(310f, 8f), new Color(0.1f, 0.1f, 0.14f, 0.8f));
            sequenceTimerFill = CreateImage("SequenceTimerFill", timerBack, PlayerBlue);
            Stretch(sequenceTimerFill.rectTransform);
            sequenceTimerFill.type = Image.Type.Filled;
            sequenceTimerFill.fillMethod = Image.FillMethod.Horizontal;
            sequenceTimerText = CreateText("", _statePanel, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-66f, 20f), new Vector2(54f, 18f), 10, TextAnchor.MiddleRight, MutedText);

            _cooldownBackplate = CreateImage("CooldownBackplate", _statePanel, new Color(0f, 0f, 0f, 0.3f));
            RectTransform cooldownRect = _cooldownBackplate.rectTransform;
            cooldownRect.anchorMin = new Vector2(0f, 0f);
            cooldownRect.anchorMax = new Vector2(1f, 1f);
            cooldownRect.offsetMin = Vector2.zero;
            cooldownRect.offsetMax = Vector2.zero;
            cooldownFill = CreateImage("CooldownFill", _statePanel, new Color(0.7f, 0.7f, 0.85f, 0.35f));
            Stretch(cooldownFill.rectTransform);
            cooldownFill.type = Image.Type.Filled;
            cooldownFill.fillMethod = Image.FillMethod.Horizontal;
            cooldownFill.fillAmount = 0f;
            cooldownText = CreateText("", _statePanel, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 3f), new Vector2(120f, 22f), 13, TextAnchor.MiddleCenter, MutedText);
        }

        private void CreateComboHud(RectTransform root)
        {
            _comboRoot = CreatePanel("ComboCounter", root, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(70f, 70f), new Vector2(170f, 70f), new Color(0f, 0f, 0f, 0f));
            comboText = CreateText("", _comboRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 34, TextAnchor.MiddleLeft, Color.white);
            AddShadow(comboText.gameObject, PlayerPurple, new Vector2(2f, -2f));
            SetCanvasGroup(_comboRoot.gameObject, 0f);
        }

        private BarView CreateBar(string name, RectTransform parent, Vector2 anchoredPosition, Vector2 size, Color fillColor, Color backColor, bool rightToLeft)
        {
            RectTransform back = CreatePanel(name + "Back", parent, new Vector2(rightToLeft ? 1f : 0f, 1f), new Vector2(rightToLeft ? 1f : 0f, 1f), anchoredPosition, size, backColor);

            Image delay = CreateImage(name + "Delay", back, new Color(1f, 1f, 1f, 0.35f));
            Stretch(delay.rectTransform);
            delay.type = Image.Type.Filled;
            delay.fillMethod = Image.FillMethod.Horizontal;
            delay.fillOrigin = rightToLeft ? 1 : 0;
            delay.fillAmount = 1f;

            Image fill = CreateImage(name + "Fill", back, fillColor);
            Stretch(fill.rectTransform);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = rightToLeft ? 1 : 0;
            fill.fillAmount = 1f;

            return new BarView { fill = fill, delayedFill = delay, displayed = 1f, delayed = 1f, target = 1f };
        }

        private void BindBarViews()
        {
            _playerHealthBar.fill = playerHealthFill;
            _playerHealthBar.delayedFill = playerHealthDelayFill;
            _enemyHealthBar.fill = enemyHealthFill;
            _enemyHealthBar.delayedFill = enemyHealthDelayFill;
            _energyBar.fill = energyFill;
            _energyBar.delayedFill = null;
        }

        private void TickBar(BarView bar)
        {
            if (bar == null || bar.fill == null)
                return;

            float dt = Time.unscaledDeltaTime;
            bar.displayed = Mathf.Lerp(bar.displayed, bar.target, 1f - Mathf.Exp(-barLerpSpeed * dt));
            bar.fill.fillAmount = bar.displayed;

            if (bar.delayedFill != null)
            {
                if (bar.target > bar.delayed)
                    bar.delayed = bar.target;
                else
                    bar.delayed = Mathf.Lerp(bar.delayed, bar.target, 1f - Mathf.Exp(-delayedBarLerpSpeed * dt));

                bar.delayedFill.fillAmount = bar.delayed;
            }
        }

        private void SetBarTarget(BarView bar, float normalised)
        {
            if (bar == null)
                return;

            bar.target = Mathf.Clamp01(normalised);
        }

        private void SetSkillState(StateSkillState state)
        {
            _currentSkillState = state;
            if (stateText != null)
                stateText.text = FormatState(state);

            ApplyStateColors(state);
            SetExpectedSequence(SequenceForState(state));
            ResetSequenceProgress();
            UpdateEnergyReadyPulse();

            if (state == StateSkillState.ReleaseReady)
                StartReleaseReadyPulse();
            else
            {
                StopOverlayRoutine(ref _releaseReadyPulseRoutine);
                if (_statePanel != null)
                    _statePanel.localScale = Vector3.one;
            }
        }

        private void ApplyStateColors(StateSkillState state)
        {
            Color stateColor = Color.white;
            Color backColor = PanelDark;

            switch (state)
            {
                case StateSkillState.Activation:
                    stateColor = PlayerBlue;
                    backColor = new Color(0.02f, 0.06f, 0.14f, 0.9f);
                    break;
                case StateSkillState.Charge:
                    stateColor = PlayerPurple;
                    backColor = new Color(0.06f, 0.025f, 0.14f, 0.92f);
                    break;
                case StateSkillState.ReleaseReady:
                    stateColor = new Color(0.9f, 0.55f, 1f, 1f);
                    backColor = new Color(0.11f, 0.02f, 0.18f, 0.94f);
                    break;
                case StateSkillState.Releasing:
                    stateColor = Color.white;
                    backColor = new Color(0.18f, 0.05f, 0.28f, 0.95f);
                    break;
                case StateSkillState.Cooldown:
                    stateColor = new Color(0.65f, 0.68f, 0.78f, 1f);
                    backColor = new Color(0.025f, 0.025f, 0.03f, 0.92f);
                    break;
            }

            if (stateText != null) stateText.color = stateColor;
            if (_statePanelBackground != null) _statePanelBackground.color = backColor;
            if (_cooldownBackplate != null) _cooldownBackplate.enabled = state == StateSkillState.Cooldown;
        }

        private void SetExpectedSequence(IReadOnlyList<KeyCode> sequence)
        {
            if (sequence == null || sequence.Count == 0)
                return;

            _currentSequence = new KeyCode[Mathf.Min(3, sequence.Count)];
            for (int i = 0; i < _currentSequence.Length; i++)
                _currentSequence[i] = sequence[i];

            UpdateSequenceVisuals();
        }

        private void ResetSequenceProgress()
        {
            _completedSequenceInputs = 0;
            _sequenceRemaining = 0f;
            _sequenceWindow = defaultSequenceWindow;
            UpdateSequenceVisuals();
        }

        private void UpdateSequenceVisuals()
        {
            for (int i = 0; i < _sequenceBoxes.Count; i++)
            {
                bool hasKey = _currentSequence != null && i < _currentSequence.Length && _currentSequence[i] != KeyCode.None;
                if (_sequenceLabels[i] != null)
                    _sequenceLabels[i].text = hasKey ? _currentSequence[i].ToString().ToUpperInvariant() : "-";

                if (_sequenceBoxes[i] == null)
                    continue;

                if (!hasKey || _currentSkillState == StateSkillState.Cooldown || _currentSkillState == StateSkillState.Releasing)
                    _sequenceBoxes[i].color = new Color(0.08f, 0.08f, 0.1f, 0.65f);
                else if (i < _completedSequenceInputs)
                    _sequenceBoxes[i].color = Color.Lerp(PlayerBlue, PlayerPurple, 0.45f);
                else if (i == _completedSequenceInputs)
                    _sequenceBoxes[i].color = new Color(0.16f, 0.18f, 0.28f, 1f);
                else
                    _sequenceBoxes[i].color = new Color(0.08f, 0.09f, 0.14f, 0.95f);
            }

            if (sequenceTimerFill != null)
            {
                float fill = _sequenceWindow > 0f && _sequenceRemaining > 0f
                    ? Mathf.Clamp01(_sequenceRemaining / _sequenceWindow)
                    : 0f;
                sequenceTimerFill.fillAmount = fill;
            }

            if (sequenceTimerText != null)
                sequenceTimerText.text = _sequenceRemaining > 0f ? $"{_sequenceRemaining:0.0}s" : "";
        }

        private void TickSequenceTimer()
        {
            if (_sequenceRemaining <= 0f)
                return;

            _sequenceRemaining = Mathf.Max(0f, _sequenceRemaining - Time.deltaTime);
            UpdateSequenceVisuals();
        }

        private void ShowCombo(int combo)
        {
            if (comboText == null || _comboRoot == null)
                return;

            comboText.text = $"{combo} HIT";
            SetCanvasGroup(_comboRoot.gameObject, 1f);

            StopOverlayRoutine(ref _comboRoutine);
            _comboRoutine = StartCoroutine(ComboFadeRoutine());

            StopOverlayRoutine(ref _comboPopRoutine);
            _comboPopRoutine = StartCoroutine(PopRectRoutine(_comboRoot, 1.18f, 0.12f));
        }

        private void HideCombo()
        {
            if (_comboRoot == null)
                return;

            StopOverlayRoutine(ref _comboRoutine);
            _comboRoutine = StartCoroutine(FadeCanvasGroupRoutine(_comboRoot.gameObject, 0f, 0.18f));
        }

        private IEnumerator ComboFadeRoutine()
        {
            yield return new WaitForSeconds(comboFadeDelay);
            yield return FadeCanvasGroupRoutine(_comboRoot.gameObject, 0f, 0.22f);
            _comboRoutine = null;
        }

        private void UpdateEnergyReadyPulse()
        {
            bool enough = _currentEnergy >= EnergyCostForState(_currentSkillState);
            if (!enough || _currentSkillState == StateSkillState.Cooldown || _currentSkillState == StateSkillState.Releasing)
            {
                StopOverlayRoutine(ref _energyPulseRoutine);
                if (_energyGlow != null)
                    _energyGlow.color = new Color(PlayerPurple.r, PlayerPurple.g, PlayerPurple.b, 0f);
                return;
            }

            if (_energyPulseRoutine == null && _energyGlow != null)
                _energyPulseRoutine = StartCoroutine(EnergyPulseRoutine());
        }

        private IEnumerator EnergyPulseRoutine()
        {
            while (_energyGlow != null)
            {
                float alpha = Mathf.Lerp(0.08f, 0.28f, (Mathf.Sin(Time.unscaledTime * 5.5f) + 1f) * 0.5f);
                _energyGlow.color = new Color(PlayerPurple.r, PlayerPurple.g, PlayerPurple.b, alpha);
                yield return null;
            }
        }

        private void StartReleaseReadyPulse()
        {
            if (_statePanel == null || _releaseReadyPulseRoutine != null)
                return;

            _releaseReadyPulseRoutine = StartCoroutine(ReleaseReadyPulseRoutine());
        }

        private IEnumerator ReleaseReadyPulseRoutine()
        {
            Vector3 baseScale = Vector3.one;
            while (_statePanel != null && _currentSkillState == StateSkillState.ReleaseReady)
            {
                float scale = Mathf.Lerp(1f, 1.035f, (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f);
                _statePanel.localScale = baseScale * scale;
                yield return null;
            }

            if (_statePanel != null)
                _statePanel.localScale = baseScale;
            _releaseReadyPulseRoutine = null;
        }

        private IEnumerator DarkenRoutine(float alpha, float duration)
        {
            Color color = new Color(0.02f, 0f, 0.08f, alpha);
            float fadeIn = Mathf.Min(0.08f, duration * 0.35f);
            float fadeOut = Mathf.Min(0.18f, duration * 0.45f);
            float hold = Mathf.Max(0f, duration - fadeIn - fadeOut);

            yield return FadeImage(darkenOverlay, Color.clear, color, fadeIn);
            if (hold > 0f)
                yield return new WaitForSecondsRealtime(hold);
            yield return FadeImage(darkenOverlay, color, Color.clear, fadeOut);

            _darkenRoutine = null;
        }

        private IEnumerator FlashRoutine(Color color, float peakAlpha, float duration)
        {
            color.a = peakAlpha;
            float attack = Mathf.Min(0.025f, duration * 0.3f);
            float release = Mathf.Max(0.02f, duration - attack);

            yield return FadeImage(flashOverlay, Color.clear, color, attack);
            yield return FadeImage(flashOverlay, color, Color.clear, release);

            _flashRoutine = null;
        }

        private IEnumerator ShakeRectRoutine(RectTransform rect, float duration, float distance)
        {
            Vector2 original = rect.anchoredPosition;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float fade = 1f - elapsed / duration;
                rect.anchoredPosition = original + Random.insideUnitCircle * distance * fade;
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            rect.anchoredPosition = original;
            _inputShakeRoutine = null;
        }

        private IEnumerator PopRectRoutine(RectTransform rect, float peakScale, float duration)
        {
            Vector3 original = Vector3.one;
            float half = Mathf.Max(0.01f, duration * 0.5f);
            yield return ScaleRect(rect, original, original * peakScale, half);
            yield return ScaleRect(rect, original * peakScale, original, half);
            _comboPopRoutine = null;
        }

        private IEnumerator ScaleRect(RectTransform rect, Vector3 from, Vector3 to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                rect.localScale = Vector3.Lerp(from, to, t);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            rect.localScale = to;
        }

        private IEnumerator FadeCanvasGroupRoutine(GameObject target, float targetAlpha, float duration)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
                group = target.AddComponent<CanvasGroup>();

            float startAlpha = group.alpha;
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                group.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            group.alpha = targetAlpha;
        }

        private IEnumerator FadeImage(Image image, Color from, Color to, float duration)
        {
            if (image == null)
                yield break;

            if (duration <= 0f)
            {
                image.color = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                image.color = Color.Lerp(from, to, t);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            image.color = to;
        }

        private void EnsureOverlayCanvas()
        {
            if (!createOverlayAtRuntime && feedbackCanvas == null)
                return;

            if (feedbackCanvas == null)
            {
                GameObject canvasObject = new GameObject("ScreenFeedbackCanvas");
                canvasObject.transform.SetParent(transform, false);
                feedbackCanvas = canvasObject.AddComponent<Canvas>();
                feedbackCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                feedbackCanvas.sortingOrder = feedbackSortingOrder;
                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
            }

            if (darkenOverlay == null)
                darkenOverlay = CreateOverlayImage("ReleaseDarkenOverlay", feedbackCanvas.transform);

            if (flashOverlay == null)
                flashOverlay = CreateOverlayImage("ImpactFlashOverlay", feedbackCanvas.transform);
        }

        private Image CreateOverlayImage(string objectName, Transform parent)
        {
            GameObject overlayObject = new GameObject(objectName);
            overlayObject.transform.SetParent(parent, false);

            RectTransform rect = overlayObject.AddComponent<RectTransform>();
            Stretch(rect);

            Image image = overlayObject.AddComponent<Image>();
            image.color = Color.clear;
            image.raycastTarget = false;
            return image;
        }

        private RectTransform CreatePanel(string name, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, Color color)
        {
            Image image = CreateImage(name, parent, color);
            RectTransform rect = image.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f, anchorMin.y == anchorMax.y ? anchorMin.y : 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return rect;
        }

        private Image CreateImage(string name, Transform parent, Color color)
        {
            GameObject imageObject = new GameObject(name);
            imageObject.transform.SetParent(parent, false);
            Image image = imageObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text CreateText(string text, RectTransform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 size, int fontSize, TextAnchor alignment, Color color)
        {
            GameObject textObject = new GameObject("Text");
            textObject.transform.SetParent(parent, false);
            Text label = textObject.AddComponent<Text>();
            label.text = text;
            label.font = _font;
            label.fontSize = fontSize;
            label.fontStyle = FontStyle.Bold;
            label.alignment = alignment;
            label.color = color;
            label.raycastTarget = false;
            label.resizeTextForBestFit = true;
            label.resizeTextMinSize = Mathf.Max(8, fontSize - 8);
            label.resizeTextMaxSize = fontSize;

            RectTransform rect = label.rectTransform;
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            if (size == Vector2.zero && anchorMin != anchorMax)
            {
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
            }
            else
            {
                rect.pivot = new Vector2(anchorMin.x == anchorMax.x ? anchorMin.x : 0.5f, anchorMin.y == anchorMax.y ? anchorMin.y : 0.5f);
                rect.anchoredPosition = anchoredPosition;
                rect.sizeDelta = size;
            }

            return label;
        }

        private void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void CopyRect(RectTransform source, RectTransform target)
        {
            target.anchorMin = source.anchorMin;
            target.anchorMax = source.anchorMax;
            target.pivot = source.pivot;
            target.anchoredPosition = source.anchoredPosition;
            target.sizeDelta = source.sizeDelta;
            target.offsetMin = source.offsetMin;
            target.offsetMax = source.offsetMax;
        }

        private void AddShadow(GameObject target, Color color, Vector2 distance)
        {
            Shadow shadow = target.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private void SetCanvasGroup(GameObject target, float alpha)
        {
            CanvasGroup group = target.GetComponent<CanvasGroup>();
            if (group == null)
                group = target.AddComponent<CanvasGroup>();
            group.alpha = alpha;
        }

        private void SetImageAlpha(Image image, float alpha)
        {
            if (image == null)
                return;

            Color color = image.color;
            color.a = alpha;
            image.color = color;
        }

        private void StopOverlayRoutine(ref Coroutine routine)
        {
            if (routine == null)
                return;

            StopCoroutine(routine);
            routine = null;
        }

        private Font ResolveFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont("Arial", 16);
            return font;
        }

        private KeyCode[] SequenceForState(StateSkillState state)
        {
            switch (state)
            {
                case StateSkillState.Activation:
                    return new[] { KeyCode.W, KeyCode.E, KeyCode.Q };
                case StateSkillState.Charge:
                    return new[] { KeyCode.E, KeyCode.Q, KeyCode.W };
                case StateSkillState.ReleaseReady:
                case StateSkillState.Releasing:
                case StateSkillState.Cooldown:
                    return new[] { KeyCode.None, KeyCode.None, KeyCode.None };
                default:
                    return new[] { KeyCode.Q, KeyCode.W, KeyCode.E };
            }
        }

        private float EnergyCostForState(StateSkillState state)
        {
            switch (state)
            {
                case StateSkillState.Normal:
                    return activationEnergyCost;
                case StateSkillState.Activation:
                    return chargeEnergyCost;
                case StateSkillState.Charge:
                    return releaseEnergyCost;
                default:
                    return float.PositiveInfinity;
            }
        }

        private string FormatState(StateSkillState state)
        {
            switch (state)
            {
                case StateSkillState.ReleaseReady:
                    return "RELEASE READY";
                default:
                    return state.ToString().ToUpperInvariant();
            }
        }

        private float Normalise(float current, float max)
        {
            return max > 0f ? Mathf.Clamp01(current / max) : 0f;
        }
    }
}
