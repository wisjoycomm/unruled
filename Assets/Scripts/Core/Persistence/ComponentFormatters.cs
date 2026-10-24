using Unruled.Core.AI;
using Unruled.Core.Board;
using Unruled.Core.Ecs;
using Unruled.Core.Economy;
using Unruled.Core.Galaxy;
using Unruled.Core.Military;
using Unruled.Core.Research;
using Unruled.Core.Simulation;
using Unruled.Core.SystemFactions;

namespace Unruled.Core.Persistence
{
    /// <summary>
    /// One explicit formatter per saved component (no reflection, AOT safe). Each component is a MessagePack array
    /// whose position is its key: keys are only ever appended. Keys an older save lacks read as the default; keys a
    /// newer build appended are skipped. Fixed-buffer components are encoded by hand at the end of the file.
    /// </summary>
    internal static class ComponentFormatters
    {
        internal static void Write(MsgPackWriter w, in SiteInfo v)
        {
            w.WriteArrayHeader(10);
            w.Write(v.ClusterId);
            w.Write(v.OrbitIndex);
            w.Write(v.PosX);
            w.Write(v.PosY);
            w.Write(v.TerrainSeed);
            w.Write(v.BaseEsiMilli);
            w.Write(v.CurrentEsiMilli);
            w.Write((int)v.LifecycleState);
            w.Write(v.OwnerPlayerId);
            w.Write(v.IrradiatedUntilTick);
        }

        internal static SiteInfo ReadSiteInfo(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SiteInfo();
            if (n > 0) v.ClusterId = r.ReadInt32();
            if (n > 1) v.OrbitIndex = r.ReadInt32();
            if (n > 2) v.PosX = r.ReadInt32();
            if (n > 3) v.PosY = r.ReadInt32();
            if (n > 4) v.TerrainSeed = r.ReadUInt64();
            if (n > 5) v.BaseEsiMilli = r.ReadInt32();
            if (n > 6) v.CurrentEsiMilli = r.ReadInt32();
            if (n > 7) v.LifecycleState = (PlanetLifecycleState)r.ReadByte();
            if (n > 8) v.OwnerPlayerId = r.ReadInt32();
            if (n > 9) v.IrradiatedUntilTick = r.ReadInt64();
            SkipNewer(r, n, 10);
            return v;
        }

        internal static void Write(MsgPackWriter w, in StarlaneEdge v)
        {
            w.WriteArrayHeader(4);
            WriteEntity(w, v.SiteA);
            WriteEntity(w, v.SiteB);
            w.Write((int)v.Tier);
            w.Write(v.Length);
        }

        internal static StarlaneEdge ReadStarlaneEdge(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new StarlaneEdge();
            if (n > 0) v.SiteA = ReadEntity(r);
            if (n > 1) v.SiteB = ReadEntity(r);
            if (n > 2) v.Tier = (StarlaneTier)r.ReadByte();
            if (n > 3) v.Length = r.ReadInt32();
            SkipNewer(r, n, 4);
            return v;
        }

        internal static void Write(MsgPackWriter w, in LaneWeather v)
        {
            w.WriteArrayHeader(3);
            w.Write((int)v.RadioBlackoutLevel);
            w.Write((int)v.SolarRadiationLevel);
            w.Write((int)v.GeomagneticStormLevel);
        }

        internal static LaneWeather ReadLaneWeather(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new LaneWeather();
            if (n > 0) v.RadioBlackoutLevel = r.ReadByte();
            if (n > 1) v.SolarRadiationLevel = r.ReadByte();
            if (n > 2) v.GeomagneticStormLevel = r.ReadByte();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in StormFront v)
        {
            w.WriteArrayHeader(7);
            w.Write(v.ClusterId);
            w.Write((int)v.Scale);
            w.Write((int)v.Level);
            w.Write(v.CenterX);
            w.Write(v.CenterY);
            w.Write(v.Radius);
            w.Write(v.EndTick);
        }

        internal static StormFront ReadStormFront(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new StormFront();
            if (n > 0) v.ClusterId = r.ReadInt32();
            if (n > 1) v.Scale = (StormScale)r.ReadByte();
            if (n > 2) v.Level = r.ReadByte();
            if (n > 3) v.CenterX = r.ReadInt32();
            if (n > 4) v.CenterY = r.ReadInt32();
            if (n > 5) v.Radius = r.ReadInt32();
            if (n > 6) v.EndTick = r.ReadInt64();
            SkipNewer(r, n, 7);
            return v;
        }

