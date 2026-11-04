using System;
using System.Linq;
using CairoDesktop.Features.MonitorProfiles;
using Xunit;

namespace CairoDesktop.Features.Tests.MonitorProfiles
{
    public class TopologyDiffTests
    {
        [Fact]
        public void Identical_IsEmpty()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Fhd(), Mon.Ultra()), Mon.Set(Mon.Fhd(), Mon.Ultra()));
            Assert.True(d.IsEmpty);
            Assert.Equal("no change", d.ToString());
            Assert.Empty(d.AffectedIds);
        }

        [Fact]
        public void NullLists_AreTreatedAsEmpty()
        {
            Assert.True(MonitorTopologyDiff.Compare(null, null).IsEmpty);
            var d = MonitorTopologyDiff.Compare(null, Mon.Set(Mon.Fhd()));
            Assert.Single(d.Added);
            Assert.True(d.PrimaryChanged);
        }

        [Fact]
        public void HotPlug_Add()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Fhd()), Mon.Set(Mon.Fhd(), Mon.Ultra()));
            Assert.Single(d.Added);
            Assert.Equal(@"\\.\DISPLAY2", d.Added[0].DeviceName);
            Assert.Empty(d.Removed);
            Assert.False(d.PrimaryChanged);
            Assert.Equal(new[] { @"\\.\DISPLAY2" }, d.AffectedIds);
        }

        [Fact]
        public void HotPlug_Remove()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Fhd(), Mon.Ultra()), Mon.Set(Mon.Fhd()));
            Assert.Single(d.Removed);
            Assert.Equal(new[] { @"\\.\DISPLAY2" }, d.RemovedIds);
            Assert.Empty(d.AffectedIds);
            Assert.False(d.IsEmpty);
        }

        [Fact]
        public void BoundsChange_IsMoved()
        {
            var before = Mon.Set(Mon.Fhd(), Mon.Ultra());
            var after = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", -3440, 0, 3440, 1440));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.Single(d.Moved);
            Assert.Equal(1920, d.Moved[0].Before.Bounds.Left);
            Assert.Equal(-3440, d.Moved[0].After.Bounds.Left);
            Assert.Empty(d.Added);
            Assert.Empty(d.Removed);
        }

        [Fact]
        public void ResolutionChange_IsMoved_NotAddRemove()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Fhd()), Mon.Set(Mon.Make(@"\\.\DISPLAY1", 0, 0, 2560, 1440, true)));
            Assert.Single(d.Moved);
            Assert.Empty(d.Added);
            Assert.False(d.PrimaryChanged);
        }

        [Fact]
        public void DpiChange_Detected()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Fhd()), Mon.Set(Mon.Make(@"\\.\DISPLAY1", 0, 0, 1920, 1080, true, 1.5)));
            Assert.Single(d.DpiChanged);
            Assert.Empty(d.Moved);
            Assert.Equal(new[] { @"\\.\DISPLAY1" }, d.AffectedIds);
        }

        [Fact]
        public void TinyDpiNoise_IsIgnored()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Make("A", 0, 0, 10, 10, true, 1.0)), Mon.Set(Mon.Make("A", 0, 0, 10, 10, true, 1.0004)));
            Assert.True(d.IsEmpty);
        }

        [Fact]
        public void WorkAreaOnlyChange_IsNotReported()
        {
            var a = Mon.Fhd();
            var b = Mon.Fhd();
            b.WorkArea = new MonitorRect(0, 0, 1920, 900);
            Assert.True(MonitorTopologyDiff.Compare(Mon.Set(a), Mon.Set(b)).IsEmpty);
        }

        [Fact]
        public void PrimarySwap_SameNames_ReportsPrimaryChangedAndBothAffected()
        {
            var before = Mon.Set(Mon.Make("A", 0, 0, 1920, 1080, true), Mon.Make("B", 1920, 0, 1920, 1080));
            var after = Mon.Set(Mon.Make("A", 0, 0, 1920, 1080), Mon.Make("B", 1920, 0, 1920, 1080, true));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.True(d.PrimaryChanged);
            Assert.Equal("A", d.OldPrimary.DeviceName);
            Assert.Equal("B", d.NewPrimary.DeviceName);
            Assert.Equal(new[] { "B", "A" }, d.AffectedIds);
            Assert.Empty(d.Added);
            Assert.Empty(d.Moved);
        }

        [Fact]
        public void PrimaryUnplugged_ReportsRemovedAndPrimaryChanged()
        {
            var before = Mon.Set(Mon.Make("A", 0, 0, 1920, 1080, true), Mon.Make("B", 1920, 0, 1920, 1080));
            var after = Mon.Set(Mon.Make("B", 0, 0, 1920, 1080, true));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.True(d.PrimaryChanged);
            Assert.Single(d.Removed);
            Assert.Equal("A", d.Removed[0].DeviceName);
            Assert.Single(d.Moved);          // B now sits at 0,0
            Assert.Contains("B", d.AffectedIds);
        }

        [Fact]
        public void ReusedDeviceName_DifferentBounds_IsTreatedAsSameMonitorMoved()
        {
            var before = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 1280, 1024));
            var after = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 3440, 1440));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.Single(d.Moved);
            Assert.Empty(d.Added);
            Assert.Empty(d.Removed);
        }

        [Fact]
        public void ReusedDeviceName_DifferentHardware_IsRemoveAndAdd()
        {
            var before = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 1280, 1024, false, 1.0, "DELL1"));
            var after = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 3440, 1440, false, 1.0, "LG9"));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.Single(d.Added);
            Assert.Single(d.Removed);
            Assert.Empty(d.Moved);
        }

        [Fact]
        public void SameHardware_NameReuse_StillSameMonitor()
        {
            var before = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 1280, 1024, false, 1.0, "DELL1"));
            var after = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 1280, 1024, false, 1.0, "DELL1"));
            Assert.True(MonitorTopologyDiff.Compare(before, after).IsEmpty);
        }

        [Fact]
        public void RenumberedMonitor_SameBounds_IsRenamedNotAddRemove()
        {
            var before = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY2", 1920, 0, 2560, 1440));
            var after = Mon.Set(Mon.Fhd(), Mon.Make(@"\\.\DISPLAY3", 1920, 0, 2560, 1440));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.Single(d.Renamed);
            Assert.Equal(@"\\.\DISPLAY2", d.Renamed[0].Before.DeviceName);
            Assert.Equal(@"\\.\DISPLAY3", d.Renamed[0].After.DeviceName);
            Assert.Empty(d.Added);
            Assert.Empty(d.Removed);
            Assert.Equal(new[] { @"\\.\DISPLAY3" }, d.AffectedIds);
        }

        [Fact]
        public void NameMatchIsCaseInsensitive()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Make(@"\\.\display1", 0, 0, 10, 10, true)), Mon.Set(Mon.Make(@"\\.\DISPLAY1", 0, 0, 10, 10, true)));
            Assert.True(d.IsEmpty);
        }

        [Fact]
        public void Dock_ThreeMonitorsAppearAtOnce()
        {
            var d = MonitorTopologyDiff.Compare(Mon.Set(Mon.Make("L", 0, 0, 1920, 1080, true)), Mon.Set(Mon.Make("L", 0, 0, 1920, 1080, true), Mon.Make("D1", 1920, 0, 1920, 1080), Mon.Make("D2", 3840, 0, 1920, 1080), Mon.Make("D3", 5760, 0, 1920, 1080)));
            Assert.Equal(3, d.Added.Count);
            Assert.Equal(3, d.AffectedIds.Count);
        }

        [Fact]
        public void LaptopLidClosed_PrimaryMovesToExternal()
        {
            var before = Mon.Set(Mon.Make("LAP", 0, 0, 1920, 1080, true), Mon.Make("EXT", 1920, 0, 2560, 1440));
            var after = Mon.Set(Mon.Make("EXT", 0, 0, 2560, 1440, true));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.Equal(new[] { "LAP" }, d.RemovedIds);
            Assert.True(d.PrimaryChanged);
            Assert.Equal("EXT", d.NewPrimary.DeviceName);
        }

        [Fact]
        public void DuplicateNamesInSnapshot_AreDeduplicated()
        {
            var dup = new[] { Mon.Make("A", 0, 0, 10, 10, true), Mon.Make("A", 20, 0, 10, 10) };
            var d = MonitorTopologyDiff.Compare(dup, dup);
            Assert.True(d.IsEmpty);
        }

        [Fact]
        public void AffectedIds_NoDuplicates()
        {
            var before = Mon.Set(Mon.Make("A", 0, 0, 100, 100, true), Mon.Make("B", 100, 0, 100, 100));
            var after = Mon.Set(Mon.Make("A", 0, 0, 100, 100), Mon.Make("B", 0, 100, 100, 100, true, 2.0));
            var d = MonitorTopologyDiff.Compare(before, after);
            Assert.Equal(d.AffectedIds.Count, d.AffectedIds.Distinct().Count());
        }
    }

    public class DebouncerTests
    {
        private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);
        private static ChangeDebouncer Make() => new ChangeDebouncer(Ms(300), Ms(1000));

        [Fact]
        public void NothingPending_NeverFires()
        {
            var d = Make();
            Assert.False(d.ShouldFire(Ms(5000)));
            Assert.Null(d.TimeUntilFire(Ms(0)));
            Assert.False(d.IsPending);
        }

        [Fact]
        public void SingleNotification_FiresAfterQuietPeriod()
        {
            var d = Make();
            d.Notify(Ms(0));
            Assert.False(d.ShouldFire(Ms(299)));
            Assert.True(d.ShouldFire(Ms(300)));
        }

        [Fact]
        public void FiresOnlyOncePerBurst()
        {
            var d = Make();
            d.Notify(Ms(0));
            Assert.True(d.ShouldFire(Ms(400)));
            Assert.False(d.ShouldFire(Ms(500)));
            Assert.False(d.IsPending);
        }

        [Fact]
        public void Burst_ExtendsQuietWindow()
        {
            var d = Make();
            d.Notify(Ms(0));
            d.Notify(Ms(200));
            d.Notify(Ms(400));
            Assert.False(d.ShouldFire(Ms(600)));
            Assert.True(d.ShouldFire(Ms(700)));
        }

        [Fact]
        public void ContinuousStorm_FiresAtMaxWait()
        {
            var d = Make();
            for (int t = 0; t <= 900; t += 100) d.Notify(Ms(t));
            Assert.False(d.ShouldFire(Ms(900)));
            d.Notify(Ms(1000));
            Assert.True(d.ShouldFire(Ms(1000)));
        }

        [Fact]
        public void AfterFiring_NewBurstStartsFresh()
        {
            var d = Make();
            d.Notify(Ms(0));
            Assert.True(d.ShouldFire(Ms(300)));
            d.Notify(Ms(5000));
            Assert.False(d.ShouldFire(Ms(5200)));
            Assert.True(d.ShouldFire(Ms(5300)));
        }

        [Fact]
        public void ClockRegression_InNotify_ReanchorsBurst()
        {
            var d = Make();
            d.Notify(Ms(10_000));
            d.Notify(Ms(100));              // clock went backwards
            Assert.False(d.ShouldFire(Ms(399)));
            Assert.True(d.ShouldFire(Ms(400)));
        }

        [Fact]
        public void ClockRegression_InShouldFire_DoesNotFireButRecovers()
        {
            var d = Make();
            d.Notify(Ms(10_000));
            Assert.False(d.ShouldFire(Ms(50)));       // regression: re-anchored at 50
            Assert.True(d.IsPending);
            Assert.False(d.ShouldFire(Ms(349)));
            Assert.True(d.ShouldFire(Ms(350)));
        }

        [Fact]
        public void TimeUntilFire_CountsDownAndClamps()
        {
            var d = Make();
            d.Notify(Ms(0));
            Assert.Equal(Ms(300), d.TimeUntilFire(Ms(0)));
            Assert.Equal(Ms(100), d.TimeUntilFire(Ms(200)));
            Assert.Equal(TimeSpan.Zero, d.TimeUntilFire(Ms(900)));
        }

        [Fact]
        public void TimeUntilFire_NeverExceedsMaxWait()
        {
            var d = Make();
            d.Notify(Ms(0));
            d.Notify(Ms(900));
            Assert.Equal(Ms(100), d.TimeUntilFire(Ms(900)));
        }

        [Fact]
        public void Reset_DropsPending()
        {
            var d = Make();
            d.Notify(Ms(0));
            d.Reset();
            Assert.False(d.ShouldFire(Ms(10_000)));
        }

        [Fact]
        public void Constructor_ClampsMaxWaitAndRejectsNegativeQuiet()
        {
            Assert.Equal(Ms(300), new ChangeDebouncer(Ms(300), Ms(10)).MaxWait);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ChangeDebouncer(Ms(-1), Ms(10)));
        }

        [Fact]
        public void ZeroQuiet_FiresImmediately()
        {
            var d = new ChangeDebouncer(TimeSpan.Zero, TimeSpan.Zero);
            d.Notify(Ms(5));
            Assert.True(d.ShouldFire(Ms(5)));
        }
    }
}
