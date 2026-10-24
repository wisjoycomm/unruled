namespace Unruled.Core.Galaxy
{
    /// <summary>Where a chart is offered.</summary>
    public enum ChartShop : byte
    {
        Player = 0, // the seller's own shop: anyone who knows the system may list it
        World = 1 // THE SYSTEM's shop: only the system's first surveyor, for an STA listing fee and a cut of each sale
    }

    /// <summary>
    /// ECS component on its own entity: a standing offer of one system's chart at a fixed price. Every buyer pays once
    /// and keeps the chart; the listing stays until the seller cancels it. Owned by <see cref="ChartMarket"/>.
    /// </summary>
    public struct ChartListing
    {
        public int SellerPlayerId;
        public int ClusterId;
        public long PriceCents;
        public ChartShop Shop;
        public long ListedTick;
        public int Sales;
    }
}
