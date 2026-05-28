using System;
using UnityEngine;

namespace AnimeFighter.Combat
{
    /// <summary>
    /// HP pool + knockback receiver. Implements <see cref="IDamageable"/> so any
    /// attacker can hit it without knowing the concrete type, and
    /// <see cref="IKnockbackReceiver"/> so the same component handles physics
    /// pushback. UI and FX subscribe to the events rather than polling.
    /// </summary>
    public sealed class HealthSystem : MonoBehaviour, IDamageable, IKnockbackReceiver
    {
        [Header("Pool")]
        [SerializeField] private float maxHealth = 100f;
        [SerializeField] private float currentHealth = 100f;

        [Header("Knockback")]
        [Tooltip("Rigidbody the impulse is applied to. Auto-resolved from this object if null.")]
        [SerializeField] private Rigidbody body;

        public event Action<float, float> OnHealthChanged; // (current, max)
        public event Action<DamageInfo> OnDamaged;
        public event Action OnDeath;

        public float Max => maxHealth;
        public float Current => currentHealth;
        public float Normalised => maxHealth > 0f ? currentHealth / maxHealth : 0f;
        public bool IsAlive => currentHealth > 0f;

        private void Awake()
        {
            if (body == null) body = GetComponent<Rigidbody>();
            // Snap any clamp-busting inspector value at boot.
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        // ============ IDamageable ============

        public void TakeDamage(DamageInfo info)
        {
            if (!IsAlive || info.damageAmount <= 0f) return;

            currentHealth = Mathf.Max(0f, currentHealth - info.damageAmount);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
            OnDamaged?.Invoke(info);

            // Damage and knockback travel together — pure push impulses should
            // call ApplyKnockback directly instead of going through TakeDamage.
            if (info.knockbackForce > 0f)
                ApplyKnockback(info.knockbackDirection, info.knockbackForce);

            if (!IsAlive) OnDeath?.Invoke();
        }

        public void Heal(float amount)
        {
            if (!IsAlive || amount <= 0f) return;
            currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
            OnHealthChanged?.Invoke(currentHealth, maxHealth);
        }

        // ============ IKnockbackReceiver ============

        public void ApplyKnockback(Vector3 direction, float force)
        {
            if (body == null || force <= 0f) return;
            Vector3 impulse = direction.sqrMagnitude > 0.0001f
                ? direction.normalized * force
                : Vector3.zero;
            impulse.z = 0f; // stay on the lane
            body.AddForce(impulse, ForceMode.Impulse);
        }

        // ============ Editor convenience ============

#if UNITY_EDITOR
        [ContextMenu("Debug/Apply 10 damage")]
        private void DebugApplyDamage()
        {
            TakeDamage(new DamageInfo(
                10f, transform.position, Vector3.right, 0f, null, true, AttackType.Light));
        }
#endif
    }
}
