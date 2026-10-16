namespace Unruled.Core.Board
{
    /// <summary>ECS component: current NOAA storm levels over a site (0 = calm, 1..5 = R/S/G scale).</summary>
    public struct SiteWeather
    {
        public byte RadioBlackoutLevel;
        public byte SolarRadiationLevel;
        public byte GeomagneticStormLevel;
    }
}
