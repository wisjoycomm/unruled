using Unruled.Core.Ecs;

namespace Unruled.Core.Economy
{
    /// <summary>
    /// ECS component: one ore body on a site with three separate reserves (GDD §2.1 B). Mined top-down:
    /// surface, then deep crust, then core fracture once a nuclear strike has unlocked it.
    /// </summary>
    public struct MineralDeposit
    {
        public EntityId Site;
        public ItemId RawItem;
        public int SurfaceReserve;
        public int DeepCrustReserve;
        public int CoreFractureReserve;
        public bool IsCoreFractureUnlocked;
        public int RigCount;
    }
}
