using Unruled.Core.Ecs;

namespace Unruled.Core.Board
{
    /// <summary>
    /// ECS component on its own entity: a home system's jump gate (GDD §1.2). Dormant while <see cref="Lane"/> is null:
    /// the home system has no lane out and cannot be reached. Opening it adds a lane from <see cref="HomeSite"/> to
    /// <see cref="FarSite"/> in the contested cluster, for good.
    /// </summary>
    public struct JumpGate
    {
        public int OwnerPlayerId;
        public EntityId HomeSite;
        public EntityId FarSite;
        public EntityId Lane;
        public long OpenedAtTick;

        public bool IsOpen => !Lane.IsNull;
    }
}
