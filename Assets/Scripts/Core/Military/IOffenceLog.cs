namespace Unruled.Core.Military
{
    /// <summary>Where combat reports offences against THE SYSTEM's rules.</summary>
    public interface IOffenceLog
    {
        /// <summary>The player took WTO contract cargo by force.</summary>
        void ReportPiracy(int offenderPlayerId, long tick);
    }
}
