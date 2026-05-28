using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using AnimeFighter.Combat;
using AnimeFighter.Player;

namespace AnimeFighter.VFX
{
    /// <summary>
    /// Runtime-generated placeholder VFX for combat and State Skill feedback.
    /// The effects are intentionally simple, tunable, and safe to replace with
    /// production prefabs later.
    /// </summary>
    public sealed class VFXManager : MonoBehaviour
    {
        private enum PooledEffect
        {
            HitSpark,
            UltimateHitSpark,
            BlockSpark,
            EnergyGain,
            WrongInput,
            EnemyHit,
            ReleaseBurst
        }

        [Header("Pool Roots")]
        [SerializeField] private Transform vfxRoot;
        [SerializeField] private int prewarmCount = 4;

        [Header("Palette")]
        [SerializeField] private Color voidBlue = new Color(0.15f, 0.7f, 1f, 1f);
        [SerializeField] private Color voidPurple = new Color(0.65f, 0.2f, 1f, 1f);
        [SerializeField] private Color cursedRed = new Color(1f, 0.08f, 0.04f, 1f);
        [SerializeField] private Color cursedBlack = new Color(0.03f, 0f, 0.02f, 1f);
        [SerializeField] private Color blockCyan = new Color(0.55f, 0.95f, 1f, 1f);

        [Header("Dash Afterimage")]
        [SerializeField] private float dashAfterimageDuration = 0.18f;
        [SerializeField] private float dashAfterimageInterval = 0.045f;
        [SerializeField] private float dashGhostLifetime = 0.18f;
        [Range(0f, 1f)]
        [SerializeField] private float dashGhostAlpha = 0.38f;

        [Header("Slash Trail")]
        [SerializeField] private float slashTrailLifetime = 0.18f;
        [SerializeField] private float slashTrailWidth = 0.18f;
        [SerializeField] private float slashTrailReach = 1.35f;

        [Header("State Aura")]
        [SerializeField] private float activationAuraRadius = 0.9f;
        [SerializeField] private float chargeAuraRadius = 1.2f;
        [SerializeField] private float auraVerticalOffset = 0.1f;
        [SerializeField] private float auraPulseSpeed = 4f;

        [Header("Release Blast")]
        [SerializeField] private float releaseBlastRange = 7f;
        [SerializeField] private float releaseBeamWidth = 0.55f;
        [SerializeField] private float releaseBeamLifetime = 0.28f;

        private readonly Dictionary<PooledEffect, Queue<GameObject>> _pool = new Dictionary<PooledEffect, Queue<GameObject>>();
        private readonly Dictionary<PooledEffect, GameObject> _templates = new Dictionary<PooledEffect, GameObject>();
        private Material _particleMaterial;
        private Material _trailMaterial;
        private Material _ghostMaterial;
        private Material _ringMaterial;
        private GameObject _stateAura;
        private Coroutine _auraPulseRoutine;

        private void Awake()
        {
            if (vfxRoot == null) vfxRoot = transform;
            CreateRuntimeMaterials();
            CreateRuntimeTemplates();
            PrewarmPool();
        }

        private void OnDisable()
        {
            StopStateAura();
        }

        /// <summary>Spawn a one-shot prefab at a position. Returns the instance.</summary>
        public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return null;
            return Instantiate(prefab, position, rotation, vfxRoot);
        }

