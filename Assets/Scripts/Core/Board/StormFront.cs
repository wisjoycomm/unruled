namespace Unruled.Core.Board
{
    /// <summary>NOAA space-weather scales: R (radio blackout), S (solar radiation), G (geomagnetic storm).</summary>
    public enum StormScale : byte
    {
        Radio = 1,
        Solar = 2,
        Geomagnetic = 3
    }

    /// <summary>ECS component: one storm over a disc of a star system, active until <see cref="EndTick"/>.</summary>
    public struct StormFront
    {
        public int ClusterId;
        public StormScale Scale;
        public byte Level; // 1..5
        public int CenterX;
        public int CenterY;
        public int Radius;
        public long EndTick;
    }

    /// <summary>ECS component: storm levels felt along a lane, capped by its tier's exposure. Written by the weather engine.</summary>
    public struct LaneWeather
    {
        public byte RadioBlackoutLevel;
        public byte SolarRadiationLevel;
        public byte GeomagneticStormLevel;
    }
}
