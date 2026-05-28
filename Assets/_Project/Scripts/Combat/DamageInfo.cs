using UnityEngine;

namespace AnimeFighter.Combat
{
    /// <summary>
    /// All the data needed to resolve a single hit. Built by the attacker
    /// (typically <see cref="CombatController"/>) and consumed by the
    /// defender's <see cref="IDamageable"/> implementation.
    /// </summary>
    [System.Serializable]
    public struct DamageInfo
    {
        public float damageAmount;
        public Vector3 hitPoint;
        public Vector3 knockbackDirection;
        public float knockbackForce;
        public GameObject source;
        public bool canBeBlocked;
        public AttackType attackType;

        public DamageInfo(
            float damageAmount,
            Vector3 hitPoint,
            Vector3 knockbackDirection,
            float knockbackForce,
            GameObject source,
            bool canBeBlocked,
            AttackType attackType)
        {
            this.damageAmount = damageAmount;
            this.hitPoint = hitPoint;
            this.knockbackDirection = knockbackDirection;
            this.knockbackForce = knockbackForce;
            this.source = source;
            this.canBeBlocked = canBeBlocked;
            this.attackType = attackType;
        }
    }
}
