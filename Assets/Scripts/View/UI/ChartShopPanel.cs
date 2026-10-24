using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Simulation;
using UnityEngine.UIElements;
using EntityId = Unruled.Core.Ecs.EntityId;

namespace Unruled.View.UI
{
    /// <summary>
    /// The MARKET tab's star-chart block. Offers: steps through the chart listings (THE SYSTEM's world shop first, then
    /// player shops) of systems the player doesn't know yet, with Buy. The selected system (from the last site picked
    /// on the board): Sell lists its chart on the player's own shop, or on the world shop when the player charted it
    /// first (a price prompt), Withdraw takes the player's listing down, Share gives it to a rival (a prompt for the
    /// player number). Buttons only forward commands through <see cref="CommandIssuer"/>; the chart market decides.
    /// </summary>
    public sealed class ChartShopPanel
    {
        private const int DefaultPriceCredits = 200;

        private readonly Label _offer, _status, _selected, _feedback;
        private readonly Button _prev, _next, _buy, _sell, _withdraw, _share;
        private readonly ModalPopup _popup;
        private readonly UiArt _art;

        private SimulationWorld _sim;
        private int _playerId;
        private EntityId _hq;
        private int _offerIndex;
        private EntityId _offerListing;
        private int _cluster = -1;
        private long _shownKey = long.MinValue;
        private string _shownLabel;

        public ChartShopPanel(VisualElement root, ModalPopup popup, UiArt art)
        {
            _popup = popup;
            _art = art;
            _offer = TopTelemetryHud.Require<Label>(root, "chart-offer");
            _status = TopTelemetryHud.Require<Label>(root, "chart-status");
            _selected = TopTelemetryHud.Require<Label>(root, "chart-selected");
            _feedback = TopTelemetryHud.Require<Label>(root, "chart-feedback");
            _prev = TopTelemetryHud.Require<Button>(root, "chart-prev");
            _next = TopTelemetryHud.Require<Button>(root, "chart-next");
            _buy = TopTelemetryHud.Require<Button>(root, "do-buy-chart");
            _sell = TopTelemetryHud.Require<Button>(root, "do-sell-chart");
            _withdraw = TopTelemetryHud.Require<Button>(root, "do-withdraw-chart");
            _share = TopTelemetryHud.Require<Button>(root, "do-share-chart");
            _prev.clicked += () => Step(-1);
            _next.clicked += () => Step(1);
            _buy.clicked += ConfirmBuy;
            _sell.clicked += PromptSell;
            _withdraw.clicked += Withdraw;
            _share.clicked += PromptShare;
        }

        private EntityId CoreBase => _sim != null && _sim.Players.Has(_hq) ? _sim.Players.Get(_hq).CoreBase : default;

        public void Bind(SimulationWorld sim, int playerId)
        {
            _sim = sim;
            _playerId = playerId;
            HudQueries.TryFindHq(sim, playerId, out _hq);
            _offerIndex = 0;
            _cluster = _sim.Sites.Has(CoreBase) ? _sim.Sites.Get(CoreBase).ClusterId : -1;
            _shownKey = long.MinValue;
        }

        /// <summary>A site picked on the board makes its system the one to sell or share.</summary>
        public void SelectSite(EntityId site)
        {
            if (_sim == null || !_sim.Sites.Has(site))
                return;
            _cluster = _sim.Sites.Get(site).ClusterId;
            _shownKey = long.MinValue;
        }

