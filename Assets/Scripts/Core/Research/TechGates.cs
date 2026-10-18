using System;
using Unruled.Core.Ecs;
using Unruled.Core.Military;

namespace Unruled.Core.Research
{
    /// <summary>Abilities a player's tech tier unlocks (GDD §1.2, §2.1 B).</summary>
    public enum TechGate : byte
    {
        LaserComms = 1, // orders get through R-scale radio blackouts
        CoreFractureMining = 2, // rigs may mine an unlocked core fracture
        Terraform = 3, // an irradiated world may be terraformed
        JumpGate = 4 // the home system's dormant jump gate may be opened
    }

    /// <summary>Read-only tech tier queries for the systems a tier gates.</summary>
    public interface ITechGates
    {
        byte TechTierOf(int playerId);
        byte TechTierOf(int playerId, ResearchBranch branch);
        bool Unlocks(int playerId, TechGate gate);
    }

    /// <summary>Reads <see cref="TechProgress"/> on player HQs (owned by <see cref="ResearchSystem"/>).</summary>
    public sealed class TechGates : ITechGates
    {
        private readonly ComponentStore<TechProgress> _progress;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ResearchBalance _balance;

        public TechGates(ComponentStore<TechProgress> progress, ComponentStore<PlayerHq> players, ResearchBalance balance)
        {
            _progress = progress ?? throw new ArgumentNullException(nameof(progress));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        /// <summary>The player's Military tech tier; 1 for a player without research (and for THE SYSTEM or neutral ids).</summary>
        public byte TechTierOf(int playerId) => TechTierOf(playerId, ResearchBranch.Military);

        /// <summary>The player's tech tier in <paramref name="branch"/>; 1 for a player without research (and for THE SYSTEM or neutral ids).</summary>
        public byte TechTierOf(int playerId, ResearchBranch branch)
        {
            if (playerId <= 0)
                return 1;
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players.At(i).PlayerId != playerId)
                    continue;
                var hq = _players.EntityAt(i);
                return _progress.Has(hq) ? _progress.Get(hq).Tier(branch) : (byte)1;
            }
            return 1;
        }

        public bool Unlocks(int playerId, TechGate gate) =>
            TechTierOf(playerId, _balance.GateBranch(gate)) >= _balance.GateTier(gate);
    }
}
