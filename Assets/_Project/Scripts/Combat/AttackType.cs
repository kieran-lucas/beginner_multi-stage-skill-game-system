namespace AnimeFighter.Combat
{
    /// <summary>
    /// Coarse-grained attack category. Drives VFX selection, animation triggers,
    /// armour penetration, and (later) audio cue choice.
    /// </summary>
    public enum AttackType
    {
        Light,
        Heavy,
        Skill,
        Ultimate
    }
}