        internal static void Write(MsgPackWriter w, in SiteWeather v)
        {
            w.WriteArrayHeader(3);
            w.Write((int)v.RadioBlackoutLevel);
            w.Write((int)v.SolarRadiationLevel);
            w.Write((int)v.GeomagneticStormLevel);
        }

        internal static SiteWeather ReadSiteWeather(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SiteWeather();
            if (n > 0) v.RadioBlackoutLevel = r.ReadByte();
            if (n > 1) v.SolarRadiationLevel = r.ReadByte();
            if (n > 2) v.GeomagneticStormLevel = r.ReadByte();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in MineralDeposit v)
        {
            w.WriteArrayHeader(9);
            WriteEntity(w, v.Site);
            w.Write((int)v.RawItem);
            w.Write(v.SurfaceReserve);
            w.Write(v.DeepCrustReserve);
            w.Write(v.CoreFractureReserve);
            w.Write(v.IsCoreFractureUnlocked);
            w.Write(v.RigCount);
            w.Write(v.RetractUntilTick);
            w.Write(v.IsPaused);
        }

        internal static MineralDeposit ReadMineralDeposit(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new MineralDeposit();
            if (n > 0) v.Site = ReadEntity(r);
            if (n > 1) v.RawItem = (ItemId)r.ReadByte();
            if (n > 2) v.SurfaceReserve = r.ReadInt32();
            if (n > 3) v.DeepCrustReserve = r.ReadInt32();
            if (n > 4) v.CoreFractureReserve = r.ReadInt32();
            if (n > 5) v.IsCoreFractureUnlocked = r.ReadBool();
            if (n > 6) v.RigCount = r.ReadInt32();
            if (n > 7) v.RetractUntilTick = r.ReadInt64();
            if (n > 8) v.IsPaused = r.ReadBool();
            SkipNewer(r, n, 9);
            return v;
        }

        internal static void Write(MsgPackWriter w, in Refinery v)
        {
            w.WriteArrayHeader(5);
            WriteEntity(w, v.Site);
            w.Write((int)v.Output);
            w.Write((int)v.State);
            w.Write(v.AccruedUpkeepCents);
            w.Write(v.StockCap);
        }

        internal static Refinery ReadRefinery(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new Refinery();
            if (n > 0) v.Site = ReadEntity(r);
            if (n > 1) v.Output = (ItemId)r.ReadByte();
            if (n > 2) v.State = (RefineryState)r.ReadByte();
            if (n > 3) v.AccruedUpkeepCents = r.ReadInt64();
            if (n > 4) v.StockCap = r.ReadInt32();
            SkipNewer(r, n, 5);
            return v;
        }

        internal static void Write(MsgPackWriter w, in SiteQuarantine v)
        {
            w.WriteArrayHeader(2);
            w.Write(v.StarvedSinceTick);
            w.Write(v.QuarantineUntilTick);
        }

        internal static SiteQuarantine ReadSiteQuarantine(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SiteQuarantine();
            if (n > 0) v.StarvedSinceTick = r.ReadInt64();
            if (n > 1) v.QuarantineUntilTick = r.ReadInt64();
            SkipNewer(r, n, 2);
            return v;
        }

        internal static void Write(MsgPackWriter w, in PlayerHq v)
        {
            w.WriteArrayHeader(3);
            w.Write(v.PlayerId);
            WriteEntity(w, v.CoreBase);
            w.Write(v.IsCapitalConvoyInTransit);
        }

        internal static PlayerHq ReadPlayerHq(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new PlayerHq();
            if (n > 0) v.PlayerId = r.ReadInt32();
            if (n > 1) v.CoreBase = ReadEntity(r);
            if (n > 2) v.IsCapitalConvoyInTransit = r.ReadBool();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in RelayStation v)
        {
            w.WriteArrayHeader(1);
            w.Write(v.OwnerPlayerId);
        }

        internal static RelayStation ReadRelayStation(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new RelayStation();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            SkipNewer(r, n, 1);
            return v;
        }

        internal static void Write(MsgPackWriter w, in FleetUnit v)
        {
            w.WriteArrayHeader(6);
            w.Write(v.OwnerPlayerId);
            w.Write((int)v.TechTier);
            w.Write(v.LineShips);
            w.Write(v.EwShips);
            w.Write(v.SupplyTenders);
            w.Write(v.CargoConvoys);
        }