        public void Refresh()
        {
            if (_sim == null)
                return;
            int offers = CountOffers();
            if (_offerIndex >= offers)
                _offerIndex = 0;
            _offerListing = OfferAt(_offerIndex);
            bool knows = _cluster >= 0 && !_sim.Galaxy.SystemOf(_cluster).IsNull && _sim.Galaxy.Knows(_playerId, _cluster);
            var shop = SellShop();
            var mine = knows ? _sim.Charts.FindListing(_playerId, _cluster, shop) : default;
            bool listedByMe = !mine.IsNull && _sim.ChartListings.Get(mine).SellerPlayerId == _playerId;
            bool taken = !mine.IsNull && !listedByMe;
            long cash = _sim.Ledger.TryFindPlayerWallet(_playerId, out var wallet) ? _sim.Ledger.Balance(wallet) : 0;
            long price = _offerListing.IsNull ? 0 : _sim.ChartListings.Get(_offerListing).PriceCents;
            long key = offers ^ ((long)_offerIndex << 16) ^ ((long)_offerListing.Index << 24) ^ ((long)_cluster << 40) ^
                       (knows ? 1L << 60 : 0) ^ (listedByMe ? 1L << 61 : 0) ^ (taken ? 1L << 62 : 0) ^ (cash << 4);
            string label = Label(_cluster); // cached per system, so a rename shows up as a new reference
            if (key == _shownKey && ReferenceEquals(label, _shownLabel))
                return;
            _shownKey = key;
            _shownLabel = label;

            UiFormat.Set(_status, UiFormat.Number(offers) + (offers == 1 ? " OFFER" : " OFFERS"));
            if (_offerListing.IsNull)
                UiFormat.Set(_offer, "NO CHART ON SALE THAT YOU LACK");
            else
            {
                ref var listing = ref _sim.ChartListings.Get(_offerListing);
                UiFormat.Set(_offer, Label(listing.ClusterId) + " · " + UiFormat.Price(listing.PriceCents) + " CR\n" +
                                     (listing.Shop == ChartShop.World ? "WORLD SHOP · STA REGISTRY" : "SHOP OF P" + UiFormat.Number(listing.SellerPlayerId)) +
                                     " · " + UiFormat.Number(listing.Sales) + " SOLD");
            }
            _prev.SetEnabled(offers > 1);
            _next.SetEnabled(offers > 1);
            _buy.SetEnabled(!_offerListing.IsNull && cash >= price);

            UiFormat.Set(_selected, !knows ? "PICK A CHARTED SYSTEM ON THE BOARD"
                : "SELECTED · " + Label(_cluster) + (listedByMe ? " · LISTED" : taken ? " · ON THE WORLD SHOP" : "") +
                  (shop == ChartShop.World ? " · YOU CHARTED IT FIRST" : ""));
            _sell.SetEnabled(knows && mine.IsNull);
            _sell.text = shop == ChartShop.World ? "Sell on world shop" : "Sell chart";
            _withdraw.SetEnabled(listedByMe);
            _share.SetEnabled(knows);
        }

        private string Label(int cluster) => cluster >= 0 ? UiFormat.SystemLabel(_sim, cluster) : "";

        private ChartShop SellShop() =>
            _cluster >= 0 && _sim.Galaxy.FirstSurveyor(_cluster) == _playerId ? ChartShop.World : ChartShop.Player;

        // Offers are listings of systems the player doesn't know, world shop first, each group in store order.
        private bool IsOffer(in ChartListing listing) =>
            listing.SellerPlayerId != _playerId && !_sim.Galaxy.Knows(_playerId, listing.ClusterId);

        private int CountOffers()
        {
            int n = 0;
            for (int i = 0; i < _sim.ChartListings.Count; i++)
                if (IsOffer(in _sim.ChartListings.At(i)))
                    n++;
            return n;
        }

        private EntityId OfferAt(int index)
        {
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _sim.ChartListings.Count; i++)
                {
                    ref var listing = ref _sim.ChartListings.At(i);
                    if (!IsOffer(in listing) || (listing.Shop == ChartShop.World) != (pass == 0))
                        continue;
                    if (index-- == 0)
                        return _sim.ChartListings.EntityAt(i);
                }
            return default;
        }

