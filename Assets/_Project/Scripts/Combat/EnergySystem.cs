using System;
using UnityEngine;

namespace AnimeFighter.Combat
{
    /// <summary>
    /// Cursed Energy pool. Regenerates passively, gains chunks on hit / block,
    /// and is spent by dashes and (later) State Skills.
    /// </summary>
    public sealed class EnergySystem : MonoBehaviour
    {
        [Header("Pool")]
        [SerializeField] private float maxEnergy = 100f;
        [SerializeField] private float currentEnergy = 0f;

        [Header("Regen")]
        [Tooltip("Passive Cursed Energy gained per second.")]
        [SerializeField] private float regenPerSecond = 4f;

        [Header("Combat Gain")]
        [SerializeField] private float gainOnHit = 8f;
        [SerializeField] private float gainOnBlock = 3f;
        [Tooltip("Hook for the future Perfect Dodge mechanic.")]
        [SerializeField] private float gainOnPerfectDodge = 15f;

        public event Action<float, float> OnEnergyChanged; // (current, max)

        public float Max => maxEnergy;
        public float Current => currentEnergy;
        public float Normalised => maxEnergy > 0f ? currentEnergy / maxEnergy : 0f;
        public float GainOnHit => gainOnHit;
        public float GainOnBlock => gainOnBlock;
        public float GainOnPerfectDodge => gainOnPerfectDodge;

        private void Awake()
        {
            currentEnergy = Mathf.Clamp(currentEnergy, 0f, maxEnergy);
        }

        // ============ Spend / gain ============

        public bool HasEnoughEnergy(float amount) => amount <= 0f || currentEnergy >= amount;

        /// <summary>Spend <paramref name="amount"/>. Returns false (and changes nothing) if insufficient.</summary>
        public bool SpendEnergy(float amount)
        {
            if (amount <= 0f) return true;
            if (currentEnergy < amount) return false;
            currentEnergy -= amount;
            OnEnergyChanged?.Invoke(currentEnergy, maxEnergy);
            return true;
        }

        public void AddEnergy(float amount)
        {
            if (amount <= 0f) return;
            float prev = currentEnergy;
            currentEnergy = Mathf.Min(maxEnergy, currentEnergy + amount);
            if (currentEnergy != prev) OnEnergyChanged?.Invoke(currentEnergy, maxEnergy);
        }

        // ============ Combat hooks ============

        /// <summary>Call after a successful attack lands on a target.</summary>
        public void NotifyHitLanded() => AddEnergy(gainOnHit);

        /// <summary>Call when this character successfully blocks incoming damage.</summary>
        public void NotifyBlocked() => AddEnergy(gainOnBlock);

        /// <summary>Hook for the future Perfect Dodge mechanic.</summary>
        public void NotifyPerfectDodge() => AddEnergy(gainOnPerfectDodge);

        private void Update()
        {
            if (regenPerSecond > 0f && currentEnergy < maxEnergy)
                AddEnergy(regenPerSecond * Time.deltaTime);
        }
    }
}
