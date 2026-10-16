using System;

namespace Unruled.Core.Military
{
    /// <summary>
    /// Hughes salvo combat (GDD §2.4 C, data sheet tab 04 Combat_Ratio rows). Firepower, point defence and hull are in
    /// hundredths of a damage point; temperatures in milli-°C; per-ship supply use in milli-units.
    /// </summary>
    public sealed class CombatBalance
    {
        public readonly int SalvoCycleTicks;
        public readonly int LineAlpha; // damage points per line ship per salvo at 100% ROF
        public readonly int EwBeta; // point-defence points per EW ship per salvo
        public readonly int ConservationRofPercent;
        public readonly int StandardRofPercent;
        public readonly int OverdriveRofPercent;
        public readonly int FortificationPdPercent; // defender at its own site
        public readonly int FlankStepPercent; // PD lost per extra independent attack lane
        public readonly int FlankFloorPercent;
        public readonly int MaxExtraLanes;
        public readonly int LineHp;
        public readonly int EwHp;
        public readonly int SupplyHp;
        public readonly int CargoHp;
        public readonly int AmmoPerLineShipMilli; // per salvo at 100% ROF, times the tier's ammo burn
        public readonly int BatteryPerEwShipMilli; // per salvo with PD up, times the tier's battery burn
        public readonly int VentCoolantPerShipMilli; // per venting salvo, times the tier's coolant burn
        public readonly int TenderTransferPerSalvo; // items of each kind one tender hands over per salvo
        public readonly int ResupplyTarget; // tenders top friendly holds up to this many of each item
        public readonly int AmbientMilli;
        public readonly int FireHeatMilli; // per salvo at 100% ROF; scales with ROF squared
        public readonly int PassiveCoolMilli;
        public readonly int VentCoolMilli;
        public readonly int OverheatMilli;
        public readonly int OverheatReleaseMilli;
        public readonly int CookOffHullPercent;
        public readonly int FuelPerShipPerSalvoMilli; // manoeuvring fuel of every acting ship, times the tier's fuel burn

        public CombatBalance(
            int salvoCycleTicks, int lineAlpha, int ewBeta, int conservationRofPercent, int standardRofPercent,
            int overdriveRofPercent, int fortificationPdPercent, int flankStepPercent, int flankFloorPercent,
            int maxExtraLanes, int lineHp, int ewHp, int supplyHp, int cargoHp, int ammoPerLineShipMilli,
            int batteryPerEwShipMilli, int ventCoolantPerShipMilli, int tenderTransferPerSalvo, int resupplyTarget,
            int ambientMilli, int fireHeatMilli, int passiveCoolMilli, int ventCoolMilli, int overheatMilli,
            int overheatReleaseMilli, int cookOffHullPercent, int fuelPerShipPerSalvoMilli)
        {
            if (salvoCycleTicks < 1) throw new ArgumentOutOfRangeException(nameof(salvoCycleTicks));
            if (lineAlpha < 0) throw new ArgumentOutOfRangeException(nameof(lineAlpha));
            if (ewBeta < 0) throw new ArgumentOutOfRangeException(nameof(ewBeta));
            if (conservationRofPercent < 0 || standardRofPercent < conservationRofPercent || overdriveRofPercent < standardRofPercent)
                throw new ArgumentOutOfRangeException(nameof(standardRofPercent));
            if (fortificationPdPercent < 0) throw new ArgumentOutOfRangeException(nameof(fortificationPdPercent));
            if (flankStepPercent < 0 || flankFloorPercent < 0 || flankFloorPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(flankFloorPercent));
            if (maxExtraLanes < 0) throw new ArgumentOutOfRangeException(nameof(maxExtraLanes));
            if (lineHp < 1 || ewHp < 1 || supplyHp < 1 || cargoHp < 1) throw new ArgumentOutOfRangeException(nameof(lineHp));
            if (ammoPerLineShipMilli < 0 || batteryPerEwShipMilli < 0 || ventCoolantPerShipMilli < 0)
                throw new ArgumentOutOfRangeException(nameof(ammoPerLineShipMilli));
            if (tenderTransferPerSalvo < 0 || resupplyTarget < 0) throw new ArgumentOutOfRangeException(nameof(tenderTransferPerSalvo));
            if (overheatReleaseMilli >= overheatMilli || ambientMilli >= overheatReleaseMilli)
                throw new ArgumentOutOfRangeException(nameof(overheatReleaseMilli));
            if (fireHeatMilli < 0 || passiveCoolMilli < 0 || ventCoolMilli < 0) throw new ArgumentOutOfRangeException(nameof(fireHeatMilli));
            if (cookOffHullPercent < 0 || cookOffHullPercent > 100) throw new ArgumentOutOfRangeException(nameof(cookOffHullPercent));
            if (fuelPerShipPerSalvoMilli < 0) throw new ArgumentOutOfRangeException(nameof(fuelPerShipPerSalvoMilli));

            SalvoCycleTicks = salvoCycleTicks;
            LineAlpha = lineAlpha;
            EwBeta = ewBeta;
            ConservationRofPercent = conservationRofPercent;
            StandardRofPercent = standardRofPercent;
            OverdriveRofPercent = overdriveRofPercent;
            FortificationPdPercent = fortificationPdPercent;
            FlankStepPercent = flankStepPercent;
            FlankFloorPercent = flankFloorPercent;
            MaxExtraLanes = maxExtraLanes;
            LineHp = lineHp;
            EwHp = ewHp;
            SupplyHp = supplyHp;
            CargoHp = cargoHp;
            AmmoPerLineShipMilli = ammoPerLineShipMilli;
            BatteryPerEwShipMilli = batteryPerEwShipMilli;
            VentCoolantPerShipMilli = ventCoolantPerShipMilli;
            TenderTransferPerSalvo = tenderTransferPerSalvo;
            ResupplyTarget = resupplyTarget;
            AmbientMilli = ambientMilli;
            FireHeatMilli = fireHeatMilli;
            PassiveCoolMilli = passiveCoolMilli;
            VentCoolMilli = ventCoolMilli;
            OverheatMilli = overheatMilli;
            OverheatReleaseMilli = overheatReleaseMilli;
            CookOffHullPercent = cookOffHullPercent;
            FuelPerShipPerSalvoMilli = fuelPerShipPerSalvoMilli;
        }

        public int RofPercent(RofStance stance)
        {
            switch (stance)
            {
                case RofStance.Conservation50: return ConservationRofPercent;
                case RofStance.Standard100: return StandardRofPercent;
                case RofStance.Overdrive150: return OverdriveRofPercent;
                default: throw new ArgumentOutOfRangeException(nameof(stance), stance, null);
            }
        }

        public int Hp(TargetFocusClass shipClass)
        {
            switch (shipClass)
            {
                case TargetFocusClass.LineShips: return LineHp;
                case TargetFocusClass.EwShips: return EwHp;
                case TargetFocusClass.SupplyTenders: return SupplyHp;
                case TargetFocusClass.CargoConvoys: return CargoHp;
                default: throw new ArgumentOutOfRangeException(nameof(shipClass), shipClass, null);
            }
        }

        /// <summary>F_flank in percent: max(floor, 100 - step x extra lanes), extra lanes capped.</summary>
        public int FlankPercent(int extraLanes) =>
            Math.Max(FlankFloorPercent, 100 - FlankStepPercent * Math.Min(extraLanes, MaxExtraLanes));
    }
}
