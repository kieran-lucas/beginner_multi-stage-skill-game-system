using UnityEngine;

namespace AnimeFighter.Combat
{
    /// <summary>Anything that can be pushed by a directional impulse.</summary>
    public interface IKnockbackReceiver
    {
        void ApplyKnockback(Vector3 direction, float force);
    }
}
