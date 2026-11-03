using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Features.SnapLayouts;
using Xunit;

namespace CairoDesktop.Features.Tests.SnapLayouts
{
    public class LayoutPickerTests
    {
        private static readonly LayoutCatalog Catalog = new LayoutCatalog();

        private static string Pick(int w, int h, IDictionary<string, string> map = null, string def = null, string name = "\\\\.\\DISPLAY1", bool primary = false)
            => LayoutPicker.Pick(Catalog, new MonitorDescriptor(name, w, h, primary), map, def).Id;

        [Theory]
        [InlineData(1920, 1080, "halves-lr")]
        [InlineData(2560, 1600, "halves-lr")]
        [InlineData(1280, 1024, "halves-lr")]
        [InlineData(1024, 1024, "halves-lr")]
        [InlineData(1080, 1920, "halves-tb")]
        [InlineData(1439, 1440, "halves-tb")]
        [InlineData(3440, 1440, "ultrawide")]
        [InlineData(2560, 1080, "ultrawide")]
        [InlineData(5120, 1440, "ultrawide")]
        public void ByShape(int w, int h, string expected)
        {
            Assert.Equal(expected, Pick(w, h));
        }

        [Fact]
        public void UltrawideThresholdIsInclusive()
        {
            Assert.Equal("ultrawide", Pick(2200, 1000));
            Assert.Equal("halves-lr", Pick(2199, 1000));
        }

        [Fact]
        public void DefaultLayoutBeatsShape()
        {
            Assert.Equal("thirds", Pick(1080, 1920, def: "thirds"));
        }

        [Fact]
        public void UnknownDefaultFallsBackToShape()
        {
            Assert.Equal("halves-tb", Pick(1080, 1920, def: "nope"));
        }

        [Fact]
        public void MapByDeviceNameBeatsDefault()
        {
            var map = new Dictionary<string, string> { ["\\\\.\\DISPLAY1"] = "quarters" };
            Assert.Equal("quarters", Pick(1920, 1080, map, "thirds"));
            Assert.Equal("thirds", Pick(1920, 1080, map, "thirds", name: "\\\\.\\DISPLAY2"));
        }

        [Fact]
        public void MapByResolutionAndPrimary()
        {
            var map = new Dictionary<string, string> { ["1920x1080"] = "thirds", ["primary"] = "quarters" };
            Assert.Equal("thirds", Pick(1920, 1080, map, primary: true)); // resolution is more specific than 'primary'
            Assert.Equal("quarters", Pick(2560, 1440, map, primary: true));
            Assert.Equal("halves-lr", Pick(2560, 1440, map, primary: false));
        }

        [Fact]
        public void MapKeysAreCaseInsensitiveAndTrimmed()
        {
            var map = new Dictionary<string, string> { [" \\\\.\\display1 "] = "thirds" };
            Assert.Equal("thirds", Pick(1920, 1080, map));
        }

        [Fact]
        public void MapEntryWithUnknownLayoutIsSkipped()
        {
            var map = new Dictionary<string, string> { ["primary"] = "ghost" };
            Assert.Equal("thirds", Pick(1920, 1080, map, "thirds", primary: true));
        }

        [Fact]
        public void CustomLayoutCanBeMapped()
        {
            var custom = new SnapLayout("mine", "Mine", new[] { new Zone(0, 0, 1, 1) });
            var cat = new LayoutCatalog(new[] { custom });
            var map = new Dictionary<string, string> { ["primary"] = "mine" };
            Assert.Equal("mine", LayoutPicker.Pick(cat, new MonitorDescriptor("x", 1920, 1080, true), map).Id);
        }

        [Fact]
        public void NullMonitorAndZeroSizeAreSafe()
        {
            Assert.Equal("halves-lr", LayoutPicker.Pick(Catalog, null).Id);
            Assert.Equal("halves-lr", Pick(0, 0));
            Assert.Equal("halves-lr", LayoutPicker.Pick(null, null).Id);
        }
    }

    public class SnapMemoryTests
    {
        private static readonly Rect Orig = new Rect(100, 100, 500, 400);
        private static readonly Rect Zone0 = new Rect(0, 0, 960, 1080);
        private static readonly Rect Zone1 = new Rect(960, 0, 1920, 1080);