        internal static FleetUnit ReadFleetUnit(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new FleetUnit();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            if (n > 1) v.TechTier = r.ReadByte();
            if (n > 2) v.LineShips = r.ReadInt32();
            if (n > 3) v.EwShips = r.ReadInt32();
            if (n > 4) v.SupplyTenders = r.ReadInt32();
            if (n > 5) v.CargoConvoys = r.ReadInt32();
            SkipNewer(r, n, 6);
            return v;
        }

        internal static void Write(MsgPackWriter w, in FleetMotion v)
        {
            w.WriteArrayHeader(7);
            WriteEntity(w, v.Site);
            WriteEntity(w, v.Lane);
            WriteEntity(w, v.FromSite);
            w.Write(v.TravelledMilli);
            WriteEntity(w, v.ArrivalLane);
            w.Write(v.IsHolding);
            w.Write(v.IsReversing);
        }

        internal static FleetMotion ReadFleetMotion(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new FleetMotion();
            if (n > 0) v.Site = ReadEntity(r);
            if (n > 1) v.Lane = ReadEntity(r);
            if (n > 2) v.FromSite = ReadEntity(r);
            if (n > 3) v.TravelledMilli = r.ReadInt64();
            if (n > 4) v.ArrivalLane = ReadEntity(r);
            if (n > 5) v.IsHolding = r.ReadBool();
            if (n > 6) v.IsReversing = r.ReadBool();
            SkipNewer(r, n, 7);
            return v;
        }

        internal static void Write(MsgPackWriter w, in FleetSensors v)
        {
            w.WriteArrayHeader(2);
            w.Write(v.RevealedUntilTick);
            w.Write(v.RwrWarningTick);
        }

        internal static FleetSensors ReadFleetSensors(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new FleetSensors();
            if (n > 0) v.RevealedUntilTick = r.ReadInt64();
            if (n > 1) v.RwrWarningTick = r.ReadInt64();
            SkipNewer(r, n, 2);
            return v;
        }

        internal static void Write(MsgPackWriter w, in FleetReadiness v)
        {
            w.WriteArrayHeader(3);
            w.Write(v.IsMothballed);
            w.Write(v.WarmupUntilTick);
            w.Write(v.IsUnsupplied);
        }

        internal static FleetReadiness ReadFleetReadiness(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new FleetReadiness();
            if (n > 0) v.IsMothballed = r.ReadBool();
            if (n > 1) v.WarmupUntilTick = r.ReadInt64();
            if (n > 2) v.IsUnsupplied = r.ReadBool();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in FleetCombatState v)
        {
            w.WriteArrayHeader(13);
            w.Write((int)v.Focus);
            w.Write((int)v.Rof);
            w.Write(v.TemperatureMilli);
            w.Write(v.IsOverheated);
            w.Write(v.IsVenting);
            w.Write(v.EngagedUntilTick);
            w.Write(v.FiredAtTick);
            w.Write(v.DamageCarry);
            w.Write(v.AmmoDebtMilli);
            w.Write(v.BatteryDebtMilli);
            w.Write(v.CoolantDebtMilli);
            w.Write(v.FuelDebtMilli);
            w.Write(v.RaidsConvoys);
        }

        internal static FleetCombatState ReadFleetCombatState(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new FleetCombatState();
            if (n > 0) v.Focus = (TargetFocusClass)r.ReadByte();
            if (n > 1) v.Rof = (RofStance)r.ReadByte();
            if (n > 2) v.TemperatureMilli = r.ReadInt32();
            if (n > 3) v.IsOverheated = r.ReadBool();
            if (n > 4) v.IsVenting = r.ReadBool();
            if (n > 5) v.EngagedUntilTick = r.ReadInt64();
            if (n > 6) v.FiredAtTick = r.ReadInt64();
            if (n > 7) v.DamageCarry = r.ReadInt64();
            if (n > 8) v.AmmoDebtMilli = r.ReadInt32();
            if (n > 9) v.BatteryDebtMilli = r.ReadInt32();
            if (n > 10) v.CoolantDebtMilli = r.ReadInt32();
            if (n > 11) v.FuelDebtMilli = r.ReadInt32();
            if (n > 12) v.RaidsConvoys = r.ReadBool();
            SkipNewer(r, n, 13);
            return v;
        }

