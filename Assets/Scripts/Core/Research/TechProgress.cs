using Unruled.Core.Ecs;

namespace Unruled.Core.Research
{
    /// <summary>The three physical steps that unlock the next tech tier (GDD §2.3).</summary>
    public enum ResearchStage : byte
    {
        FieldTelemetry = 0, // TRL 1-3: a fleet with microchips sits in a NOAA storm
        StressTest = 1, // TRL 4-6: prototype runs at the core base, success odds with calibration pity
        Retooling = 2, // TRL 7-9: the factory retools; the tier unlocks when it finishes
        Complete = 3 // the top tier is reached
    }

    /// <summary>ECS component on a player HQ: the player's tech tier and the research toward the next one.</summary>
    public struct TechProgress
    {
        public byte TechTier; // 1..5, the highest tier the player can build, retrofit and use
        public ResearchStage Stage;
        public int TelemetryMilli; // field telemetry gathered toward the next tier, 0..TelemetryTargetMilli
        public int FailedTests; // stress tests failed toward the next tier: the calibration pity
        public long RetoolUntilTick;
        public EntityId LastTelemetryFleet; // the fleet that last gathered telemetry, for the views
    }
}
