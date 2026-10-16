namespace Unruled.Core.Board
{
    /// <summary>Planet lifecycle from data sheet tab 02 (GDD §2.1 B).</summary>
    public enum PlanetLifecycleState : byte
    {
        PristineWorld = 1,
        DeepCoreStripping = 2,
        DepletedBarrenHusk = 3,
        IrradiatedDeadWorld = 4,
        TerraformedRecovery = 5
    }

    /// <summary>
    /// ECS component for a map site (planet). Fixed-point replacement for the GDD §3 SiteNode core fields:
    /// positions in integer map units, ESI in milli-units (0..1000).
    /// </summary>
    public struct SiteInfo
    {
        public int ClusterId;
        public int OrbitIndex;
        public int PosX;
        public int PosY;
        public ulong TerrainSeed;
        public int BaseEsiMilli;
        public int CurrentEsiMilli;
        public PlanetLifecycleState LifecycleState;
        public int OwnerPlayerId; // 0 = neutral, -1..-5 = THE SYSTEM, > 0 = player/bot
        public long IrradiatedUntilTick; // S4 background radiation after a nuclear strike; 0 = never irradiated

        public bool IsIrradiated(long tick) => tick < IrradiatedUntilTick;
    }
}