        internal static void Write(MsgPackWriter w, in CapitalRelocation v)
        {
            w.WriteArrayHeader(7);
            w.Write((int)v.Phase);
            w.Write(v.PhaseEndTick);
            WriteEntity(w, v.OldBase);
            WriteEntity(w, v.Destination);
            WriteEntity(w, v.Convoy);
            w.Write(v.CooldownUntilTick);
            w.Write(v.ParalysedUntilTick);
        }

        internal static CapitalRelocation ReadCapitalRelocation(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new CapitalRelocation();
            if (n > 0) v.Phase = (RelocationPhase)r.ReadByte();
            if (n > 1) v.PhaseEndTick = r.ReadInt64();
            if (n > 2) v.OldBase = ReadEntity(r);
            if (n > 3) v.Destination = ReadEntity(r);
            if (n > 4) v.Convoy = ReadEntity(r);
            if (n > 5) v.CooldownUntilTick = r.ReadInt64();
            if (n > 6) v.ParalysedUntilTick = r.ReadInt64();
            SkipNewer(r, n, 7);
            return v;
        }

        internal static void Write(MsgPackWriter w, in RadarScan v)
        {
            w.WriteArrayHeader(5);
            w.Write(v.OwnerPlayerId);
            WriteEntity(w, v.Lane);
            w.Write(v.StartTick);
            w.Write(v.IntegrationTicks);
            w.Write(v.RwrFired);
        }

        internal static RadarScan ReadRadarScan(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new RadarScan();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            if (n > 1) v.Lane = ReadEntity(r);
            if (n > 2) v.StartTick = r.ReadInt64();
            if (n > 3) v.IntegrationTicks = r.ReadInt32();
            if (n > 4) v.RwrFired = r.ReadBool();
            SkipNewer(r, n, 5);
            return v;
        }

        internal static void Write(MsgPackWriter w, in Wallet v)
        {
            w.WriteArrayHeader(3);
            w.Write(v.OwnerPlayerId);
            w.Write((int)v.Pool);
            w.Write(v.Cents);
        }

        internal static Wallet ReadWallet(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new Wallet();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            if (n > 1) v.Pool = (TaxPoolKind)r.ReadByte();
            if (n > 2) v.Cents = r.ReadInt64();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in StarSystem v)
        {
            w.WriteArrayHeader(7);
            w.Write(v.StarId);
            w.Write(v.ClusterId);
            w.Write(v.OwnerPlayerId);
            w.Write(v.MaterialisedTick);
            w.Write(v.X);
            w.Write(v.Y);
            w.Write((int)v.Class);
        }

        internal static StarSystem ReadStarSystem(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new StarSystem();
            if (n > 0) v.StarId = r.ReadInt64();
            if (n > 1) v.ClusterId = r.ReadInt32();
            if (n > 2) v.OwnerPlayerId = r.ReadInt32();
            if (n > 3) v.MaterialisedTick = r.ReadInt64();
            if (n > 4) v.X = r.ReadInt32();
            if (n > 5) v.Y = r.ReadInt32();
            if (n > 6) v.Class = (StarClass)r.ReadByte();
            SkipNewer(r, n, 7);
            return v;
        }

        // SystemName: [chars as ints, renamed by, renamed tick].
        internal static void Write(MsgPackWriter w, in SystemName v)
        {
            w.WriteArrayHeader(3);
            w.WriteArrayHeader(v.Length);
            for (int i = 0; i < v.Length; i++)
                w.Write((int)v.CharAt(i));
            w.Write(v.RenamedByPlayerId);
            w.Write(v.RenamedTick);
        }

        internal static SystemName ReadSystemName(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SystemName();
            if (n > 0)
            {
                int chars = r.ReadArrayHeader();
                if (chars > SystemName.Capacity) throw new System.FormatException("System name too long.");
                for (int i = 0; i < chars; i++)
                    v.SetChar(i, (char)r.ReadInt32());
                v.Length = chars;
            }
            if (n > 1) v.RenamedByPlayerId = r.ReadInt32();
            if (n > 2) v.RenamedTick = r.ReadInt64();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in Loan v)
        {
            w.WriteArrayHeader(3);
            w.Write(v.PrincipalCents);
            w.Write(v.InterestCents);
            w.Write(v.DueTick);
        }

