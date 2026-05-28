using System;
using System.Collections.Generic;
using UnityEngine;

namespace AnimeFighter.Skills
{
    /// <summary>
    /// Reads a configured ordered key sequence with a single shared completion
    /// window. It reports only sequence progress/failure; skill-state decisions
    /// live in StateSkillController.
    /// </summary>
    public sealed class InputSequenceDetector : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private bool inputEnabled = true;
        [SerializeField] private KeyCode[] observedInputs = { KeyCode.Q, KeyCode.W, KeyCode.E };

        [Header("Window")]
        [Min(0.05f)]
        [SerializeField] private float stageWindowSeconds = 1.5f;

        [Header("Debug")]
        [SerializeField] private bool debugLogs;

        public event Action<IReadOnlyList<KeyCode>> OnSequenceStarted;
        public event Action<IReadOnlyList<KeyCode>, int, float> OnSequenceProgress;
        public event Action<IReadOnlyList<KeyCode>> OnSequenceCompleted;
        public event Action<IReadOnlyList<KeyCode>, KeyCode, int> OnSequenceFailed;
        public event Action<IReadOnlyList<KeyCode>, int> OnSequenceTimedOut;

        // Compatibility events from the foundation placeholder.
        public event Action<IReadOnlyList<KeyCode>> SequenceProgressed;
        public event Action<IReadOnlyList<KeyCode>> SequenceCompleted;
        public event Action SequenceBroken;

        private readonly List<KeyCode> _sequence = new List<KeyCode>(3);
        private float _activeWindowSeconds;

        public bool InputEnabled
        {
            get => inputEnabled;
            set => inputEnabled = value;
        }

        public float StageWindowSeconds => stageWindowSeconds;
        public float ActiveWindowSeconds => _activeWindowSeconds > 0f ? _activeWindowSeconds : stageWindowSeconds;
        public int CurrentInputIndex { get; private set; }
        public float RemainingTime { get; private set; }
        public bool HasActiveAttempt { get; private set; }
        public bool HasSequence => _sequence.Count > 0;
        public IReadOnlyList<KeyCode> CurrentSequence => _sequence;

        public KeyCode ExpectedKey
        {
            get
            {
                if (!HasSequence || CurrentInputIndex < 0 || CurrentInputIndex >= _sequence.Count)
                    return KeyCode.None;
                return _sequence[CurrentInputIndex];
            }
        }

        private void Awake()
        {
            _activeWindowSeconds = stageWindowSeconds;
        }

        private void Update()
        {
            TickTimeout();

            if (!inputEnabled || !HasSequence)
                return;

            if (TryGetPressedObservedKey(out KeyCode pressedKey))
                TryConsumeInput(pressedKey);
        }

        public void ConfigureSequence(IReadOnlyList<KeyCode> sequence)
        {
            ConfigureSequence(sequence, stageWindowSeconds);
        }

        public void ConfigureSequence(IReadOnlyList<KeyCode> sequence, float windowSeconds)
        {
            _sequence.Clear();
            if (sequence != null)
            {
                for (int i = 0; i < sequence.Count; i++)
                {
                    if (sequence[i] != KeyCode.None)
                        _sequence.Add(sequence[i]);
                }
            }

            _activeWindowSeconds = windowSeconds > 0f ? windowSeconds : stageWindowSeconds;
            ResetAttempt();
            DebugLog($"Configured sequence: {FormatSequence(_sequence)} in {_activeWindowSeconds:0.00}s");
        }

        public void ConfigureSequence(params KeyCode[] sequence)
        {
            ConfigureSequence(sequence, stageWindowSeconds);
        }

        public void ClearSequence()
        {
            ResetAttempt();
            _sequence.Clear();
        }

        public void ResetAttempt()
        {
            CurrentInputIndex = 0;
            RemainingTime = 0f;
            HasActiveAttempt = false;
        }

