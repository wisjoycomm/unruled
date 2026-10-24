using System;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Simulation;

namespace Unruled.Core.Galaxy
{
    /// <summary>
    /// Owns <see cref="ChartListing"/>: how star charts change hands. Knowledge itself stays with
    /// <see cref="GalaxySystem"/>; a chart sold or shared is learnt through its one door.
    /// <list type="bullet">
    /// <item>ShareChart (Target: a site of the system; ParamA: the recipient player) gives the chart for free.</item>
    /// <item>ListChart (Target: a site of the system; ParamA: <see cref="ChartShop"/>; ParamB: price in credits) offers
    /// it at a fixed price. Anyone who knows the system may list it on their own shop, once. Only the system's first
    /// surveyor may list it on THE SYSTEM's world shop, once per system, paying the STA listing fee; the STA registry
    /// publishes it.</item>
    /// <item>CancelChartListing (Target: a site of the system; ParamA: the shop) withdraws the seller's listing.</item>
    /// <item>BuyChart (Target: an own site, so the order travels to the buyer's base; ParamEntity: the listing) pays the
    /// price once and the buyer knows the system for good; on the world shop the STA keeps its cut. The listing stays
    /// for the next buyer.</item>
    /// </list>
    /// Players THE SYSTEM bars from trading can't list or buy. Invalid, unaffordable or pointless orders (sharing or
    /// buying what the other side already knows) change nothing.
    /// </summary>
    public sealed class ChartMarket : ICommandHandler
    {
        private readonly World _world;
        private readonly ComponentStore<ChartListing> _listings;
        private readonly ComponentStore<StarSystem> _systems;
        private readonly ComponentStore<SiteInfo> _sites;
        private readonly GalaxySystem _galaxy;
        private readonly Ledger _ledger;
        private readonly EntityId _staPool;
        private readonly IMarketAccess _access;
        private readonly IChartRegistryLog _registry;
        private readonly GalaxyBalance _balance;

        public ChartMarket(World world, ComponentStore<ChartListing> listings, ComponentStore<StarSystem> systems,
            ComponentStore<SiteInfo> sites, GalaxySystem galaxy, Ledger ledger, EntityId staPool, IMarketAccess access,
            IChartRegistryLog registry, GalaxyBalance balance)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _listings = listings ?? throw new ArgumentNullException(nameof(listings));
            _systems = systems ?? throw new ArgumentNullException(nameof(systems));
            _sites = sites ?? throw new ArgumentNullException(nameof(sites));
            _galaxy = galaxy ?? throw new ArgumentNullException(nameof(galaxy));
            _ledger = ledger ?? throw new ArgumentNullException(nameof(ledger));
            _staPool = staPool;
            _access = access ?? throw new ArgumentNullException(nameof(access));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public void Handle(in DelayedPlayerCommand command, long tick)
        {
            switch (command.CommandType)
            {
                case CommandType.ShareChart:
                    Share(command.PlayerId, command.Target, command.ParamA, tick);
                    break;
                case CommandType.ListChart:
                    List(command.PlayerId, command.Target, command.ParamA, command.ParamB, tick);
                    break;
                case CommandType.CancelChartListing:
                    Cancel(command.PlayerId, command.Target, command.ParamA);
                    break;
                case CommandType.BuyChart:
                    Buy(command.PlayerId, command.Target, command.ParamEntity, tick);
                    break;
                default:
                    throw new ArgumentException($"Cannot handle {command.CommandType}.", nameof(command));
            }
        }

        /// <summary>The listing of a system's chart by a seller on a shop, or null.</summary>
        public EntityId FindListing(int sellerPlayerId, int clusterId, ChartShop shop)
        {
            for (int i = 0; i < _listings.Count; i++)
            {
                ref var listing = ref _listings.At(i);
                if (listing.ClusterId == clusterId && listing.Shop == shop &&
                    (shop == ChartShop.World || listing.SellerPlayerId == sellerPlayerId))
                    return _listings.EntityAt(i);
            }
            return default;
        }

        private void Share(int playerId, EntityId site, int recipient, long tick)
        {
            if (!TryKnownCluster(playerId, site, out int cluster) || recipient <= 0 || recipient == playerId ||
                !_ledger.TryFindPlayerWallet(recipient, out _))
                return;
            _galaxy.Learn(recipient, cluster, ChartSource.Share, tick);
        }

        private void List(int playerId, EntityId site, int shopParam, int priceCredits, long tick)
        {
            if (shopParam < 0 || shopParam > (int)ChartShop.World || priceCredits <= 0 || !_access.MayTrade(playerId, tick) ||
                !TryKnownCluster(playerId, site, out int cluster) || !_ledger.TryFindPlayerWallet(playerId, out var wallet))
                return;
            var shop = (ChartShop)shopParam;
            if (!FindListing(playerId, cluster, shop).IsNull)
                return;
            var system = _galaxy.SystemOf(cluster);
            if (shop == ChartShop.World)
            {
                if (_systems.Get(system).FirstSurveyorPlayerId != playerId ||
                    !_ledger.Transfer(wallet, _staPool, _balance.ChartListingFeeCents))
                    return;
                _registry.ReportChartRegistered(playerId, system, tick);
            }
            _listings.Add(_world.Create(), new ChartListing
            {
                SellerPlayerId = playerId, ClusterId = cluster, PriceCents = priceCredits * Wallet.CentsPerCredit,
                Shop = shop, ListedTick = tick
            });
        }

        private void Cancel(int playerId, EntityId site, int shopParam)
        {
            if (shopParam < 0 || shopParam > (int)ChartShop.World || !_sites.Has(site))
                return;
            var listing = FindListing(playerId, _sites.Get(site).ClusterId, (ChartShop)shopParam);
            if (!listing.IsNull && _listings.Get(listing).SellerPlayerId == playerId)
                _world.Destroy(listing);
        }

        private void Buy(int playerId, EntityId ownSite, EntityId listingId, long tick)
        {
            if (!_listings.Has(listingId) || !_sites.Has(ownSite) || _sites.Get(ownSite).OwnerPlayerId != playerId ||
                !_access.MayTrade(playerId, tick) || !_ledger.TryFindPlayerWallet(playerId, out var buyer))
                return;
            ref var listing = ref _listings.Get(listingId);
            if (listing.SellerPlayerId == playerId || _galaxy.Knows(playerId, listing.ClusterId) ||
                !_ledger.TryFindPlayerWallet(listing.SellerPlayerId, out var seller) ||
                _ledger.Balance(buyer) < listing.PriceCents)
                return;
            long cut = listing.Shop == ChartShop.World ? listing.PriceCents * _balance.ChartSaleCutPermille / 1000 : 0;
            _ledger.Transfer(buyer, _staPool, cut);
            _ledger.Transfer(buyer, seller, listing.PriceCents - cut);
            listing.Sales++;
            _galaxy.Learn(playerId, listing.ClusterId, ChartSource.Purchase, tick);
        }

        private bool TryKnownCluster(int playerId, EntityId site, out int cluster)
        {
            cluster = -1;
            if (playerId <= 0 || !_sites.Has(site))
                return false;
            cluster = _sites.Get(site).ClusterId;
            return !_galaxy.SystemOf(cluster).IsNull && _galaxy.Knows(playerId, cluster);
        }
    }
}