        internal static Loan ReadLoan(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new Loan();
            if (n > 0) v.PrincipalCents = r.ReadInt64();
            if (n > 1) v.InterestCents = r.ReadInt64();
            if (n > 2) v.DueTick = r.ReadInt64();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in PlayerStanding v)
        {
            w.WriteArrayHeader(4);
            w.Write((int)v.Bracket);
            w.Write(v.Rank);
            w.Write(v.IsAudited);
            w.Write(v.IsEvading);
        }

        internal static PlayerStanding ReadPlayerStanding(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new PlayerStanding();
            if (n > 0) v.Bracket = r.ReadByte();
            if (n > 1) v.Rank = r.ReadInt32();
            if (n > 2) v.IsAudited = r.ReadBool();
            if (n > 3) v.IsEvading = r.ReadBool();
            SkipNewer(r, n, 4);
            return v;
        }

        internal static void Write(MsgPackWriter w, in WtoOrder v)
        {
            w.WriteArrayHeader(8);
            w.Write(v.OwnerPlayerId);
            w.Write((int)v.Item);
            w.Write((int)v.Side);
            w.Write(v.PriceCents);
            w.Write(v.Remaining);
            WriteEntity(w, v.Site);
            w.Write(v.PlacedTick);
            w.Write(v.DepositCents);
        }

        internal static WtoOrder ReadWtoOrder(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new WtoOrder();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            if (n > 1) v.Item = (ItemId)r.ReadByte();
            if (n > 2) v.Side = (OrderSide)r.ReadByte();
            if (n > 3) v.PriceCents = r.ReadInt64();
            if (n > 4) v.Remaining = r.ReadInt32();
            if (n > 5) v.Site = ReadEntity(r);
            if (n > 6) v.PlacedTick = r.ReadInt64();
            if (n > 7) v.DepositCents = r.ReadInt64();
            SkipNewer(r, n, 8);
            return v;
        }

        internal static void Write(MsgPackWriter w, in WtoContract v)
        {
            w.WriteArrayHeader(9);
            w.Write(v.BuyerPlayerId);
            w.Write(v.SellerPlayerId);
            w.Write((int)v.Item);
            w.Write(v.Quantity);
            w.Write(v.PriceCents);
            w.Write(v.DepositCents);
            WriteEntity(w, v.BuyerSite);
            WriteEntity(w, v.Convoy);
            w.Write(v.DeadlineTick);
        }

        internal static WtoContract ReadWtoContract(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new WtoContract();
            if (n > 0) v.BuyerPlayerId = r.ReadInt32();
            if (n > 1) v.SellerPlayerId = r.ReadInt32();
            if (n > 2) v.Item = (ItemId)r.ReadByte();
            if (n > 3) v.Quantity = r.ReadInt32();
            if (n > 4) v.PriceCents = r.ReadInt64();
            if (n > 5) v.DepositCents = r.ReadInt64();
            if (n > 6) v.BuyerSite = ReadEntity(r);
            if (n > 7) v.Convoy = ReadEntity(r);
            if (n > 8) v.DeadlineTick = r.ReadInt64();
            SkipNewer(r, n, 9);
            return v;
        }

        internal static void Write(MsgPackWriter w, in ContractCargo v)
        {
            w.WriteArrayHeader(2);
            w.Write((int)v.Item);
            w.Write(v.Quantity);
        }

        internal static ContractCargo ReadContractCargo(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new ContractCargo();
            if (n > 0) v.Item = (ItemId)r.ReadByte();
            if (n > 1) v.Quantity = r.ReadInt32();
            SkipNewer(r, n, 2);
            return v;
        }

        internal static void Write(MsgPackWriter w, in SystemSanction v)
        {
            w.WriteArrayHeader(6);
            w.Write(v.Offences);
            w.Write(v.LastOffenceTick);
            w.Write(v.WtoBarredUntilTick);
            w.Write(v.DefencePending);
            WriteEntity(w, v.DefenceFleet);
            w.Write(v.DefenceUntilTick);
        }

        internal static SystemSanction ReadSystemSanction(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SystemSanction();
            if (n > 0) v.Offences = r.ReadInt32();
            if (n > 1) v.LastOffenceTick = r.ReadInt64();
            if (n > 2) v.WtoBarredUntilTick = r.ReadInt64();
            if (n > 3) v.DefencePending = r.ReadBool();
            if (n > 4) v.DefenceFleet = ReadEntity(r);
            if (n > 5) v.DefenceUntilTick = r.ReadInt64();
            SkipNewer(r, n, 6);
            return v;
        }

