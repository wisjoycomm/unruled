using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Military;

namespace Unruled.Core.Simulation
{
    /// <summary>ECS component on a player HQ: online or not, newcomer protection and the offline shield.</summary>
    public struct PlayerPresence
    {
        public bool IsOnline;
        public bool IsNewcomer; // protected everywhere until the tutorial ends or the player gives it up
        public long ShieldFromTick; // the offline shield's window [from, until); empty when until <= from
        public long ShieldUntilTick;
        public long WeekIndex; // the game week the shield time below was spent in
        public long WeekShieldTicks;
    }

    /// <summary>
    /// Owns <see cref="PlayerPresence"/>. The host queues <see cref="CommandType.LogIn"/> and <see cref="CommandType.LogOut"/>
    /// on the clock when a player connects and leaves, and <see cref="CommandType.EndProtection"/> when the tutorial ends;
    /// the player may send that one too (target: its core base), to give newcomer protection up early. Each acts on the
    /// issuing player's own presence, whatever the target.
    /// <list type="bullet">
    /// <item>A newcomer is protected everywhere: its sites and fleets can't be fought, claimed, struck or raided.</item>
    /// <item>At logout an offline shield is booked: it rises after the delay and lasts at most the per-logout limit, within
    /// what is left of the week's allowance (booked up front, the unused part given back at login). While it is up, the
    /// player's own sites and the fleets docked there are protected; fleets out in space are not.</item>
    /// <item>A fight before the shield is up pushes it back by the delay, so logging out never ends a battle.</item>
    /// <item>Logging in, or a hostile act (claiming a rival's site, a nuclear strike, raiding convoys), ends the shield;
    /// a hostile act also ends newcomer protection.</item>
    /// </list>
    /// A player without a presence (THE SYSTEM, worlds saved before presence) is never protected.
    /// </summary>
    public sealed class PresenceSystem : ICommandHandler, IProtection
    {
        private readonly ComponentStore<PlayerPresence> _presences;
        private readonly ComponentStore<PlayerHq> _players;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly PresenceBalance _balance;
        private EntityId[] _byPlayer = new EntityId[16];

        public PresenceSystem(
            ComponentStore<PlayerPresence> presences, ComponentStore<PlayerHq> players, ComponentStore<SiteInfo> sites,
            PresenceBalance balance)
        {
            _presences = presences ?? throw new ArgumentNullException(nameof(presences));
            _players = players ?? throw new ArgumentNullException(nameof(players));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (!TryFind(command.PlayerId, out var hq))
                return;
            ref var presence = ref _presences.Get(hq);
            switch (command.CommandType)
            {
                case CommandType.LogIn:
                    presence.IsOnline = true;
                    DropShield(ref presence, tick);
                    break;
                case CommandType.LogOut:
                    if (!presence.IsOnline)
                        return;
                    presence.IsOnline = false;
                    BookShield(ref presence, tick + _balance.ShieldDelayTicks);
                    break;
                case CommandType.EndProtection:
                    presence.IsNewcomer = false;
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        /// <summary>Host side, for a new account: protected everywhere until the tutorial ends.</summary>
        public void GrantNewcomerProtection(EntityId hq)
        {
            if (!_presences.Has(hq)) throw new ArgumentException("Not a player HQ with a presence.", nameof(hq));
            _presences.Get(hq).IsNewcomer = true;
        }

        public bool IsShieldUp(EntityId hq, long tick) =>
            _presences.Has(hq) && tick >= _presences.Get(hq).ShieldFromTick && tick < _presences.Get(hq).ShieldUntilTick;

        public bool IsProtectedAt(int playerId, EntityId site, long tick)
        {
            if (!TryFind(playerId, out var hq))
                return false;
            ref var presence = ref _presences.Get(hq);
            if (presence.IsNewcomer)
                return true;
            return tick >= presence.ShieldFromTick && tick < presence.ShieldUntilTick && !site.IsNull && _sites.Has(site) &&
                   _sites.Get(site).OwnerPlayerId == playerId;
        }

        public void NoteCombat(int playerId, long tick)
        {
            if (!TryFind(playerId, out var hq))
                return;
            ref var presence = ref _presences.Get(hq);
            if (presence.ShieldUntilTick <= presence.ShieldFromTick || tick >= presence.ShieldFromTick)
                return;
            long shift = tick + _balance.ShieldDelayTicks - presence.ShieldFromTick;
            if (shift <= 0)
                return;
            presence.ShieldFromTick += shift;
            presence.ShieldUntilTick += shift;
        }

        public void EndProtection(int playerId, long tick)
        {
            if (!TryFind(playerId, out var hq))
                return;
            ref var presence = ref _presences.Get(hq);
            presence.IsNewcomer = false;
            DropShield(ref presence, tick);
        }

        // Books the shield from `from`: the per-logout limit within what is left of that week's allowance.
        private void BookShield(ref PlayerPresence presence, long from)
        {
            long week = from / PresenceBalance.TicksPerWeek;
            if (week != presence.WeekIndex)
            {
                presence.WeekIndex = week;
                presence.WeekShieldTicks = 0;
            }
            long length = Math.Min(_balance.ShieldMaxTicks, _balance.ShieldWeeklyTicks - presence.WeekShieldTicks);
            if (length <= 0)
                return;
            presence.ShieldFromTick = from;
            presence.ShieldUntilTick = from + length;
            presence.WeekShieldTicks += length;
        }

        // Ends the shield and gives back the part not used yet.
        private static void DropShield(ref PlayerPresence presence, long tick)
        {
            if (presence.ShieldUntilTick > presence.ShieldFromTick && tick < presence.ShieldUntilTick)
                presence.WeekShieldTicks -= presence.ShieldUntilTick - Math.Max(tick, presence.ShieldFromTick);
            presence.ShieldFromTick = 0;
            presence.ShieldUntilTick = 0;
        }

        private bool TryFind(int playerId, out EntityId hq)
        {
            hq = default;
            if (playerId <= 0)
                return false;
            if (playerId < _byPlayer.Length)
            {
                var cached = _byPlayer[playerId];
                if (!cached.IsNull && _presences.Has(cached) && _players.Has(cached) && _players.Get(cached).PlayerId == playerId)
                {
                    hq = cached;
                    return true;
                }
            }
            for (int i = 0; i < _players.Count; i++)
            {
                if (_players.At(i).PlayerId != playerId || !_presences.Has(_players.EntityAt(i)))
                    continue;
                hq = _players.EntityAt(i);
                if (playerId >= _byPlayer.Length)
                    Array.Resize(ref _byPlayer, Math.Max(playerId + 1, _byPlayer.Length * 2));
                _byPlayer[playerId] = hq;
                return true;
            }
            return false;
        }
    }
}
