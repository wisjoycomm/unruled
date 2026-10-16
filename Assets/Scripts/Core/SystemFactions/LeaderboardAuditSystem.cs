using System;
using System.Collections.Generic;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;

namespace Unruled.Core.SystemFactions
{
    /// <summary>
    /// Owns <see cref="PlayerStanding"/>: the Top-10K leaderboard audit (tab 05, GDD §4.4).
    /// AuditOptIn (target: any site, for latency; ParamA 1 = join, 0 = leave) enters or leaves the audit. Leaving while
    /// in a bracket makes the player evading (see <see cref="BracketRates"/>) until they join again.
    /// Every audit interval the audited players are ranked by net worth (PROVISIONAL: credits plus the stock of their
    /// sites and fleets at the WTO reference price; ties go to the lower player id) and given the bracket of their
    /// rank. Each bracket then gets its dividend share of the WTO fee pool and its grant share of the STA toll pool,
    /// as they stood at the audit, split equally among its members; what doesn't divide evenly, and the shares of
    /// empty brackets, stay in the pools. Nothing is paid that the pools don't hold. PROVISIONAL: grants are paid in
    /// credits, since the pools hold only credits.
    /// </summary>
    public sealed class LeaderboardAuditSystem : ISimSystem, ICommandHandler
    {
        private struct Entry
        {
            public long Worth;
            public int PlayerId;
            public EntityId Hq;
            public byte Bracket;
        }

        private sealed class RichestFirst : IComparer<Entry>
        {
            public int Compare(Entry a, Entry b) =>
                a.Worth != b.Worth ? b.Worth.CompareTo(a.Worth) : a.PlayerId.CompareTo(b.PlayerId);
        }

        private readonly ComponentStore<PlayerStanding> _standings;
        private readonly ComponentStore<Wallet> _wallets;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly ComponentStore<FleetUnit> _fleets;
        private readonly ComponentStore<Stockpile> _stockpiles;
        private readonly Ledger _ledger;
        private readonly EntityId _wtoPool;
        private readonly EntityId _staPool;
        private readonly WtoMarketEngine _market;
        private readonly LeaderboardBalance _balance;

        private readonly RichestFirst _order = new RichestFirst();
        private readonly Dictionary<int, int> _entryOf = new Dictionary<int, int>();
        private readonly long[] _prices = new long[ItemIds.Count];
        private readonly int[] _members;
        private Entry[] _entries = new Entry[16];

        public LeaderboardAuditSystem(
            ComponentStore<PlayerStanding> standings, ComponentStore<Wallet> wallets, ComponentStore<SiteInfo> sites,
            ComponentStore<FleetUnit> fleets, ComponentStore<Stockpile> stockpiles, Ledger ledger, EntityId wtoPool,
            EntityId staPool, WtoMarketEngine market, LeaderboardBalance balance)
        {
            _standings = standings ?? throw new ArgumentNullException(nameof(standings));
            _wallets = wallets ?? throw new ArgumentNullException(nameof(wallets));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _fleets = fleets ?? throw new ArgumentNullException(nameof(fleets));
            _stockpiles = stockpiles ?? throw new ArgumentNullException(nameof(stockpiles));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            if (wtoPool.IsNull) throw new ArgumentException("WTO fee pool required.", nameof(wtoPool));
            if (staPool.IsNull) throw new ArgumentException("STA toll pool required.", nameof(staPool));
            _wtoPool = wtoPool;
            _staPool = staPool;
            _market = market ?? throw new ArgumentNullException(nameof(market));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _members = new int[balance.Count];
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            if (command.CommandType != CommandType.AuditOptIn)
                throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            if (!_ledger.TryFindPlayerWallet(command.PlayerId, out var hq) || !_standings.Has(hq))
                return;
            ref var standing = ref _standings.Get(hq);
            if (command.ParamA != 0)
            {
                standing.IsAudited = true;
                standing.IsEvading = false;
            }
            else if (standing.IsAudited)
            {
                standing.IsAudited = false;
                standing.IsEvading = standing.Bracket != LeaderboardBalance.Unrated;
                standing.Bracket = LeaderboardBalance.Unrated;
                standing.Rank = 0;
            }
        }

        public void OnTick(long tick)
        {
            if (tick % _balance.AuditIntervalTicks != 0)
                return;

            for (int slot = 0; slot < ItemIds.Count; slot++)
                _prices[slot] = _market.ReferencePriceCents(ItemIds.FromSlot(slot), tick);

            int count = CollectAudited();
            for (int i = 0; i < _sites.Count; i++)
                AddStock(_sites.At(i).OwnerPlayerId, _sites.EntityAt(i));
            for (int i = 0; i < _fleets.Count; i++)
                AddStock(_fleets.At(i).OwnerPlayerId, _fleets.EntityAt(i));
            Array.Sort(_entries, 0, count, _order);

            Array.Clear(_members, 0, _members.Length);
            for (int r = 0; r < count; r++)
            {
                ref var entry = ref _entries[r];
                entry.Bracket = _balance.BracketForRank(r + 1);
                ref var standing = ref _standings.Get(entry.Hq);
                standing.Bracket = entry.Bracket;
                standing.Rank = entry.Bracket == LeaderboardBalance.Unrated ? 0 : r + 1;
                _members[entry.Bracket]++;
            }

            long dividends = _ledger.Balance(_wtoPool);
            long grants = _ledger.Balance(_staPool);
            for (int r = 0; r < count; r++)
            {
                ref var entry = ref _entries[r];
                if (entry.Bracket == LeaderboardBalance.Unrated)
                    continue;
                var rules = _balance.Bracket(entry.Bracket);
                int members = _members[entry.Bracket];
                Pay(_wtoPool, entry.Hq, dividends * rules.DividendSharePercent / 100 / members);
                Pay(_staPool, entry.Hq, grants * rules.GrantSharePercent / 100 / members);
            }
        }

        private int CollectAudited()
        {
            _entryOf.Clear();
            int count = 0;
            for (int i = 0; i < _standings.Count; i++)
            {
                if (!_standings.At(i).IsAudited)
                    continue;
                var hq = _standings.EntityAt(i);
                ref var wallet = ref _wallets.Get(hq);
                if (count == _entries.Length)
                    Array.Resize(ref _entries, count * 2);
                _entries[count] = new Entry { Worth = wallet.Cents, PlayerId = wallet.OwnerPlayerId, Hq = hq };
                _entryOf[wallet.OwnerPlayerId] = count;
                count++;
            }
            return count;
        }

        private void AddStock(int ownerPlayerId, EntityId holder)
        {
            if (ownerPlayerId <= 0 || !_entryOf.TryGetValue(ownerPlayerId, out int index) || !_stockpiles.Has(holder))
                return;
            ref var stock = ref _stockpiles.Get(holder);
            long worth = 0;
            for (int slot = 0; slot < ItemIds.Count; slot++)
                worth += stock.Get(ItemIds.FromSlot(slot)) * _prices[slot];
            _entries[index].Worth += worth;
        }

        private void Pay(EntityId pool, EntityId hq, long cents)
        {
            if (!_ledger.Transfer(pool, hq, cents))
                throw new InvalidOperationException("Leaderboard payout exceeds its pool.");
        }
    }
}
