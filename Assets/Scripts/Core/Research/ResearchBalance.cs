using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Research
{
    /// <summary>The cost and pace of one tier's research (tiers 2..5; tier 1 is known from the start).</summary>
    public sealed class TierResearchRules
    {
        public readonly int StressBaseChancePermille; // P_base(Tier); 1000 = guaranteed once telemetry & items are gathered
        public readonly ItemStack[] StressTestCost; // paid from the core base per attempt
        public readonly long RetoolTicks;
        public readonly int TelemetryTargetMilli; // 0 = use ResearchBalance.TelemetryTargetMilli

        public TierResearchRules(int stressBaseChancePermille, ItemStack[] stressTestCost, long retoolTicks,
            int telemetryTargetMilli = 0)
        {
            if (stressBaseChancePermille < 0 || stressBaseChancePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(stressBaseChancePermille));
            if (retoolTicks < 1) throw new ArgumentOutOfRangeException(nameof(retoolTicks));
            if (telemetryTargetMilli < 0) throw new ArgumentOutOfRangeException(nameof(telemetryTargetMilli));
            StressBaseChancePermille = stressBaseChancePermille;
            StressTestCost = (ItemStack[])(stressTestCost ?? throw new ArgumentNullException(nameof(stressTestCost))).Clone();
            RetoolTicks = retoolTicks;
            TelemetryTargetMilli = telemetryTargetMilli;
        }
    }

    /// <summary>Research rules (GDD §2.3): field telemetry, stress tests, retooling.</summary>
    public sealed class ResearchBalance
    {
        public const byte TopTier = 5;
        public const int MaxScaledChancePermille = 990;

        public readonly int TelemetryTargetMilli; // default / tier-2 field data target
        public readonly int TelemetryMilliPerStormLevel; // gathered per sample per storm level at the fleet's location
        public readonly int TelemetryMinMicrochips; // a fleet needs this many microchips aboard to measure
        public readonly int PityPermillePerFailure;
        public readonly int TelemetryChancePermillePerLevel; // sample capture chance per storm level each tick (1..1000)
        public readonly int TelemetryChanceBonusPerTierPermille; // bonus per Signals tier above 1
        public readonly byte LaserCommsTier;
        public readonly byte CoreFractureMiningTier;
        public readonly byte TerraformTier;
        public readonly byte JumpGateTier;
        private readonly TierResearchRules[] _tiers; // index 0 = tier 2

        public ResearchBalance(
            int telemetryTargetMilli, int telemetryMilliPerStormLevel, int telemetryMinMicrochips,
            int pityPermillePerFailure, byte laserCommsTier, byte coreFractureMiningTier, byte terraformTier,
            byte jumpGateTier, TierResearchRules[] tiers, int telemetryChancePermillePerLevel = 1000,
            int telemetryChanceBonusPerTierPermille = 0)
        {
            if (telemetryTargetMilli < 1) throw new ArgumentOutOfRangeException(nameof(telemetryTargetMilli));
            if (telemetryMilliPerStormLevel < 1) throw new ArgumentOutOfRangeException(nameof(telemetryMilliPerStormLevel));
            if (telemetryMinMicrochips < 1) throw new ArgumentOutOfRangeException(nameof(telemetryMinMicrochips));
            if (pityPermillePerFailure < 0) throw new ArgumentOutOfRangeException(nameof(pityPermillePerFailure));
            if (telemetryChancePermillePerLevel < 1 || telemetryChancePermillePerLevel > 1000)
                throw new ArgumentOutOfRangeException(nameof(telemetryChancePermillePerLevel));
            if (telemetryChanceBonusPerTierPermille < 0)
                throw new ArgumentOutOfRangeException(nameof(telemetryChanceBonusPerTierPermille));
            if (tiers == null || tiers.Length != TopTier - 1)
                throw new ArgumentException($"Rules for tiers 2..{TopTier} required.", nameof(tiers));
            for (int i = 0; i < tiers.Length; i++)
                if (tiers[i] == null) throw new ArgumentException($"Tier {i + 2} rules missing.", nameof(tiers));
            if (laserCommsTier < 1 || laserCommsTier > TopTier) throw new ArgumentOutOfRangeException(nameof(laserCommsTier));
            if (coreFractureMiningTier < 1 || coreFractureMiningTier > TopTier)
                throw new ArgumentOutOfRangeException(nameof(coreFractureMiningTier));
            if (terraformTier < 1 || terraformTier > TopTier) throw new ArgumentOutOfRangeException(nameof(terraformTier));
            if (jumpGateTier < 1 || jumpGateTier > TopTier) throw new ArgumentOutOfRangeException(nameof(jumpGateTier));
            LaserCommsTier = laserCommsTier;
            CoreFractureMiningTier = coreFractureMiningTier;
            TerraformTier = terraformTier;
            JumpGateTier = jumpGateTier;
            TelemetryTargetMilli = telemetryTargetMilli;
            TelemetryMilliPerStormLevel = telemetryMilliPerStormLevel;
            TelemetryMinMicrochips = telemetryMinMicrochips;
            PityPermillePerFailure = pityPermillePerFailure;
            TelemetryChancePermillePerLevel = telemetryChancePermillePerLevel;
            TelemetryChanceBonusPerTierPermille = telemetryChanceBonusPerTierPermille;
            _tiers = (TierResearchRules[])tiers.Clone();
        }

        /// <summary>Effective storm-telemetry capture chance in permille for <paramref name="stormLevel"/> at <paramref name="signalsTier"/>, capped at 99% when base is below 100%.</summary>
        public int EffectiveTelemetryChancePermille(int stormLevel, byte signalsTier) =>
            TelemetryChancePermillePerLevel >= 1000
                ? 1000
                : Math.Min(MaxScaledChancePermille,
                    stormLevel * TelemetryChancePermillePerLevel +
                    Math.Max(0, signalsTier - 1) * TelemetryChanceBonusPerTierPermille);

        /// <summary>The tech tier that unlocks <paramref name="gate"/>.</summary>
        public byte GateTier(TechGate gate)
        {
            switch (gate)
            {
                case TechGate.LaserComms: return LaserCommsTier;
                case TechGate.CoreFractureMining: return CoreFractureMiningTier;
                case TechGate.Terraform: return TerraformTier;
                case TechGate.JumpGate: return JumpGateTier;
                default: throw new ArgumentOutOfRangeException(nameof(gate));
            }
        }

        /// <summary>The research branch whose tier unlocks <paramref name="gate"/>.</summary>
        public ResearchBranch GateBranch(TechGate gate)
        {
            switch (gate)
            {
                case TechGate.LaserComms: return ResearchBranch.Signals;
                case TechGate.CoreFractureMining: return ResearchBranch.Industry;
                case TechGate.Terraform: return ResearchBranch.Industry;
                case TechGate.JumpGate: return ResearchBranch.Logistics;
                default: throw new ArgumentOutOfRangeException(nameof(gate));
            }
        }

        /// <summary>Rules for researching <paramref name="tier"/> (2..5).</summary>
        public TierResearchRules Tier(byte tier)
        {
            if (tier < 2 || tier > TopTier) throw new ArgumentOutOfRangeException(nameof(tier));
            return _tiers[tier - 2];
        }

        /// <summary>Telemetry target in milli-units required to unlock <paramref name="tier"/> (2..5).</summary>
        public int TierTelemetryTargetMilli(byte tier)
        {
            int target = Tier(tier).TelemetryTargetMilli;
            return target > 0 ? target : TelemetryTargetMilli;
        }

        /// <summary>P_success(k) = Min(1.0, P_base(Tier) + (k - 1) * PityPermillePerFailure), in permille; k - 1 = failed tests.</summary>
        public int StressChancePermille(byte tier, int failedTests) =>
            (int)Math.Min(1000L, Tier(tier).StressBaseChancePermille + (long)failedTests * PityPermillePerFailure);
    }
}
