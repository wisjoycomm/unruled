using System;

namespace Unruled.Core.Board
{
    /// <summary>Planet lifecycle states 3B and 4 (data sheet tab 02). ESI in milli-units, durations in ticks.</summary>
    public sealed class LifecycleBalance
    {
        public readonly int IrradiationMinTicks;
        public readonly int IrradiationMaxTicks;
        public readonly byte IrradiationSolarLevel;
        public readonly int IrradiatedEsiMilli;
        public readonly int TerraformedEsiMilli;
        public readonly int TerraformWater;
        public readonly int TerraformNitrogen;
        public readonly int TerraformBiomass;
        public readonly int TerraformNuclearCores;

        public LifecycleBalance(
            int irradiationMinTicks, int irradiationMaxTicks, byte irradiationSolarLevel, int irradiatedEsiMilli,
            int terraformedEsiMilli, int terraformWater, int terraformNitrogen, int terraformBiomass,
            int terraformNuclearCores)
        {
            if (irradiationMinTicks < 1) throw new ArgumentOutOfRangeException(nameof(irradiationMinTicks));
            if (irradiationMaxTicks < irradiationMinTicks) throw new ArgumentOutOfRangeException(nameof(irradiationMaxTicks));
            if (irradiationSolarLevel < 1 || irradiationSolarLevel > 5)
                throw new ArgumentOutOfRangeException(nameof(irradiationSolarLevel));
            if (irradiatedEsiMilli < 0 || irradiatedEsiMilli > 1000) throw new ArgumentOutOfRangeException(nameof(irradiatedEsiMilli));
            if (terraformedEsiMilli < 0 || terraformedEsiMilli > 1000) throw new ArgumentOutOfRangeException(nameof(terraformedEsiMilli));
            if (terraformWater < 0) throw new ArgumentOutOfRangeException(nameof(terraformWater));
            if (terraformNitrogen < 0) throw new ArgumentOutOfRangeException(nameof(terraformNitrogen));
            if (terraformBiomass < 0) throw new ArgumentOutOfRangeException(nameof(terraformBiomass));
            if (terraformNuclearCores < 0) throw new ArgumentOutOfRangeException(nameof(terraformNuclearCores));

            IrradiationMinTicks = irradiationMinTicks;
            IrradiationMaxTicks = irradiationMaxTicks;
            IrradiationSolarLevel = irradiationSolarLevel;
            IrradiatedEsiMilli = irradiatedEsiMilli;
            TerraformedEsiMilli = terraformedEsiMilli;
            TerraformWater = terraformWater;
            TerraformNitrogen = terraformNitrogen;
            TerraformBiomass = terraformBiomass;
            TerraformNuclearCores = terraformNuclearCores;
        }
    }
}