        private void Step(int step)
        {
            int offers = CountOffers();
            if (offers == 0)
                return;
            _offerIndex = (_offerIndex + step + offers) % offers;
            _shownKey = long.MinValue;
            Refresh();
        }

        private void ConfirmBuy()
        {
            if (_sim == null || !_sim.ChartListings.Has(_offerListing))
                return;
            var listingId = _offerListing;
            ref var listing = ref _sim.ChartListings.Get(listingId);
            _popup.Confirm("BUY CHART · " + Label(listing.ClusterId),
                "Pays " + UiFormat.Price(listing.PriceCents) + " credits once; the chart is yours for good. Your fleets can then " +
                "fly to the system and your orders reach it." + (listing.Shop == ChartShop.World
                    ? "\n\nSold on THE SYSTEM's world shop: the STA keeps " +
                      UiFormat.Number(_sim.Balance.Galaxy.ChartSaleCutPermille / 10) + "% of the price."
                    : ""),
                _art.Market, "Buy chart", () => Send(CommandType.BuyChart, 0, 0, 0, listingId), ModalPopup.Tone.Normal);
        }

        private void PromptSell()
        {
            if (_sim == null || _cluster < 0)
                return;
            int cluster = _cluster;
            var shop = SellShop();
            string body = shop == ChartShop.World
                ? "You charted this system first, so the STA registry lists it on THE SYSTEM's world shop for a " +
                  UiFormat.Price(_sim.Balance.Galaxy.ChartListingFeeCents) + "-credit fee and keeps " +
                  UiFormat.Number(_sim.Balance.Galaxy.ChartSaleCutPermille / 10) + "% of each sale. "
                : "Listed on your own shop. ";
            _popup.Prompt("SELL CHART · " + Label(cluster),
                body + "Every buyer pays your price once and keeps the chart; the listing stays until you withdraw it. " +
                "Price in credits:", _art.Market, UiFormat.Number(DefaultPriceCredits), 7, IsPrice, "List chart",
                text => Send(CommandType.ListChart, (int)shop, int.Parse(text, System.Globalization.CultureInfo.InvariantCulture),
                    cluster, default));
        }

        private void Withdraw()
        {
            if (_sim != null && _cluster >= 0)
                Send(CommandType.CancelChartListing, (int)SellShop(), 0, _cluster, default);
        }

        private void PromptShare()
        {
            if (_sim == null || _cluster < 0)
                return;
            int cluster = _cluster;
            _popup.Prompt("SHARE CHART · " + Label(cluster),
                "Gives the chart to another player for free: their fleets can fly there and their orders reach it. " +
                "Player number:", _art.Market, "", 3, IsRival, "Share",
                text => Send(CommandType.ShareChart, int.Parse(text, System.Globalization.CultureInfo.InvariantCulture), 0,
                    cluster, default));
        }

        private static bool IsPrice(string text) =>
            int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out int value) && value > 0;

        private bool IsRival(string text) =>
            int.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture,
                out int player) && player != _playerId && _sim.Ledger.TryFindPlayerWallet(player, out _);

        private void Send(CommandType type, int paramA, int paramB, int cluster, EntityId listing)
        {
            if (_sim == null || CoreBase.IsNull)
                return;
            var command = new DelayedPlayerCommand
            {
                PlayerId = _playerId, CommandType = type, Target = CoreBase, ParamA = paramA, ParamB = paramB, ParamC = cluster
            };
            if (!listing.IsNull)
                command.ParamEntity = listing;
            string name = InFlightStrip.CommandName(type).ToUpperInvariant();
            var result = _sim.Issuer.Issue(command, out long executeAt);
            _feedback.text = result == IssueResult.Issued
                ? "SENT · " + name + " · LANDS IN " + UiFormat.Number(executeAt - _sim.Clock.CurrentTick) + "t"
                : "REFUSED · " + name + " · " + FleetCard.RefusalText(result);
            _shownKey = long.MinValue;
        }
    }
}
