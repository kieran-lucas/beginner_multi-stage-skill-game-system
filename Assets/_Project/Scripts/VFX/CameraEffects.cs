using System.Collections;
using UnityEngine;
using AnimeFighter.Core;

namespace AnimeFighter.VFX
{
    /// <summary>
    /// Centralised camera feel layer. It owns transient offsets, zoom pulses,
    /// hit-stop, slow motion, and optional two-character arena framing.
    /// </summary>
    public sealed class CameraEffects : MonoBehaviour
    {
        [Header("Camera Reference")]
        [SerializeField] private Camera targetCamera;

        [Header("Optional Framing")]
        [SerializeField] private bool framePlayerAndEnemy = true;
        [SerializeField] private Transform playerTarget;
        [SerializeField] private Transform enemyTarget;
        [SerializeField] private Vector3 framingOffset = new Vector3(0f, 1.4f, -10f);
        [SerializeField] private float followSmoothTime = 0.08f;
        [SerializeField] private float minFramingFov = 34f;
        [SerializeField] private float maxFramingFov = 46f;
        [SerializeField] private float framingFovPerUnit = 1.35f;

        [Header("Shake Defaults")]
        [SerializeField] private float defaultShakeIntensity = 0.1f;
        [SerializeField] private float defaultShakeDuration = 0.15f;
        [SerializeField] private float shakeFrequency = 55f;

        [Header("Zoom Defaults")]
        [SerializeField] private float zoomReturnDuration = 0.12f;

        [Header("Release Cinematic")]
        [SerializeField] private float releaseCinematicTimeScale = 0.35f;
        [SerializeField] private float releaseCinematicFov = 30f;
        [SerializeField] private float releaseWindupShakeIntensity = 0.035f;
        [SerializeField] private float releaseImpactShakeIntensity = 0.28f;
        [SerializeField] private float releaseImpactPunchIntensity = 0.18f;

        private Vector3 _restWorldPosition;
        private Vector3 _followVelocity;
        private Vector3 _shakeOffset;
        private Vector3 _punchOffset;
        private float _restFov;
        private float _restOrthographicSize;
        private float _defaultFixedDeltaTime;
        private Coroutine _shakeRoutine;
        private Coroutine _zoomRoutine;
        private Coroutine _punchRoutine;
        private Coroutine _timeScaleRoutine;
        private Coroutine _releaseRoutine;

        private void Awake()
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera != null)
            {
                _restWorldPosition = targetCamera.transform.position;
                _restFov = targetCamera.fieldOfView;
                _restOrthographicSize = targetCamera.orthographicSize;
            }

