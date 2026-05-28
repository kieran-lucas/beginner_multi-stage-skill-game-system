namespace AnimeFighter.Combat
{
    /// <summary>Anything that can receive damage via a <see cref="DamageInfo"/>.</summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        void TakeDamage(DamageInfo damageInfo);
    }
}
