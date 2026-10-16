using Unruled.Core.Board;

namespace Unruled.Core.Military
{
    /// <summary>Charges whatever a lane costs to enter, beyond fuel. False = the fleet may not depart (nothing paid).</summary>
    public interface ITollGate
    {
        bool TryCharge(in FleetUnit unit, in StarlaneEdge edge);
    }
}