        public bool TryConsumeInput(KeyCode key)
        {
            if (!HasSequence || !IsObservedInput(key))
                return false;

            KeyCode expected = ExpectedKey;
            if (key != expected)
            {
                FailAttempt(key);
                return false;
            }

            if (!HasActiveAttempt)
                StartAttempt();

            CurrentInputIndex++;

            KeyCode[] snapshot = SnapshotSequence();
            int completedInputs = CurrentInputIndex;

            OnSequenceProgress?.Invoke(snapshot, completedInputs, RemainingTime);
            SequenceProgressed?.Invoke(snapshot);
            DebugLog($"Progress {completedInputs}/{snapshot.Length}: {key}");

            if (CurrentInputIndex >= _sequence.Count)
            {
                ResetAttempt();
                OnSequenceCompleted?.Invoke(snapshot);
                SequenceCompleted?.Invoke(snapshot);
                DebugLog($"Completed sequence: {FormatSequence(snapshot)}");
            }

            return true;
        }

        private void TickTimeout()
        {
            if (!HasActiveAttempt)
                return;

            RemainingTime -= Time.deltaTime;
            if (RemainingTime > 0f)
                return;

            KeyCode[] snapshot = SnapshotSequence();
            int completedInputs = CurrentInputIndex;
            ResetAttempt();

            OnSequenceTimedOut?.Invoke(snapshot, completedInputs);
            SequenceBroken?.Invoke();
            DebugLog($"Timed out after {completedInputs}/{snapshot.Length}: {FormatSequence(snapshot)}");
        }

        private void StartAttempt()
        {
            HasActiveAttempt = true;
            RemainingTime = ActiveWindowSeconds;

            KeyCode[] snapshot = SnapshotSequence();
            OnSequenceStarted?.Invoke(snapshot);
            DebugLog($"Started sequence: {FormatSequence(snapshot)}");
        }

        private void FailAttempt(KeyCode wrongKey)
        {
            KeyCode[] snapshot = SnapshotSequence();
            int failedIndex = CurrentInputIndex;
            ResetAttempt();

            OnSequenceFailed?.Invoke(snapshot, wrongKey, failedIndex);
            SequenceBroken?.Invoke();
            DebugLog($"Failed sequence at index {failedIndex} with {wrongKey}; expected {KeyAt(snapshot, failedIndex)}");
        }

        private bool TryGetPressedObservedKey(out KeyCode key)
        {
            if (observedInputs != null)
            {
                for (int i = 0; i < observedInputs.Length; i++)
                {
                    KeyCode candidate = observedInputs[i];
                    if (candidate != KeyCode.None && Input.GetKeyDown(candidate))
                    {
                        key = candidate;
                        return true;
                    }
                }
            }

            key = KeyCode.None;
            return false;
        }

        private bool IsObservedInput(KeyCode key)
        {
            if (key == KeyCode.None || observedInputs == null)
                return false;

            for (int i = 0; i < observedInputs.Length; i++)
            {
                if (observedInputs[i] == key)
                    return true;
            }

            return false;
        }

        private KeyCode[] SnapshotSequence()
        {
            KeyCode[] snapshot = new KeyCode[_sequence.Count];
            _sequence.CopyTo(snapshot);
            return snapshot;
        }

        private static KeyCode KeyAt(IReadOnlyList<KeyCode> sequence, int index)
        {
            if (sequence == null || index < 0 || index >= sequence.Count)
                return KeyCode.None;
            return sequence[index];
        }

        private static string FormatSequence(IReadOnlyList<KeyCode> sequence)
        {
            if (sequence == null || sequence.Count == 0)
                return "(none)";

            string result = sequence[0].ToString();
            for (int i = 1; i < sequence.Count; i++)
                result += " -> " + sequence[i];
            return result;
        }

        private void DebugLog(string message)
        {
            if (debugLogs)
                Debug.Log($"[InputSequenceDetector] {message}", this);
        }
    }
}
