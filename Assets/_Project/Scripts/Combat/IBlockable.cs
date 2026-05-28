namespace AnimeFighter.Combat
{
    /// <summary>
    /// Anything that exposes a "currently blocking" state — read by attackers
    /// to decide whether incoming damage should be reduced.
    /// </summary>
    public interface IBlockable
    {
        bool IsBlocking { get; }
    }
}
