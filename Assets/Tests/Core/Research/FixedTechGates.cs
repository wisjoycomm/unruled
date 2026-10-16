using Unruled.Core.Research;
using Unruled.Core.Simulation;

namespace Unruled.Tests.Core.Research
{
    /// <summary>Test double: every player is at one fixed tech tier, gated by the default gate tiers.</summary>
    public sealed class FixedTechGates : ITechGates
    {
        private readonly ResearchBalance _balance = DefaultGameBalance.CreateResearchBalance();

        public byte Tier;

        public FixedTechGates(byte tier) => Tier = tier;

        public byte TechTierOf(int playerId) => Tier;

        public bool Unlocks(int playerId, TechGate gate) => Tier >= _balance.GateTier(gate);
    }
}
