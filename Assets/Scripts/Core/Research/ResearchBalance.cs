using System;
using Unruled.Core.Simulation;

namespace Unruled.Core.Research
{
    /// <summary>The cost and pace of one tier's research (tiers 2..5; tier 1 is known from the start).</summary>
    public sealed class TierResearchRules
    {
        public readonly int StressBaseChancePermille; // P_base(Tier)
        public readonly ItemStack[] StressTestCost; // paid from the core base per attempt
        public readonly long RetoolTicks;

        public TierResearchRules(int stressBaseChancePermille, ItemStack[] stressTestCost, long retoolTicks)
        {
            if (stressBaseChancePermille < 0 || stressBaseChancePermille > 1000)
                throw new ArgumentOutOfRangeException(nameof(stressBaseChancePermille));
            if (retoolTicks < 1) throw new ArgumentOutOfRangeException(nameof(retoolTicks));
            StressBaseChancePermille = stressBaseChancePermille;
            StressTestCost = (ItemStack[])(stressTestCost ?? throw new ArgumentNullException(nameof(stressTestCost))).Clone();
            RetoolTicks = retoolTicks;
        }
    }

    /// <summary>Research rules (GDD §2.3): field telemetry, stress tests with calibration pity, retooling.</summary>
    public sealed class ResearchBalance
    {
        public const byte TopTier = 5;

        public readonly int TelemetryTargetMilli; // "100%" of the field data
        public readonly int TelemetryMilliPerStormLevel; // gathered per tick per storm level at the fleet's location
        public readonly int TelemetryMinMicrochips; // a fleet needs this many microchips aboard to measure
        public readonly int PityPermillePerFailure; // +18% per failed test
        public readonly byte LaserCommsTier;
        public readonly byte CoreFractureMiningTier;
        public readonly byte TerraformTier;
        public readonly byte JumpGateTier;
        private readonly TierResearchRules[] _tiers; // index 0 = tier 2

        public ResearchBalance(
            int telemetryTargetMilli, int telemetryMilliPerStormLevel, int telemetryMinMicrochips,
            int pityPermillePerFailure, byte laserCommsTier, byte coreFractureMiningTier, byte terraformTier,
            byte jumpGateTier, TierResearchRules[] tiers)
        {
            if (telemetryTargetMilli < 1) throw new ArgumentOutOfRangeException(nameof(telemetryTargetMilli));
            if (telemetryMilliPerStormLevel < 1) throw new ArgumentOutOfRangeException(nameof(telemetryMilliPerStormLevel));
            if (telemetryMinMicrochips < 1) throw new ArgumentOutOfRangeException(nameof(telemetryMinMicrochips));
            if (pityPermillePerFailure < 0) throw new ArgumentOutOfRangeException(nameof(pityPermillePerFailure));
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
            _tiers = (TierResearchRules[])tiers.Clone();
        }

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

        /// <summary>Rules for researching <paramref name="tier"/> (2..5).</summary>
        public TierResearchRules Tier(byte tier)
        {
            if (tier < 2 || tier > TopTier) throw new ArgumentOutOfRangeException(nameof(tier));
            return _tiers[tier - 2];
        }

        /// <summary>P_success(k) = Min(1.0, P_base(Tier) + (k - 1) * 0.18), in permille; k - 1 = failed tests.</summary>
        public int StressChancePermille(byte tier, int failedTests) =>
            (int)Math.Min(1000L, Tier(tier).StressBaseChancePermille + (long)failedTests * PityPermillePerFailure);
    }
}
