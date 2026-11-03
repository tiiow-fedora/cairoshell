using CairoDesktop.Features.SnapLayouts;
using Xunit;

namespace CairoDesktop.Features.Tests.SnapLayouts
{
    public class ZoneHitTesterTests
    {
        private static readonly Rect Screen = new Rect(0, 0, 1200, 800);

        private static Rect[] Zones(SnapLayout layout, int gap = 0) => ZoneCalculator.Compute(layout, Screen, gap, 0);

        [Fact]
        public void HitTestFindsContainingZone()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            Assert.Equal(0, ZoneHitTester.HitTest(z, 10, 10));
            Assert.Equal(1, ZoneHitTester.HitTest(z, 1100, 700));
        }

        [Fact]
        public void HitTestAtSharedBorderBelongsToRightZone()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            Assert.Equal(0, ZoneHitTester.HitTest(z, 599, 100));
            Assert.Equal(1, ZoneHitTester.HitTest(z, 600, 100));
        }

        [Fact]
        public void HitTestOutsideEverythingIsMinusOne()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            Assert.Equal(-1, ZoneHitTester.HitTest(z, -5, 10));
            Assert.Equal(-1, ZoneHitTester.HitTest(z, 1200, 10));
        }

        [Fact]
        public void PointInGapNeedsToleranceAndPicksNearerZone()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves(), gap: 20); // zone0 ends 590, zone1 starts 610
            Assert.Equal(-1, ZoneHitTester.HitTest(z, 595, 100));
            Assert.Equal(0, ZoneHitTester.HitTest(z, 595, 100, tolerance: 10));
            Assert.Equal(1, ZoneHitTester.HitTest(z, 606, 100, tolerance: 10));
        }

        [Fact]
        public void PointInGapTooFarForToleranceMisses()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves(), gap: 40);
            Assert.Equal(-1, ZoneHitTester.HitTest(z, 600, 100, tolerance: 5));
        }

        [Fact]
        public void GapPointGoesToNearerZone()
        {
            // Gap 20: zone0 covers x<590, zone1 starts at 610; pixel 590 is 1 from zone0's last pixel (589), 20 from zone1.
            Rect[] z = Zones(BuiltInLayouts.Halves(), gap: 20);
            Assert.Equal(0, ZoneHitTester.HitTest(z, 599, 100, tolerance: 15));
            Assert.Equal(1, ZoneHitTester.HitTest(z, 600, 100, tolerance: 15));
        }

        [Fact]
        public void InsideBeatsTolerance()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves(), gap: 20);
            Assert.Equal(1, ZoneHitTester.HitTest(z, 610, 100, tolerance: 100));
        }

        [Fact]
        public void NullAndEmptyAreSafe()
        {
            Assert.Equal(-1, ZoneHitTester.HitTest(null, 1, 1));
            Assert.Equal(-1, ZoneHitTester.NearestZone(null, Screen));
            Assert.Equal(-1, ZoneHitTester.NearestZone(new Rect[0], Screen));
            Assert.Equal(-1, ZoneHitTester.Navigate(new Rect[0], 0, Direction.Left, true));
            Assert.Equal(-1, ZoneHitTester.EdgeZone(null, Direction.Left));
            Assert.Equal(-1, ZoneHitTester.Next(null, 0, true));
            Assert.Equal(-1, ZoneHitTester.Previous(new Rect[0], 0, true));
        }

        [Fact]
        public void NearestZoneUsesLargestIntersection()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            Assert.Equal(0, ZoneHitTester.NearestZone(z, new Rect(100, 100, 700, 500)));   // 500 wide in zone 0, 100 in zone 1
            Assert.Equal(1, ZoneHitTester.NearestZone(z, new Rect(500, 100, 1100, 500)));
        }

        [Fact]
        public void NearestZoneTieGoesToClosestCentre()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            // 100 px in each zone; window centred on the border: centres equidistant, lower index wins.
            Assert.Equal(0, ZoneHitTester.NearestZone(z, new Rect(500, 100, 700, 200)));
            // Equal areas again, window centred exactly on the border: tie goes to the lower index.
            Assert.Equal(0, ZoneHitTester.NearestZone(z, new Rect(450, 100, 750, 200)));
            // Slightly more area in zone 1 wins outright.
            Assert.Equal(1, ZoneHitTester.NearestZone(z, new Rect(460, 100, 760, 200)));
        }

        [Fact]
        public void NearestZoneOutsideAllZonesFallsBackToCentreDistance()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            Assert.Equal(1, ZoneHitTester.NearestZone(z, new Rect(2000, 100, 2100, 200)));
            Assert.Equal(0, ZoneHitTester.NearestZone(z, new Rect(-500, 100, -400, 200)));
        }

        [Fact]
        public void NearestZoneInQuarters()
        {
            Rect[] z = Zones(BuiltInLayouts.QuartersLayout());
            Assert.Equal(3, ZoneHitTester.NearestZone(z, new Rect(700, 500, 1100, 780)));
            Assert.Equal(2, ZoneHitTester.NearestZone(z, new Rect(10, 450, 300, 790)));
        }

        // ---- Navigation in each built-in layout ----

        [Fact]
        public void HalvesLeftRight()
        {
            Rect[] z = Zones(BuiltInLayouts.Halves());
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Right, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Left, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 1, Direction.Right, false)); // no wrap: stay
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Right, true));   // wrap
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Left, true));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 0, Direction.Up, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 0, Direction.Down, true)); // a single row never wraps vertically
        }

        [Fact]
        public void HalvesTopBottom()
        {
            Rect[] z = Zones(BuiltInLayouts.HalvesTopBottom());
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Down, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Up, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 1, Direction.Down, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Down, true));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Up, true));
        }

        [Fact]
        public void ThirdsWalkRightAndWrap()
        {
            Rect[] z = Zones(BuiltInLayouts.ThirdsLayout());
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Right, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 1, Direction.Right, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 2, Direction.Right, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 2, Direction.Right, true));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 0, Direction.Left, true));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 2, Direction.Left, false));
        }

        [Fact]
        public void QuartersMoveInAllDirections()
        {
            Rect[] z = Zones(BuiltInLayouts.QuartersLayout()); // 0 TL, 1 TR, 2 BL, 3 BR
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Right, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 0, Direction.Down, false));
            Assert.Equal(3, ZoneHitTester.Navigate(z, 1, Direction.Down, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 3, Direction.Left, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 3, Direction.Up, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 0, Direction.Left, false));
        }

        [Fact]
        public void QuartersWrapStaysInRowOrColumn()
        {
            Rect[] z = Zones(BuiltInLayouts.QuartersLayout());
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Right, true)); // top row -> top-left
            Assert.Equal(2, ZoneHitTester.Navigate(z, 3, Direction.Right, true)); // bottom row -> bottom-left
            Assert.Equal(1, ZoneHitTester.Navigate(z, 3, Direction.Down, true));  // right column -> top-right
            Assert.Equal(2, ZoneHitTester.Navigate(z, 0, Direction.Up, true));    // left column -> bottom-left
        }

        [Fact]
        public void TwoThirdsAndOneThirdNavigate()
        {
            Assert.Equal(1, ZoneHitTester.Navigate(Zones(BuiltInLayouts.TwoThirdsPlusOneThird()), 0, Direction.Right, false));
            Assert.Equal(0, ZoneHitTester.Navigate(Zones(BuiltInLayouts.OneThirdPlusTwoThirds()), 1, Direction.Left, false));
        }

        [Fact]
        public void MainStackNavigation()
        {
            Rect[] z = Zones(BuiltInLayouts.MainPlusStack()); // 0 main, 1 stack top, 2 stack bottom
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Right, false)); // nearest of the two stack zones; top is closer vertically
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Left, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 2, Direction.Left, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 1, Direction.Down, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 2, Direction.Up, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 2, Direction.Down, true));
        }

        [Fact]
        public void UltrawideNavigation()
        {
            Rect[] z = Zones(BuiltInLayouts.UltrawideColumns());
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Right, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 1, Direction.Right, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Left, false));
        }

        [Fact]
        public void CenterFocusNavigation()
        {
            Rect[] z = Zones(BuiltInLayouts.CenterFocusLayout()); // 0 left strip, 1 centre, 2 right strip
            Assert.Equal(1, ZoneHitTester.Navigate(z, 0, Direction.Right, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 1, Direction.Right, false));
            Assert.Equal(0, ZoneHitTester.Navigate(z, 1, Direction.Left, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 1, Direction.Up, false));
        }

        [Fact]
        public void SingleZoneNavigationStaysPut()
        {
            Rect[] z = Zones(BuiltInLayouts.Grid(1, 1));
            foreach (Direction d in new[] { Direction.Left, Direction.Right, Direction.Up, Direction.Down })
            {
                Assert.Equal(0, ZoneHitTester.Navigate(z, 0, d, true));
                Assert.Equal(0, ZoneHitTester.Navigate(z, 0, d, false));
            }
        }

        [Fact]
        public void NavigationFromNoZoneGoesToEdgeZone()
        {
            Rect[] z = Zones(BuiltInLayouts.ThirdsLayout());
            Assert.Equal(0, ZoneHitTester.Navigate(z, -1, Direction.Left, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, -1, Direction.Right, false));
            Assert.Equal(2, ZoneHitTester.Navigate(z, 99, Direction.Right, false));
        }

        [Fact]
        public void EdgeZoneInQuarters()
        {
            Rect[] z = Zones(BuiltInLayouts.QuartersLayout());
            Assert.Equal(0, ZoneHitTester.EdgeZone(z, Direction.Left));   // tie TL/BL -> lower index
            Assert.Equal(1, ZoneHitTester.EdgeZone(z, Direction.Right));
            Assert.Equal(0, ZoneHitTester.EdgeZone(z, Direction.Up));
            Assert.Equal(2, ZoneHitTester.EdgeZone(z, Direction.Down));
        }

        [Fact]
        public void NavigationWithGapsBehavesTheSame()
        {
            Rect[] z = Zones(BuiltInLayouts.QuartersLayout(), gap: 24);
            Assert.Equal(3, ZoneHitTester.Navigate(z, 2, Direction.Right, false));
            Assert.Equal(1, ZoneHitTester.Navigate(z, 3, Direction.Up, false));
        }

        [Fact]
        public void SequentialNextAndPrevious()
        {
            Rect[] z = Zones(BuiltInLayouts.ThirdsLayout());
            Assert.Equal(1, ZoneHitTester.Next(z, 0, false));
            Assert.Equal(2, ZoneHitTester.Next(z, 2, false));
            Assert.Equal(0, ZoneHitTester.Next(z, 2, true));
            Assert.Equal(0, ZoneHitTester.Next(z, -1, false));
            Assert.Equal(1, ZoneHitTester.Previous(z, 2, false));
            Assert.Equal(0, ZoneHitTester.Previous(z, 0, false));
            Assert.Equal(2, ZoneHitTester.Previous(z, 0, true));
            Assert.Equal(2, ZoneHitTester.Previous(z, -1, false));
        }
    }
}