        internal static void Write(MsgPackWriter w, in SystemBulletin v)
        {
            w.WriteArrayHeader(6);
            w.Write((int)v.Agency);
            w.Write((int)v.Kind);
            w.Write(v.PlayerId);
            WriteEntity(w, v.Site);
            w.Write(v.Tick);
            w.Write(v.ExpiresTick);
        }

        internal static SystemBulletin ReadSystemBulletin(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SystemBulletin();
            if (n > 0) v.Agency = (SystemAgency)r.ReadByte();
            if (n > 1) v.Kind = (BulletinKind)r.ReadByte();
            if (n > 2) v.PlayerId = r.ReadInt32();
            if (n > 3) v.Site = ReadEntity(r);
            if (n > 4) v.Tick = r.ReadInt64();
            if (n > 5) v.ExpiresTick = r.ReadInt64();
            SkipNewer(r, n, 6);
            return v;
        }

        internal static void Write(MsgPackWriter w, in AiController v)
        {
            w.WriteArrayHeader(1);
            w.Write((int)v.Archetype);
        }

        internal static AiController ReadAiController(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new AiController();
            if (n > 0) v.Archetype = r.ReadByte();
            SkipNewer(r, n, 1);
            return v;
        }

        internal static void Write(MsgPackWriter w, in TechProgress v)
        {
            w.WriteArrayHeader(10);
            w.Write((int)v.Tier(ResearchBranch.Military));
            w.Write((int)v.Stage);
            w.Write(v.TelemetryMilli);
            w.Write(v.FailedTests);
            w.Write(v.RetoolUntilTick);
            WriteEntity(w, v.LastTelemetryFleet);
            w.Write((int)v.Tier(ResearchBranch.Industry));
            w.Write((int)v.Tier(ResearchBranch.Logistics));
            w.Write((int)v.Tier(ResearchBranch.Signals));
            w.Write((int)v.ActiveBranch);
        }

        internal static TechProgress ReadTechProgress(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new TechProgress();
            if (n > 0) v.TechTier = r.ReadByte();
            if (n > 1) v.Stage = (ResearchStage)r.ReadByte();
            if (n > 2) v.TelemetryMilli = r.ReadInt32();
            if (n > 3) v.FailedTests = r.ReadInt32();
            if (n > 4) v.RetoolUntilTick = r.ReadInt64();
            if (n > 5) v.LastTelemetryFleet = ReadEntity(r);
            v.IndustryTier = n > 6 ? r.ReadByte() : v.TechTier;
            v.LogisticsTier = n > 7 ? r.ReadByte() : v.TechTier;
            v.SignalsTier = n > 8 ? r.ReadByte() : v.TechTier;
            if (n > 9) v.ActiveBranch = (ResearchBranch)r.ReadByte();
            SkipNewer(r, n, 10);
            return v;
        }

        internal static void Write(MsgPackWriter w, in ShipBuildOrder v)
        {
            w.WriteArrayHeader(6);
            w.Write(v.OwnerPlayerId);
            WriteEntity(w, v.Site);
            w.Write((int)v.ShipClass);
            w.Write(v.Count);
            w.Write((int)v.TechTier);
            w.Write(v.DoneTick);
        }

        internal static ShipBuildOrder ReadShipBuildOrder(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new ShipBuildOrder();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            if (n > 1) v.Site = ReadEntity(r);
            if (n > 2) v.ShipClass = (TargetFocusClass)r.ReadByte();
            if (n > 3) v.Count = r.ReadInt32();
            if (n > 4) v.TechTier = r.ReadByte();
            if (n > 5) v.DoneTick = r.ReadInt64();
            SkipNewer(r, n, 6);
            return v;
        }

        internal static void Write(MsgPackWriter w, in JumpGate v)
        {
            w.WriteArrayHeader(5);
            w.Write(v.OwnerPlayerId);
            WriteEntity(w, v.HomeSite);
            WriteEntity(w, v.FarSite);
            WriteEntity(w, v.Lane);
            w.Write(v.OpenedAtTick);
        }

        internal static JumpGate ReadJumpGate(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new JumpGate();
            if (n > 0) v.OwnerPlayerId = r.ReadInt32();
            if (n > 1) v.HomeSite = ReadEntity(r);
            if (n > 2) v.FarSite = ReadEntity(r);
            if (n > 3) v.Lane = ReadEntity(r);
            if (n > 4) v.OpenedAtTick = r.ReadInt64();
            SkipNewer(r, n, 5);
            return v;
        }

