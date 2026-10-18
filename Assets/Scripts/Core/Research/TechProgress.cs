using Unruled.Core.Ecs;

namespace Unruled.Core.Research
{
    /// <summary>The four research tracks (GDD §2.3: 5 tiers × 4 branches × 3 stages).</summary>
    public enum ResearchBranch : byte
    {
        Military = 0, // ship tier for build/retrofit, nuclear strike (T5)
        Industry = 1, // core-fracture mining (T4), terraforming (T5)
        Logistics = 2, // jump gate (T2), storm-shortcut lanes (T4)
        Signals = 3 // laser comms (T4)
    }

    /// <summary>The three physical steps that unlock the next tech tier (GDD §2.3).</summary>
    public enum ResearchStage : byte
    {
        FieldTelemetry = 0, // TRL 1-3: a fleet with microchips sits in a NOAA storm
        StressTest = 1, // TRL 4-6: prototype runs at the core base, success odds with calibration pity
        Retooling = 2, // TRL 7-9: the factory retools; the tier unlocks when it finishes
        Complete = 3 // the top tier is reached on every branch
    }

    /// <summary>ECS component on a player HQ: the player's per-branch tech tiers and the research toward the next one.</summary>
    public struct TechProgress
    {
        public const int BranchCount = 4;

        public byte TechTier; // 1..5, Military branch tier: the highest tier the player can build, retrofit and strike at
        public ResearchStage Stage;
        public int TelemetryMilli; // field telemetry gathered toward the next tier, 0..TelemetryTargetMilli
        public int FailedTests; // stress tests failed toward the next tier on ActiveBranch: the calibration pity
        public long RetoolUntilTick;
        public EntityId LastTelemetryFleet; // the fleet that last gathered telemetry, for the views
        public byte IndustryTier; // 1..5, Industry branch tier (core fracture T4, terraform T5)
        public byte LogisticsTier; // 1..5, Logistics branch tier (jump gate T2, storm shortcuts T4)
        public byte SignalsTier; // 1..5, Signals branch tier (laser comms T4)
        public ResearchBranch ActiveBranch;

        public static TechProgress Create(byte tier = 1) => new TechProgress
        {
            TechTier = tier,
            IndustryTier = tier,
            LogisticsTier = tier,
            SignalsTier = tier
        };

        public readonly byte Tier(ResearchBranch branch)
        {
            byte t;
            switch (branch)
            {
                case ResearchBranch.Military: t = TechTier; break;
                case ResearchBranch.Industry: t = IndustryTier; break;
                case ResearchBranch.Logistics: t = LogisticsTier; break;
                case ResearchBranch.Signals: t = SignalsTier; break;
                default: return 1;
            }
            return t == 0 ? (byte)1 : t;
        }

        public void SetTier(ResearchBranch branch, byte tier)
        {
            switch (branch)
            {
                case ResearchBranch.Military: TechTier = tier; break;
                case ResearchBranch.Industry: IndustryTier = tier; break;
                case ResearchBranch.Logistics: LogisticsTier = tier; break;
                case ResearchBranch.Signals: SignalsTier = tier; break;
            }
        }

        public readonly bool AllAtOrAbove(byte topTier) =>
            Tier(ResearchBranch.Military) >= topTier &&
            Tier(ResearchBranch.Industry) >= topTier &&
            Tier(ResearchBranch.Logistics) >= topTier &&
            Tier(ResearchBranch.Signals) >= topTier;
    }
}

