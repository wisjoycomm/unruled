namespace Unruled.Core.SystemFactions
{
    /// <summary>THE SYSTEM's five agencies. As an owner id (sites, fleets) an agency is the negative of its value.</summary>
    public enum SystemAgency : byte
    {
        Sta = 1,
        Wto = 2,
        Who = 3,
        Iaea = 4,
        Noaa = 5
    }

    public static class SystemAgencies
    {
        public static int OwnerId(SystemAgency agency) => -(int)agency;
    }

    public enum BulletinKind : byte
    {
        NuclearStrike = 1,
        CallToArms = 2
    }

    /// <summary>
    /// ECS component: a public bulletin from THE SYSTEM, on its own entity, visible to every player until
    /// <see cref="ExpiresTick"/>.
    /// </summary>
    public struct SystemBulletin
    {
        public SystemAgency Agency;
        public BulletinKind Kind;
        public int PlayerId; // the player concerned, 0 if none
        public Ecs.EntityId Site;
        public long Tick;
        public long ExpiresTick;
    }
}