        internal static void Write(MsgPackWriter w, in ConvoyRoute v)
        {
            w.WriteArrayHeader(3);
            WriteEntity(w, v.Hub);
            WriteEntity(w, v.Outpost);
            w.Write(v.Batch);
        }

        internal static ConvoyRoute ReadConvoyRoute(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new ConvoyRoute();
            if (n > 0) v.Hub = ReadEntity(r);
            if (n > 1) v.Outpost = ReadEntity(r);
            if (n > 2) v.Batch = r.ReadInt32();
            SkipNewer(r, n, 3);
            return v;
        }

        internal static void Write(MsgPackWriter w, in SystemCitadel v)
        {
            w.WriteArrayHeader(6);
            w.Write(v.Integrity);
            w.Write((int)v.DefenceTier);
            WriteEntity(w, v.Garrison);
            w.Write(v.GarrisonDueTick);
            w.Write(v.LastHitTick);
            w.Write(v.FallenTick);
        }

        internal static SystemCitadel ReadSystemCitadel(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new SystemCitadel();
            if (n > 0) v.Integrity = r.ReadInt32();
            if (n > 1) v.DefenceTier = r.ReadByte();
            if (n > 2) v.Garrison = ReadEntity(r);
            if (n > 3) v.GarrisonDueTick = r.ReadInt64();
            if (n > 4) v.LastHitTick = r.ReadInt64();
            if (n > 5) v.FallenTick = r.ReadInt64();
            SkipNewer(r, n, 6);
            return v;
        }

        internal static void Write(MsgPackWriter w, in CitadelAssault v)
        {
            w.WriteArrayHeader(2);
            w.Write(v.Damage);
            w.Write(v.LastHitTick);
        }

        internal static CitadelAssault ReadCitadelAssault(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new CitadelAssault();
            if (n > 0) v.Damage = r.ReadInt64();
            if (n > 1) v.LastHitTick = r.ReadInt64();
            SkipNewer(r, n, 2);
            return v;
        }

        internal static void Write(MsgPackWriter w, in VictoryRecord v)
        {
            w.WriteArrayHeader(2);
            w.Write(v.DecidedTick);
            w.Write(v.Seats);
        }

        internal static VictoryRecord ReadVictoryRecord(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new VictoryRecord();
            if (n > 0) v.DecidedTick = r.ReadInt64();
            if (n > 1) v.Seats = r.ReadInt32();
            SkipNewer(r, n, 2);
            return v;
        }

        internal static void Write(MsgPackWriter w, in DirectorateSeat v)
        {
            w.WriteArrayHeader(1);
            w.Write(v.SinceTick);
        }

        internal static DirectorateSeat ReadDirectorateSeat(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new DirectorateSeat();
            if (n > 0) v.SinceTick = r.ReadInt64();
            SkipNewer(r, n, 1);
            return v;
        }

        // Stockpile and StolenGoods: [quantity per item slot].
        internal static void Write(MsgPackWriter w, in Stockpile v)
        {
            w.WriteArrayHeader(ItemIds.Count);
            for (int slot = 0; slot < ItemIds.Count; slot++)
                w.Write(v.Get(ItemIds.FromSlot(slot)));
        }

        internal static Stockpile ReadStockpile(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new Stockpile();
            for (int slot = 0; slot < n; slot++)
            {
                int quantity = r.ReadInt32();
                if (slot >= ItemIds.Count)
                    throw new System.FormatException($"Stockpile has {n} item slots; this build knows {ItemIds.Count}.");
                v.Add(ItemIds.FromSlot(slot), quantity);
            }
            return v;
        }

        internal static void Write(MsgPackWriter w, in StolenGoods v)
        {
            w.WriteArrayHeader(ItemIds.Count);
            for (int slot = 0; slot < ItemIds.Count; slot++)
                w.Write(v.Get(ItemIds.FromSlot(slot)));
        }

        internal static StolenGoods ReadStolenGoods(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new StolenGoods();
            for (int slot = 0; slot < n; slot++)
            {
                int quantity = r.ReadInt32();
                if (slot >= ItemIds.Count)
                    throw new System.FormatException($"Stolen goods have {n} item slots; this build knows {ItemIds.Count}.");
                v.Add(ItemIds.FromSlot(slot), quantity);
            }
            return v;
        }