        [Fact]
        public void RememberAndRestore()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "halves-lr");
            Assert.True(m.Restore("a", out Rect r));
            Assert.Equal(Orig, r);
            Assert.Equal(0, m.Count);
            Assert.False(m.Restore("a", out _)); // restoring twice does nothing
        }

        [Fact]
        public void ZoneToZoneKeepsTheOriginalRect()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "halves-lr");
            m.Remember("a", Zone0, Zone1, 1, "halves-lr"); // window still where we put it
            m.Restore("a", out Rect r);
            Assert.Equal(Orig, r);
        }

        [Fact]
        public void StillSnappedToleratesMinimumSizeRounding()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "x");
            m.Remember("a", new Rect(1, 0, 961, 1081), Zone1, 1, "x");
            m.Restore("a", out Rect r);
            Assert.Equal(Orig, r);
        }

        [Fact]
        public void MovedWindowGetsNewOriginal()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "x");
            var moved = new Rect(300, 300, 700, 600); // user dragged it away
            m.Remember("a", moved, Zone1, 1, "x");
            m.Restore("a", out Rect r);
            Assert.Equal(moved, r);
        }

        [Fact]
        public void IsStillSnapped()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "x");
            Assert.True(m.IsStillSnapped("a", Zone0));
            Assert.True(m.IsStillSnapped("a", new Rect(0, 0, 961, 1080)));
            Assert.False(m.IsStillSnapped("a", Orig));
            Assert.False(m.IsStillSnapped("zzz", Zone0));
        }

        [Fact]
        public void EntryExposesZoneAndLayout()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone1, 1, "thirds");
            Assert.True(m.TryGet("a", out SnapMemory.Entry e));
            Assert.Equal(1, e.ZoneIndex);
            Assert.Equal("thirds", e.LayoutId);
            Assert.Equal(Zone1, e.AppliedRect);
        }

        [Fact]
        public void LeastRecentlyUsedIsEvicted()
        {
            var m = new SnapMemory(3);
            Assert.Null(m.Remember("a", Orig, Zone0, 0, "x"));
            m.Remember("b", Orig, Zone0, 0, "x");
            m.Remember("c", Orig, Zone0, 0, "x");
            Assert.Equal("a", m.Remember("d", Orig, Zone0, 0, "x"));
            Assert.Equal(3, m.Count);
            Assert.False(m.Peek("a", out _));
            Assert.True(m.Peek("d", out _));
        }

        [Fact]
        public void TryGetRefreshesRecency()
        {
            var m = new SnapMemory(3);
            m.Remember("a", Orig, Zone0, 0, "x");
            m.Remember("b", Orig, Zone0, 0, "x");
            m.Remember("c", Orig, Zone0, 0, "x");
            m.TryGet("a", out _);
            Assert.Equal("b", m.Remember("d", Orig, Zone0, 0, "x"));
        }

        [Fact]
        public void PeekDoesNotRefreshRecency()
        {
            var m = new SnapMemory(2);
            m.Remember("a", Orig, Zone0, 0, "x");
            m.Remember("b", Orig, Zone0, 0, "x");
            m.Peek("a", out _);
            Assert.Equal("a", m.Remember("c", Orig, Zone0, 0, "x"));
        }

        [Fact]
        public void ReRememberingRefreshesRecencyWithoutGrowing()
        {
            var m = new SnapMemory(2);
            m.Remember("a", Orig, Zone0, 0, "x");
            m.Remember("b", Orig, Zone0, 0, "x");
            Assert.Null(m.Remember("a", Zone0, Zone1, 1, "x"));
            Assert.Equal(2, m.Count);
            Assert.Equal("b", m.Remember("c", Orig, Zone0, 0, "x"));
            Assert.Equal(new[] { "c", "a" }, m.Keys.ToArray());
        }

        [Fact]
        public void CapacityIsAtLeastOne()
        {
            var m = new SnapMemory(0);
            m.Remember("a", Orig, Zone0, 0, "x");
            m.Remember("b", Orig, Zone0, 0, "x");
            Assert.Equal(1, m.Count);
            Assert.Equal(1, m.Capacity);
        }

        [Fact]
        public void ForgetRemovesAndReportsUnknown()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "x");
            Assert.True(m.Forget("a"));
            Assert.False(m.Forget("a"));
            Assert.False(m.Forget(null));
            Assert.Equal(0, m.Count);
        }

        [Fact]
        public void PruneDropsVanishedWindows()
        {
            var m = new SnapMemory();
            foreach (string k in new[] { "a", "b", "c", "d" }) m.Remember(k, Orig, Zone0, 0, "x");
            int dropped = m.Prune(k => k == "a" || k == "c");
            Assert.Equal(2, dropped);
            Assert.Equal(new[] { "c", "a" }, m.Keys.ToArray());
        }

        [Fact]
        public void PruneTreatsThrowingCheckAsDead()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "x");
            Assert.Equal(1, m.Prune(k => throw new System.InvalidOperationException()));
            Assert.Equal(0, m.Prune(null));
        }

        [Fact]
        public void NullKeyIsIgnored()
        {
            var m = new SnapMemory();
            Assert.Null(m.Remember(null, Orig, Zone0, 0, "x"));
            Assert.Equal(0, m.Count);
            Assert.False(m.TryGet(null, out _));
        }

        [Fact]
        public void ClearEmptiesEverything()
        {
            var m = new SnapMemory();
            m.Remember("a", Orig, Zone0, 0, "x");
            m.Clear();
            Assert.Equal(0, m.Count);
            Assert.Empty(m.Keys);
        }

        [Fact]
        public void WindowKeyIsStableAndDistinguishesWindows()
        {
            string k1 = WindowKey.Make(0x1A2B, "Notepad.exe", "Untitled");
            Assert.Equal(k1, WindowKey.Make(0x1A2B, "notepad.EXE ", "Untitled"));
            Assert.NotEqual(k1, WindowKey.Make(0x1A2C, "notepad.exe", "Untitled"));
            Assert.NotEqual(k1, WindowKey.Make(0x1A2B, "calc.exe", "Untitled"));
            Assert.NotEqual(k1, WindowKey.Make(0x1A2B, "notepad.exe", "Other"));
            Assert.Equal(WindowKey.Make(1, "a"), WindowKey.Make(1, "a", ""));
            Assert.StartsWith("1a2b:notepad.exe:", k1);
        }

        [Fact]
        public void TitleHashIsFnv1a()
        {
            Assert.Equal("e40c292c", WindowKey.Hash("a")); // FNV-1a 32-bit of "a"
            Assert.Equal("0", WindowKey.Hash(null));
        }
    }

    public class SnapPolicyTests
    {
        [Fact]
        public void NormalWindowsAreSnappable()
        {
            Assert.True(new SnapPolicy().CanSnap(WindowStates.Normal));
        }

        [Fact]
        public void MinimizedFullscreenAndExcludedAreNeverSnapped()
        {
            var p = new SnapPolicy { SnapMaximized = true };
            Assert.False(p.CanSnap(WindowStates.Minimized));
            Assert.False(p.CanSnap(WindowStates.FullScreen));
            Assert.False(p.CanSnap(WindowStates.Excluded));
            Assert.False(p.CanSnap(WindowStates.Maximized | WindowStates.Minimized));
        }

        [Fact]
        public void MaximizedFollowsPolicy()
        {
            Assert.False(new SnapPolicy().CanSnap(WindowStates.Maximized));
            Assert.True(new SnapPolicy { SnapMaximized = true }.CanSnap(WindowStates.Maximized));
        }
    }

    public class FrameInsetsTests
    {
        [Fact]
        public void TypicalWindows10Border()
        {
            // GetWindowRect has 7px invisible borders left/right/bottom; DWM frame is the visible window.
            var window = new Rect(93, 100, 507, 407);
            var frame = new Rect(100, 100, 500, 400);
            Rect result = FrameInsets.Compensate(new Rect(0, 0, 960, 1040), frame, window);
            Assert.Equal(new Rect(-7, 0, 967, 1047), result);
        }

        [Fact]
        public void VisibleFrameLandsOnTheZoneAfterCompensation()
        {
            var window = new Rect(93, 100, 507, 407);
            var frame = new Rect(100, 100, 500, 400);
            var insets = FrameInsets.Measure(frame, window);
            Rect zone = new Rect(960, 0, 1920, 1040);
            Rect setPos = insets.Apply(zone);
            // Applying the same insets again to the new window rect yields the zone as the visible frame.
            Assert.Equal(zone.Left, setPos.Left + insets.Left);
            Assert.Equal(zone.Right, setPos.Right - insets.Right);
            Assert.Equal(zone.Top, setPos.Top + insets.Top);
            Assert.Equal(zone.Bottom, setPos.Bottom - insets.Bottom);
        }

        [Fact]
        public void NoBorderMeansNoChange()
        {
            var r = new Rect(10, 10, 410, 310);
            Assert.Equal(new Rect(0, 0, 100, 100), FrameInsets.Compensate(new Rect(0, 0, 100, 100), r, r));
        }

        [Fact]
        public void AsymmetricInsets()
        {
            var window = new Rect(0, 0, 400, 300);
            var frame = new Rect(1, 2, 395, 290);
            FrameInsets i = FrameInsets.Measure(frame, window);
            Assert.Equal(1, i.Left);
            Assert.Equal(2, i.Top);
            Assert.Equal(5, i.Right);
            Assert.Equal(10, i.Bottom);
            Assert.Equal(new Rect(99, 198, 505, 510), FrameInsets.Compensate(new Rect(100, 200, 500, 500), frame, window));
        }

        [Fact]
        public void NegativeInsetsShrinkTheTarget()
        {
            // Frame bounds larger than the window rect (e.g. a window drawing a shadow outside its rect).
            var window = new Rect(10, 10, 110, 110);
            var frame = new Rect(8, 8, 113, 112);
            FrameInsets i = FrameInsets.Measure(frame, window);
            Assert.Equal(-2, i.Left);
            Assert.Equal(-3, i.Right);
            Assert.Equal(new Rect(202, 202, 297, 298), FrameInsets.Compensate(new Rect(200, 200, 300, 300), frame, window));
        }

        [Fact]
        public void FailedDwmQueryLeavesRectUntouched()
        {
            Rect d = new Rect(0, 0, 100, 100);
            Assert.Equal(d, FrameInsets.Compensate(d, default, new Rect(0, 0, 50, 50)));
            Assert.Equal(d, FrameInsets.Compensate(d, new Rect(0, 0, 50, 50), default));
        }

        [Fact]
        public void ImplausibleInsetsAreIgnored()
        {
            // e.g. frame bounds in physical pixels while the window rect was virtualised.
            Rect d = new Rect(0, 0, 100, 100);
            Assert.Equal(d, FrameInsets.Compensate(d, new Rect(0, 0, 1000, 1000), new Rect(0, 0, 500, 500)));
            Assert.Equal(FrameInsets.None.Left, FrameInsets.Measure(new Rect(0, 0, 1000, 1000), new Rect(0, 0, 500, 500)).Left);
        }

        [Fact]
        public void MaxPlausibleIsConfigurable()
        {
            var window = new Rect(0, 0, 200, 200);
            var frame = new Rect(30, 0, 200, 200);
            Assert.Equal(30, FrameInsets.Measure(frame, window).Left);
            Assert.Equal(0, FrameInsets.Measure(frame, window, 20).Left);
        }

        [Fact]
        public void EmptyDesiredStaysEmpty()
        {
            var window = new Rect(0, 0, 100, 100);
            var frame = new Rect(7, 0, 93, 93);
            Assert.True(FrameInsets.Compensate(new Rect(5, 5, 5, 5), frame, window).IsEmpty);
        }

        [Fact]
        public void AdjacentZonesStillTouchVisiblyAfterCompensation()
        {
            // Both windows have 7px invisible borders. After compensation the visible frames are exactly 'gap' apart,
            // even though the SetWindowPos rectangles overlap.
            var window = new Rect(0, 0, 414, 307);
            var frame = new Rect(7, 0, 407, 300);
            var zones = ZoneCalculator.Compute(BuiltInLayouts.Halves(), new Rect(0, 0, 1000, 600), 10, 0);
            Rect a = FrameInsets.Compensate(zones[0], frame, window);
            Rect b = FrameInsets.Compensate(zones[1], frame, window);
            Assert.Equal(10, (b.Left + 7) - (a.Right - 7));
        }
    }
}