            _defaultFixedDeltaTime = Time.fixedDeltaTime;
        }

        private void Start()
        {
            ResolveTargets();
        }

        private void LateUpdate()
        {
            if (targetCamera == null)
                return;

            Vector3 basePosition = GetBaseCameraPosition();
            targetCamera.transform.position = basePosition + _shakeOffset + _punchOffset;

            if (framePlayerAndEnemy && playerTarget != null && enemyTarget != null)
                ApplyFramingFov();
        }

        private void OnDisable()
        {
            ResetEffects();
        }

        public void Shake() => Shake(defaultShakeIntensity, defaultShakeDuration);

        /// <summary>Shake with intensity first, duration second.</summary>
        public void Shake(float intensity, float duration)
        {
            if (targetCamera == null || intensity <= 0f || duration <= 0f)
                return;

            if (_shakeRoutine != null)
                StopCoroutine(_shakeRoutine);

            _shakeRoutine = StartCoroutine(ShakeRoutine(intensity, duration));
        }

        public void CameraPunch(float intensity, float duration)
        {
            if (targetCamera == null || intensity <= 0f || duration <= 0f)
                return;

            if (_punchRoutine != null)
                StopCoroutine(_punchRoutine);

            _punchRoutine = StartCoroutine(CameraPunchRoutine(intensity, duration));
        }

        public void ZoomPulse(float targetFovOrSize, float duration)
        {
            if (targetCamera == null || targetFovOrSize <= 0f || duration <= 0f)
                return;

            if (_zoomRoutine != null)
                StopCoroutine(_zoomRoutine);

            _zoomRoutine = StartCoroutine(ZoomPulseRoutine(targetFovOrSize, duration));
        }

        public void ZoomPunch(float targetFov, float duration) => ZoomPulse(targetFov, duration);

        public void HitStop(float duration)
        {
            if (duration <= 0f)
                return;

            StartTimeScaleRoutine(0f, duration);
        }

        public void HitPause(float seconds) => HitStop(seconds);

        public void SlowMotion(float timeScale, float duration)
        {
            if (duration <= 0f)
                return;

            StartTimeScaleRoutine(Mathf.Clamp(timeScale, 0.05f, 1f), duration);
        }

        public void ReleaseCinematic(float windupDuration, float impactDuration)
        {
            if (_releaseRoutine != null)
                StopCoroutine(_releaseRoutine);

            _releaseRoutine = StartCoroutine(ReleaseCinematicRoutine(
                Mathf.Max(0f, windupDuration),
                Mathf.Max(0.01f, impactDuration)));
        }

        public void ResetEffects()
        {
            StopRunningRoutine(ref _shakeRoutine);
            StopRunningRoutine(ref _zoomRoutine);
            StopRunningRoutine(ref _punchRoutine);
            StopRunningRoutine(ref _timeScaleRoutine);
            StopRunningRoutine(ref _releaseRoutine);

            _shakeOffset = Vector3.zero;
            _punchOffset = Vector3.zero;

            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime > 0f ? _defaultFixedDeltaTime : Time.fixedDeltaTime;

            if (targetCamera == null)
                return;

            if (targetCamera.orthographic)
                targetCamera.orthographicSize = _restOrthographicSize;
            else
                targetCamera.fieldOfView = _restFov;

            targetCamera.transform.position = GetBaseCameraPosition();
        }

        private void ResolveTargets()
        {
            GameManager gameManager = GameManager.Instance;
            if (gameManager == null)
                return;

            if (playerTarget == null) playerTarget = gameManager.Player;
            if (enemyTarget == null) enemyTarget = gameManager.Enemy;
        }

        private Vector3 GetBaseCameraPosition()
        {
            if (!framePlayerAndEnemy || playerTarget == null || enemyTarget == null)
                return _restWorldPosition;

            Vector3 midpoint = (playerTarget.position + enemyTarget.position) * 0.5f;
            midpoint.z = 0f;
            Vector3 desired = midpoint + framingOffset;

            if (followSmoothTime <= 0f)
                return desired;

            return Vector3.SmoothDamp(
                targetCamera.transform.position - _shakeOffset - _punchOffset,
                desired,
                ref _followVelocity,
                followSmoothTime,
                Mathf.Infinity,
                Time.unscaledDeltaTime);
        }

        private void ApplyFramingFov()
        {
            if (targetCamera.orthographic)
                return;

            float distance = Mathf.Abs(playerTarget.position.x - enemyTarget.position.x);
            float desiredFov = Mathf.Clamp(
                _restFov + distance * framingFovPerUnit,
                minFramingFov,
                maxFramingFov);

            if (_zoomRoutine == null)
                targetCamera.fieldOfView = Mathf.Lerp(
                    targetCamera.fieldOfView,
                    desiredFov,
                    1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
        }

        private IEnumerator ShakeRoutine(float intensity, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float fade = 1f - (elapsed / duration);
                float angle = elapsed * shakeFrequency;
                float noiseX = Mathf.PerlinNoise(angle, 0.13f) * 2f - 1f;
                float noiseY = Mathf.PerlinNoise(0.37f, angle) * 2f - 1f;
                _shakeOffset = new Vector3(noiseX, noiseY, 0f) * intensity * fade;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _shakeOffset = Vector3.zero;
            _shakeRoutine = null;
        }

        private IEnumerator CameraPunchRoutine(float intensity, float duration)
        {
            float elapsed = 0f;
            Vector3 direction = -targetCamera.transform.forward;

            while (elapsed < duration)
            {
                float t = elapsed / duration;
                float envelope = Mathf.Sin(t * Mathf.PI);
                _punchOffset = direction * (intensity * envelope);

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            _punchOffset = Vector3.zero;
            _punchRoutine = null;
        }

        private IEnumerator ZoomPulseRoutine(float targetFovOrSize, float duration)
        {
            float start = targetCamera.orthographic ? targetCamera.orthographicSize : targetCamera.fieldOfView;
            float rest = targetCamera.orthographic ? _restOrthographicSize : _restFov;
            float inDuration = Mathf.Max(0.01f, duration * 0.35f);
            float outDuration = Mathf.Max(zoomReturnDuration, duration * 0.65f);

            yield return LerpZoom(start, targetFovOrSize, inDuration);
            yield return LerpZoom(targetFovOrSize, rest, outDuration);

            if (targetCamera.orthographic)
                targetCamera.orthographicSize = rest;
            else
                targetCamera.fieldOfView = rest;

            _zoomRoutine = null;
        }

        private IEnumerator LerpZoom(float from, float to, float duration)
        {
            float elapsed = 0f;
            while (elapsed < duration)
            {
                float t = Mathf.SmoothStep(0f, 1f, elapsed / duration);
                float value = Mathf.Lerp(from, to, t);
                if (targetCamera.orthographic)
                    targetCamera.orthographicSize = value;
                else
                    targetCamera.fieldOfView = value;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }
        }

        private void StartTimeScaleRoutine(float targetTimeScale, float duration)
        {
            if (_timeScaleRoutine != null)
                StopCoroutine(_timeScaleRoutine);

            _timeScaleRoutine = StartCoroutine(TimeScaleRoutine(targetTimeScale, duration));
        }

        private IEnumerator TimeScaleRoutine(float targetTimeScale, float duration)
        {
            Time.timeScale = targetTimeScale;
            Time.fixedDeltaTime = _defaultFixedDeltaTime * Mathf.Max(0.05f, targetTimeScale);

            yield return new WaitForSecondsRealtime(duration);

            Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
            _timeScaleRoutine = null;
        }

        private IEnumerator ReleaseCinematicRoutine(float windupDuration, float impactDuration)
        {
            SlowMotion(releaseCinematicTimeScale, windupDuration);
            ZoomPulse(releaseCinematicFov, windupDuration + impactDuration);

            if (windupDuration > 0f)
            {
                Shake(releaseWindupShakeIntensity, windupDuration);
                yield return new WaitForSecondsRealtime(windupDuration);
            }

            CameraPunch(releaseImpactPunchIntensity, impactDuration);
            Shake(releaseImpactShakeIntensity, impactDuration);
            _releaseRoutine = null;
        }

        private void StopRunningRoutine(ref Coroutine routine)
        {
            if (routine == null)
                return;

            StopCoroutine(routine);
            routine = null;
        }
    }
}
