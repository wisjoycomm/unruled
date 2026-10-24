using System;
using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Simulation
{
    /// <summary>All balance numbers the simulation reads. Built once (see <see cref="DefaultGameBalance"/>) and passed in.</summary>
    public sealed class GameBalanceConfig
    {
        public readonly RecipeDatabase Recipes;
        public readonly UniverseGenBalance Universe;
        public readonly StarlaneBalance Starlanes;
        public readonly NoaaBalance Weather;
        public readonly RefineryBalance Refinery;
        public readonly MiningBalance Mining;
        public readonly LifecycleBalance Lifecycle;
        public readonly LatencyBalance Latency;
        public readonly RadarBalance Radar;
        public readonly FleetBalance Fleets;
        public readonly CombatBalance Combat;
        public readonly RelocationBalance Relocation;
        public readonly LedgerBalance Ledger;
        public readonly StaBalance Sta;
        public readonly LeaderboardBalance Leaderboard;
        public readonly WtoBalance Wto;
        public readonly WtoContractBalance Contracts;
        public readonly SystemAuthorityBalance Authority;
        public readonly SystemFactionBalance SystemFactions;
        public readonly AiBalance Ai;
        public readonly NewGameBalance NewGame;
        public readonly ResearchBalance Research;
        public readonly ShipyardBalance Shipyard;
        public readonly SiteControlBalance SiteControl;
        public readonly JumpGateBalance JumpGates;
        public readonly ConvoyBalance Convoys;
        public readonly CitadelBalance Citadel;
        public readonly GalaxyBalance Galaxy;
        public readonly PresenceBalance Presence;

        public GameBalanceConfig(
            RecipeDatabase recipes, UniverseGenBalance universe, StarlaneBalance starlanes, NoaaBalance weather,
            RefineryBalance refinery, MiningBalance mining,
            LifecycleBalance lifecycle, LatencyBalance latency, RadarBalance radar, FleetBalance fleets, CombatBalance combat,
            RelocationBalance relocation, LedgerBalance ledger, StaBalance sta, LeaderboardBalance leaderboard,
            WtoBalance wto, WtoContractBalance contracts, SystemAuthorityBalance authority,
            SystemFactionBalance systemFactions, AiBalance ai, NewGameBalance newGame,
            ResearchBalance research, ShipyardBalance shipyard, SiteControlBalance siteControl,
            JumpGateBalance jumpGates, ConvoyBalance convoys, CitadelBalance citadel, GalaxyBalance galaxy,
            PresenceBalance presence)
        {
            Recipes = recipes ?? throw new ArgumentNullException(nameof(recipes));
            Universe = universe ?? throw new ArgumentNullException(nameof(universe));
            Starlanes = starlanes ?? throw new ArgumentNullException(nameof(starlanes));
            Weather = weather ?? throw new ArgumentNullException(nameof(weather));
            Refinery = refinery ?? throw new ArgumentNullException(nameof(refinery));
            Mining = mining ?? throw new ArgumentNullException(nameof(mining));
            Lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
            Latency = latency ?? throw new ArgumentNullException(nameof(latency));
            Radar = radar ?? throw new ArgumentNullException(nameof(radar));
            Fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            Relocation = relocation ?? throw new ArgumentNullException(nameof(relocation));
            Ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            Sta = sta ?? throw new ArgumentNullException(nameof(sta));
            Leaderboard = leaderboard ?? throw new ArgumentNullException(nameof(leaderboard));
            Wto = wto ?? throw new ArgumentNullException(nameof(wto));
            Contracts = contracts ?? throw new ArgumentNullException(nameof(contracts));
            Authority = authority ?? throw new ArgumentNullException(nameof(authority));
            SystemFactions = systemFactions ?? throw new ArgumentNullException(nameof(systemFactions));
            Ai = ai ?? throw new ArgumentNullException(nameof(ai));
            NewGame = newGame ?? throw new ArgumentNullException(nameof(newGame));
            Research = research ?? throw new ArgumentNullException(nameof(research));
            Shipyard = shipyard ?? throw new ArgumentNullException(nameof(shipyard));
            SiteControl = siteControl ?? throw new ArgumentNullException(nameof(siteControl));
            JumpGates = jumpGates ?? throw new ArgumentNullException(nameof(jumpGates));
            Convoys = convoys ?? throw new ArgumentNullException(nameof(convoys));
            Citadel = citadel ?? throw new ArgumentNullException(nameof(citadel));
            Galaxy = galaxy ?? throw new ArgumentNullException(nameof(galaxy));
            Presence = presence ?? throw new ArgumentNullException(nameof(presence));
        }
    }
}