        /// <summary>Spawn and auto-destroy after a lifetime.</summary>
        public GameObject SpawnTimed(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
        {
            GameObject instance = Spawn(prefab, position, rotation);
            if (instance != null && lifetime > 0f) Destroy(instance, lifetime);
            return instance;
        }

        public void PlayHitImpact(Vector3 position, AttackType attackType)
        {
            bool ultimate = attackType == AttackType.Ultimate;
            Color color = ultimate ? voidPurple : Color.Lerp(voidBlue, Color.white, 0.35f);
            float scale = ultimate ? 1.65f : 1f;
            PlayPooledEffect(ultimate ? PooledEffect.UltimateHitSpark : PooledEffect.HitSpark, position, Quaternion.identity, color, scale);
        }

        public void PlayBlockImpact(Vector3 position)
        {
            PlayPooledEffect(PooledEffect.BlockSpark, position, Quaternion.identity, blockCyan, 1f);
        }

        public void PlayDashAfterimage(Transform visualRoot)
        {
            if (visualRoot == null)
                return;

            StartCoroutine(DashAfterimageRoutine(
                visualRoot,
                new Color(voidPurple.r, voidPurple.g, voidPurple.b, dashGhostAlpha),
                new Color(voidBlue.r, voidBlue.g, voidBlue.b, 0f)));
        }

        public void PlayEnemyDashAfterimage(Transform visualRoot)
        {
            if (visualRoot == null)
                return;

            StartCoroutine(DashAfterimageRoutine(
                visualRoot,
                new Color(cursedRed.r, cursedRed.g, cursedRed.b, dashGhostAlpha),
                new Color(cursedBlack.r, cursedBlack.g, cursedBlack.b, 0f)));
        }

        public void PlaySlashTrail(Transform attackOrigin)
        {
            if (attackOrigin == null)
                return;

            PlayerController player = attackOrigin.GetComponentInParent<PlayerController>();
            int facing = player != null ? player.FacingDirection : 1;

            GameObject trailObject = new GameObject("SlashTrail");
            trailObject.transform.SetParent(vfxRoot, true);
            TrailRenderer trail = trailObject.AddComponent<TrailRenderer>();
            trail.time = slashTrailLifetime;
            trail.widthMultiplier = slashTrailWidth;
            trail.material = _trailMaterial;
            trail.numCornerVertices = 6;
            trail.numCapVertices = 6;
            trail.startColor = new Color(voidBlue.r, voidBlue.g, voidBlue.b, 0.92f);
            trail.endColor = new Color(voidPurple.r, voidPurple.g, voidPurple.b, 0f);
            trail.widthCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

            StartCoroutine(SlashTrailRoutine(trailObject.transform, attackOrigin, facing));
        }

        public void PlayActivationAura(Transform target)
        {
            CreateStateAura(target, activationAuraRadius, 24f, false);
        }

        public void PlayChargeAura(Transform target)
        {
            CreateStateAura(target, chargeAuraRadius, 52f, true);
        }

        public void PlayReleaseBlast(Vector3 origin, Vector3 direction)
        {
            Vector3 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.right;
            PlayPooledEffect(PooledEffect.ReleaseBurst, origin, Quaternion.LookRotation(Vector3.forward, safeDirection), voidPurple, 1.8f);
            CreateReleaseBeam(origin, safeDirection);
        }

        public void PlayWrongInputFlicker(Transform target)
        {
            if (target == null)
                return;

            Vector3 position = target.position + Vector3.up * 0.8f;
            PlayPooledEffect(PooledEffect.WrongInput, position, Quaternion.identity, new Color(1f, 0f, 0.85f, 1f), 1f);

            if (_stateAura != null)
                StartCoroutine(FlickerObjectRoutine(_stateAura, 0.22f, 4));
        }

        public void PlayEnergyGain(Transform target)
        {
            if (target == null)
                return;

            PlayPooledEffect(PooledEffect.EnergyGain, target.position + Vector3.up * 1.1f, Quaternion.identity, voidBlue, 1f);
        }

        public void PlayEnemyHitReaction(Vector3 position)
        {
            PlayPooledEffect(PooledEffect.EnemyHit, position, Quaternion.identity, cursedRed, 1f);
        }

        public void PlayCrimsonSlash(Vector3 origin, Vector3 direction)
        {
            Vector3 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.left;
            PlayPooledEffect(PooledEffect.EnemyHit, origin + safeDirection * 0.7f, Quaternion.identity, cursedRed, 1.15f);
            CreateCrimsonSlashArc(origin, safeDirection);
        }

        public void StopStateAura()
        {
            if (_auraPulseRoutine != null)
            {
                StopCoroutine(_auraPulseRoutine);
                _auraPulseRoutine = null;
            }

            if (_stateAura != null)
            {
                Destroy(_stateAura);
                _stateAura = null;
            }
        }

        private void CreateRuntimeMaterials()
        {
            _particleMaterial = CreateMaterial("Particles/Standard Unlit", Color.white);
            _trailMaterial = CreateMaterial("Sprites/Default", Color.white);
            _ghostMaterial = CreateMaterial("Sprites/Default", new Color(voidPurple.r, voidPurple.g, voidPurple.b, dashGhostAlpha));
            _ringMaterial = CreateMaterial("Sprites/Default", Color.white);
        }

        private Material CreateMaterial(string preferredShader, Color color)
        {
            Shader shader = Shader.Find(preferredShader);
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Standard");

            Material material = new Material(shader);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            else if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            return material;
        }

        private void CreateRuntimeTemplates()
        {
            _templates[PooledEffect.HitSpark] = CreateParticleTemplate(
                "HitSparkTemplate", 18, 0.18f, 0.12f, 0.28f, 2.8f, 0.08f, ParticleSystemShapeType.Sphere);
            _templates[PooledEffect.UltimateHitSpark] = CreateParticleTemplate(
                "UltimateHitSparkTemplate", 46, 0.32f, 0.18f, 0.5f, 4.8f, 0.18f, ParticleSystemShapeType.Sphere);
            _templates[PooledEffect.BlockSpark] = CreateParticleTemplate(
                "BlockSparkTemplate", 24, 0.22f, 0.12f, 0.32f, 2.6f, 0.05f, ParticleSystemShapeType.Circle);
            _templates[PooledEffect.EnergyGain] = CreateParticleTemplate(
                "EnergyGainTemplate", 16, 0.55f, 0.35f, 0.75f, 1.25f, 0.22f, ParticleSystemShapeType.Sphere);
            _templates[PooledEffect.WrongInput] = CreateParticleTemplate(
                "WrongInputTemplate", 20, 0.22f, 0.08f, 0.22f, 2.2f, 0.25f, ParticleSystemShapeType.Sphere);
            _templates[PooledEffect.EnemyHit] = CreateParticleTemplate(
                "EnemyHitTemplate", 20, 0.26f, 0.12f, 0.34f, 2.1f, 0.18f, ParticleSystemShapeType.Sphere);
            _templates[PooledEffect.ReleaseBurst] = CreateParticleTemplate(
                "ReleaseBurstTemplate", 70, 0.45f, 0.25f, 0.7f, 5.8f, 0.35f, ParticleSystemShapeType.Sphere);
        }

        private GameObject CreateParticleTemplate(
            string objectName,
            int burstCount,
            float duration,
            float minLifetime,
            float maxLifetime,
            float startSpeed,
            float startSize,
            ParticleSystemShapeType shapeType)
        {
            GameObject template = new GameObject(objectName);
            template.transform.SetParent(vfxRoot, false);
            template.SetActive(false);

            ParticleSystem particles = template.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.duration = duration;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(minLifetime, maxLifetime);
            main.startSpeed = startSpeed;
            main.startSize = startSize;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.maxParticles = Mathf.Max(8, burstCount * 2);

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(burstCount, 1, short.MaxValue)) });

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = shapeType;
            shape.radius = shapeType == ParticleSystemShapeType.Circle ? 0.45f : 0.18f;

            ParticleSystem.ColorOverLifetimeModule colorOverLifetime = particles.colorOverLifetime;
            colorOverLifetime.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLifetime.color = gradient;

            ParticleSystemRenderer renderer = template.GetComponent<ParticleSystemRenderer>();
            renderer.material = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;

            return template;
        }

        private void PrewarmPool()
        {
            foreach (KeyValuePair<PooledEffect, GameObject> pair in _templates)
            {
                Queue<GameObject> queue = GetQueue(pair.Key);
                for (int i = 0; i < prewarmCount; i++)
                {
                    GameObject instance = Instantiate(pair.Value, vfxRoot);
                    instance.name = pair.Key + "_Pooled";
                    instance.SetActive(false);
                    queue.Enqueue(instance);
                }
            }
        }

        private void PlayPooledEffect(PooledEffect effect, Vector3 position, Quaternion rotation, Color color, float scale)
        {
            if (!_templates.ContainsKey(effect))
                return;

            GameObject instance = GetPooledInstance(effect);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, scale);
            instance.SetActive(true);

            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>();
            float maxLifetime = 0.35f;
            for (int i = 0; i < particles.Length; i++)
            {
                ParticleSystem ps = particles[i];
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ParticleSystem.MainModule main = ps.main;
                main.startColor = color;
                maxLifetime = Mathf.Max(maxLifetime, main.duration + main.startLifetime.constantMax);
                ps.Play(true);
            }

            StartCoroutine(ReturnToPoolAfter(effect, instance, maxLifetime + 0.05f));
        }

        private GameObject GetPooledInstance(PooledEffect effect)
        {
            Queue<GameObject> queue = GetQueue(effect);
            if (queue.Count > 0)
                return queue.Dequeue();

            GameObject instance = Instantiate(_templates[effect], vfxRoot);
            instance.name = effect + "_Pooled";
            return instance;
        }

        private Queue<GameObject> GetQueue(PooledEffect effect)
        {
            if (!_pool.TryGetValue(effect, out Queue<GameObject> queue))
            {
                queue = new Queue<GameObject>();
                _pool.Add(effect, queue);
            }

            return queue;
        }

        private IEnumerator ReturnToPoolAfter(PooledEffect effect, GameObject instance, float delay)
        {
            yield return new WaitForSeconds(delay);

            if (instance == null)
                yield break;

            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>();
            for (int i = 0; i < particles.Length; i++)
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            instance.SetActive(false);
            instance.transform.SetParent(vfxRoot, false);
            GetQueue(effect).Enqueue(instance);
        }

        private IEnumerator DashAfterimageRoutine(Transform visualRoot, Color startColor, Color endColor)
        {
            float elapsed = 0f;
            while (elapsed < dashAfterimageDuration && visualRoot != null)
            {
                SpawnDashGhost(visualRoot, startColor, endColor);
                yield return new WaitForSecondsRealtime(dashAfterimageInterval);
                elapsed += dashAfterimageInterval;
            }
        }

        private void SpawnDashGhost(Transform visualRoot, Color startColor, Color endColor)
        {
            MeshFilter[] meshFilters = visualRoot.GetComponentsInChildren<MeshFilter>();
            for (int i = 0; i < meshFilters.Length; i++)
            {
                MeshFilter sourceFilter = meshFilters[i];
                MeshRenderer sourceRenderer = sourceFilter.GetComponent<MeshRenderer>();
                if (sourceFilter.sharedMesh == null || sourceRenderer == null)
                    continue;

                GameObject ghost = new GameObject("DashAfterimage");
                ghost.transform.SetParent(vfxRoot, true);
                ghost.transform.SetPositionAndRotation(sourceFilter.transform.position, sourceFilter.transform.rotation);
                ghost.transform.localScale = sourceFilter.transform.lossyScale;

                MeshFilter filter = ghost.AddComponent<MeshFilter>();
                filter.sharedMesh = sourceFilter.sharedMesh;

                MeshRenderer renderer = ghost.AddComponent<MeshRenderer>();
                Material material = new Material(_ghostMaterial);
                renderer.material = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                StartCoroutine(FadeGhostRoutine(ghost, material, startColor, endColor));
            }
        }

        private IEnumerator FadeGhostRoutine(GameObject ghost, Material material, Color startColor, Color endColor)
        {
            float elapsed = 0f;
            while (elapsed < dashGhostLifetime && ghost != null)
            {
                float t = elapsed / dashGhostLifetime;
                Color color = Color.Lerp(startColor, endColor, t);

                if (material != null)
                    material.color = color;

                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (ghost != null)
                Destroy(ghost);
            if (material != null)
                Destroy(material);
        }

        private IEnumerator SlashTrailRoutine(Transform trailTransform, Transform attackOrigin, int facing)
        {
            Vector3 basePosition = attackOrigin.position;
            Vector3 start = basePosition + Vector3.right * facing * -0.35f + Vector3.up * 0.55f;
            Vector3 mid = basePosition + Vector3.right * facing * (slashTrailReach * 0.55f) + Vector3.up * 0.18f;
            Vector3 end = basePosition + Vector3.right * facing * slashTrailReach + Vector3.up * -0.25f;

            float travelDuration = Mathf.Max(0.03f, slashTrailLifetime * 0.6f);
            float elapsed = 0f;
            while (elapsed < travelDuration && trailTransform != null)
            {
                float t = elapsed / travelDuration;
                Vector3 a = Vector3.Lerp(start, mid, t);
                Vector3 b = Vector3.Lerp(mid, end, t);
                trailTransform.position = Vector3.Lerp(a, b, t);
                elapsed += Time.deltaTime;
                yield return null;
            }

            if (trailTransform != null)
            {
                trailTransform.position = end;
                Destroy(trailTransform.gameObject, slashTrailLifetime);
            }
        }

        private void CreateStateAura(Transform target, float radius, float particleRate, bool charged)
        {
            if (target == null)
                return;

            StopStateAura();

            _stateAura = new GameObject(charged ? "ChargeAura" : "ActivationAura");
            _stateAura.transform.SetParent(target, false);
            _stateAura.transform.localPosition = Vector3.up * auraVerticalOffset;
            _stateAura.transform.localRotation = Quaternion.identity;

            AddLoopingAuraParticles(_stateAura, radius, particleRate, charged);
            CreateAuraRing(_stateAura.transform, radius, 0.15f, voidBlue, charged ? 0.06f : 0.035f);
            CreateAuraRing(_stateAura.transform, radius * 0.72f, 0.85f, voidPurple, charged ? 0.045f : 0.028f);

            _auraPulseRoutine = StartCoroutine(PulseAuraRoutine(_stateAura.transform, charged ? 0.9f : 0.96f, charged ? 1.1f : 1.04f));
        }

        private void AddLoopingAuraParticles(GameObject auraObject, float radius, float rate, bool charged)
        {
            ParticleSystem particles = auraObject.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particles.main;
            main.duration = 1.2f;
            main.loop = true;
            main.startLifetime = charged
                ? new ParticleSystem.MinMaxCurve(0.45f, 0.9f)
                : new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
            main.startSpeed = charged ? 0.85f : 0.45f;
            main.startSize = charged
                ? new ParticleSystem.MinMaxCurve(0.035f, 0.085f)
                : new ParticleSystem.MinMaxCurve(0.025f, 0.055f);
            main.startColor = charged ? voidPurple : voidBlue;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = rate;

            ParticleSystem.ShapeModule shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;

            ParticleSystemRenderer renderer = auraObject.GetComponent<ParticleSystemRenderer>();
            renderer.material = _particleMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
        }

        private void CreateAuraRing(Transform parent, float radius, float yOffset, Color color, float width)
        {
            GameObject ringObject = new GameObject("AuraRing");
            ringObject.transform.SetParent(parent, false);
            ringObject.transform.localPosition = Vector3.up * yOffset;

            LineRenderer ring = ringObject.AddComponent<LineRenderer>();
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.positionCount = 96;
            ring.widthMultiplier = width;
            ring.material = _ringMaterial;
            ring.startColor = new Color(color.r, color.g, color.b, 0.75f);
            ring.endColor = new Color(color.r, color.g, color.b, 0.15f);

            for (int i = 0; i < ring.positionCount; i++)
            {
                float angle = (i / (float)ring.positionCount) * Mathf.PI * 2f;
                ring.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius * 0.28f, 0f));
            }
        }

        private IEnumerator PulseAuraRoutine(Transform aura, float minScale, float maxScale)
        {
            while (aura != null)
            {
                float t = (Mathf.Sin(Time.time * auraPulseSpeed) + 1f) * 0.5f;
                float scale = Mathf.Lerp(minScale, maxScale, t);
                aura.localScale = Vector3.one * scale;
                yield return null;
            }
        }

        private void CreateReleaseBeam(Vector3 origin, Vector3 direction)
        {
            GameObject beamObject = new GameObject("ReleaseBeam");
            beamObject.transform.SetParent(vfxRoot, true);

            LineRenderer beam = beamObject.AddComponent<LineRenderer>();
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.material = _trailMaterial;
            beam.numCapVertices = 8;
            beam.widthMultiplier = releaseBeamWidth;
            beam.startColor = new Color(voidBlue.r, voidBlue.g, voidBlue.b, 0.95f);
            beam.endColor = new Color(voidPurple.r, voidPurple.g, voidPurple.b, 0f);
            beam.SetPosition(0, origin);
            beam.SetPosition(1, origin + direction.normalized * releaseBlastRange);

            StartCoroutine(FadeLineRoutine(beamObject, beam, releaseBeamLifetime));
        }

        private void CreateCrimsonSlashArc(Vector3 origin, Vector3 direction)
        {
            Vector3 safeDirection = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.left;
            GameObject slashObject = new GameObject("CrimsonSlashArc");
            slashObject.transform.SetParent(vfxRoot, true);

            LineRenderer slash = slashObject.AddComponent<LineRenderer>();
            slash.useWorldSpace = true;
            slash.positionCount = 7;
            slash.material = _trailMaterial;
            slash.numCapVertices = 8;
            slash.numCornerVertices = 8;
            slash.widthMultiplier = 0.22f;
            slash.startColor = new Color(cursedRed.r, cursedRed.g, cursedRed.b, 0.95f);
            slash.endColor = new Color(cursedBlack.r, cursedBlack.g, cursedBlack.b, 0f);

            Vector3 right = safeDirection;
            Vector3 up = Vector3.up;
            for (int i = 0; i < slash.positionCount; i++)
            {
                float t = i / (float)(slash.positionCount - 1);
                float x = Mathf.Lerp(0.15f, 3.9f, t);
                float arc = Mathf.Sin(t * Mathf.PI) * 0.75f;
                Vector3 point = origin + right * x + up * (0.2f + arc);
                point.z = origin.z;
                slash.SetPosition(i, point);
            }

            StartCoroutine(FadeLineRoutine(slashObject, slash, 0.32f));
        }

        private IEnumerator FadeLineRoutine(GameObject lineObject, LineRenderer line, float lifetime)
        {
            float elapsed = 0f;
            Color start = line.startColor;
            Color end = line.endColor;
            float startWidth = line.widthMultiplier;
            while (elapsed < lifetime && line != null)
            {
                float fade = 1f - (elapsed / lifetime);
                start.a = 0.95f * fade;
                end.a = 0f;
                line.startColor = start;
                line.endColor = end;
                line.widthMultiplier = startWidth * Mathf.Lerp(1f, 0.25f, elapsed / lifetime);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            if (lineObject != null)
                Destroy(lineObject);
        }

        private IEnumerator FlickerObjectRoutine(GameObject target, float duration, int pulses)
        {
            float step = duration / Mathf.Max(1, pulses * 2);
            for (int i = 0; i < pulses && target != null; i++)
            {
                target.SetActive(false);
                yield return new WaitForSecondsRealtime(step);
                if (target != null)
                    target.SetActive(true);
                yield return new WaitForSecondsRealtime(step);
            }
        }
    }
}