        // FleetRoute: [lanes [entity...], next].
        internal static void Write(MsgPackWriter w, in FleetRoute v)
        {
            w.WriteArrayHeader(2);
            w.WriteArrayHeader(v.Count);
            for (int i = 0; i < v.Count; i++)
                WriteEntity(w, v.LaneAt(i));
            w.Write(v.Next);
        }

        internal static FleetRoute ReadFleetRoute(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new FleetRoute();
            if (n > 0)
            {
                int lanes = r.ReadArrayHeader();
                for (int i = 0; i < lanes; i++)
                    v.Append(ReadEntity(r));
            }
            if (n > 1) v.Next = r.ReadInt32();
            SkipNewer(r, n, 2);
            return v;
        }

        // ItemMarket: [item, last VWAP, ring [[slot, tick, value, volume]...]] with only the slots ever written.
        internal static void Write(MsgPackWriter w, in ItemMarket v)
        {
            w.WriteArrayHeader(3);
            w.Write((int)v.Item);
            w.Write(v.LastVwapCents);
            int used = 0;
            for (int i = 0; i < ItemMarket.MaxWindowTicks; i++)
            {
                v.SlotAt(i, out long tick, out long value, out long volume);
                if (tick != 0 || value != 0 || volume != 0) used++;
            }
            w.WriteArrayHeader(used);
            for (int i = 0; i < ItemMarket.MaxWindowTicks; i++)
            {
                v.SlotAt(i, out long tick, out long value, out long volume);
                if (tick == 0 && value == 0 && volume == 0) continue;
                w.WriteArrayHeader(4);
                w.Write(i);
                w.Write(tick);
                w.Write(value);
                w.Write(volume);
            }
        }

        internal static ItemMarket ReadItemMarket(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new ItemMarket();
            if (n > 0) v.Item = (ItemId)r.ReadByte();
            if (n > 1) v.LastVwapCents = r.ReadInt64();
            if (n > 2)
            {
                int used = r.ReadArrayHeader();
                for (int k = 0; k < used; k++)
                {
                    int fields = r.ReadArrayHeader();
                    if (fields < 4) throw new System.FormatException("Market slot needs 4 values.");
                    v.RestoreSlot(r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadInt64());
                    SkipNewer(r, fields, 4);
                }
            }
            SkipNewer(r, n, 3);
            return v;
        }

        // DelayedPlayerCommand: queued commands.
        internal static void Write(MsgPackWriter w, in DelayedPlayerCommand v)
        {
            w.WriteArrayHeader(9);
            w.Write(v.IssuedAtTick);
            w.Write(v.ExecuteAtTick);
            w.Write(v.PlayerId);
            w.Write((int)v.CommandType);
            w.Write(v.TargetEntityId);
            w.Write(v.TargetGeneration);
            w.Write(v.ParamA);
            w.Write(v.ParamB);
            w.Write(v.ParamC);
        }

        internal static DelayedPlayerCommand ReadCommand(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            var v = new DelayedPlayerCommand();
            if (n > 0) v.IssuedAtTick = r.ReadInt64();
            if (n > 1) v.ExecuteAtTick = r.ReadInt64();
            if (n > 2) v.PlayerId = r.ReadInt32();
            if (n > 3) v.CommandType = (CommandType)r.ReadByte();
            if (n > 4) v.TargetEntityId = r.ReadInt32();
            if (n > 5) v.TargetGeneration = r.ReadInt32();
            if (n > 6) v.ParamA = r.ReadInt32();
            if (n > 7) v.ParamB = r.ReadInt32();
            if (n > 8) v.ParamC = r.ReadInt32();
            SkipNewer(r, n, 9);
            return v;
        }

        // EntityId: [index, generation].
        internal static void WriteEntity(MsgPackWriter w, EntityId id)
        {
            w.WriteArrayHeader(2);
            w.Write(id.Index);
            w.Write(id.Generation);
        }

        internal static EntityId ReadEntity(MsgPackReader r)
        {
            int n = r.ReadArrayHeader();
            if (n < 2) throw new System.FormatException("Entity id needs index and generation.");
            var id = new EntityId(r.ReadInt32(), r.ReadInt32());
            SkipNewer(r, n, 2);
            return id;
        }

        private static void SkipNewer(MsgPackReader r, int count, int known)
        {
            if (count > known) r.Skip(count - known);
        }
    }
}
