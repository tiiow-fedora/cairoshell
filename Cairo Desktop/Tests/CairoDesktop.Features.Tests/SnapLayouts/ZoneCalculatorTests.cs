using System.Collections.Generic;
using System.Linq;
using CairoDesktop.Features.SnapLayouts;
using Xunit;

namespace CairoDesktop.Features.Tests.SnapLayouts
{
    public class RectTests
    {
        [Fact]
        public void SizeAndEmptiness()
        {
            var r = new Rect(10, 20, 110, 70);
            Assert.Equal(100, r.Width);
            Assert.Equal(50, r.Height);
            Assert.Equal(5000, r.Area);
            Assert.False(r.IsEmpty);
            Assert.True(new Rect(5, 5, 5, 9).IsEmpty);
            Assert.True(new Rect(5, 5, 3, 9).IsEmpty);
            Assert.Equal(0, new Rect(5, 5, 3, 9).Area);
        }

        [Fact]
        public void ContainsIsHalfOpen()
        {
            var r = new Rect(0, 0, 10, 10);
            Assert.True(r.Contains(0, 0));
            Assert.True(r.Contains(9, 9));
            Assert.False(r.Contains(10, 5));
            Assert.False(r.Contains(5, 10));
            Assert.False(r.Contains(-1, 5));
        }

        [Fact]
        public void IntersectOverlapAndDisjoint()
        {
            Assert.Equal(new Rect(5, 5, 10, 10), Rect.Intersect(new Rect(0, 0, 10, 10), new Rect(5, 5, 20, 20)));
            Assert.True(Rect.Intersect(new Rect(0, 0, 10, 10), new Rect(10, 0, 20, 10)).IsEmpty); // touching only
            Assert.False(new Rect(0, 0, 10, 10).IntersectsWith(new Rect(20, 20, 30, 30)));
        }

        [Fact]
        public void InsetShrinksAndNeverGoesNegative()
        {
            Assert.Equal(new Rect(2, 3, 8, 7), new Rect(0, 0, 10, 10).Inset(2, 3, 2, 3));
            Assert.Equal(new Rect(1, 1, 9, 9), new Rect(0, 0, 10, 10).Inset(1));
            Rect tiny = new Rect(0, 0, 4, 4).Inset(10);
            Assert.True(tiny.IsEmpty);
            Assert.True(tiny.Width >= 0 && tiny.Height >= 0);
            Assert.Equal(new Rect(-2, -2, 12, 12), new Rect(0, 0, 10, 10).Inset(-2));
        }

        [Fact]
        public void ContainsRectAndNearlyEquals()
        {
            Assert.True(new Rect(0, 0, 10, 10).Contains(new Rect(2, 2, 8, 8)));
            Assert.False(new Rect(0, 0, 10, 10).Contains(new Rect(2, 2, 11, 8)));
            Assert.True(new Rect(0, 0, 10, 10).NearlyEquals(new Rect(1, -1, 11, 9), 1));
            Assert.False(new Rect(0, 0, 10, 10).NearlyEquals(new Rect(2, 0, 10, 10), 1));
        }
    }

    public class SnapLayoutTests
    {
        [Fact]
        public void AllBuiltInsAreValid()
        {
            foreach (SnapLayout l in BuiltInLayouts.All())
            {
                Assert.True(l.IsValid, l.Id + ": " + string.Join("; ", l.Validate()));
            }
        }

        [Fact]
        public void BuiltInIdsAreUnique()
        {
            var ids = BuiltInLayouts.All().Select(l => l.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct().Count());
            Assert.Equal(9, ids.Count);
        }

        [Fact]
        public void ZoneCountsMatchTheirNames()
        {
            Assert.Equal(2, BuiltInLayouts.Halves().Zones.Count);
            Assert.Equal(2, BuiltInLayouts.HalvesTopBottom().Zones.Count);
            Assert.Equal(3, BuiltInLayouts.ThirdsLayout().Zones.Count);
            Assert.Equal(4, BuiltInLayouts.QuartersLayout().Zones.Count);
            Assert.Equal(3, BuiltInLayouts.MainPlusStack().Zones.Count);
            Assert.Equal(3, BuiltInLayouts.UltrawideColumns().Zones.Count);
            Assert.Equal(3, BuiltInLayouts.CenterFocusLayout().Zones.Count);
        }

        [Fact]
        public void EmptyLayoutIsInvalid()
        {
            Assert.NotEmpty(new SnapLayout("x", "x", new Zone[0]).Validate());
        }

        [Theory]
        [InlineData(0, 0, 0, 1)]
        [InlineData(0, 0, 1, 0)]
        [InlineData(0, 0, -0.5, 1)]
        public void ZeroOrNegativeAreaIsInvalid(double x, double y, double w, double h)
        {
            var errors = new SnapLayout("x", "x", new[] { new Zone(x, y, w, h) }).Validate();
            Assert.Contains(errors, e => e.Contains("greater than 0"));
        }

        [Theory]
        [InlineData(-0.1, 0, 0.5, 1)]
        [InlineData(0.6, 0, 0.5, 1)]
        [InlineData(0, 0.5, 1, 0.6)]
        public void ZonesOutsideTheUnitSquareAreInvalid(double x, double y, double w, double h)
        {
            Assert.Contains(new SnapLayout("x", "x", new[] { new Zone(x, y, w, h) }).Validate(), e => e.Contains("outside"));
        }

        [Fact]
        public void NanAndInfinityAreInvalid()
        {
            Assert.NotEmpty(new SnapLayout("x", "x", new[] { new Zone(double.NaN, 0, 1, 1) }).Validate());
            Assert.NotEmpty(new SnapLayout("x", "x", new[] { new Zone(0, 0, double.PositiveInfinity, 1) }).Validate());
        }

        [Fact]
        public void OverlapIsRejectedUnlessAllowed()
        {
            var zones = new[] { new Zone(0, 0, 0.6, 1), new Zone(0.5, 0, 0.5, 1) };
            Assert.Contains(new SnapLayout("o", "o", zones).Validate(), e => e.Contains("overlap"));
            Assert.Empty(new SnapLayout("o", "o", zones, allowOverlap: true).Validate());
        }

        [Fact]
        public void TouchingZonesDoNotCountAsOverlap()
        {
            Assert.Empty(new SnapLayout("t", "t", new[] { new Zone(0, 0, 0.5, 1), new Zone(0.5, 0, 0.5, 1) }).Validate());
        }

        [Theory]
        [InlineData("")]
        [InlineData("has space")]
        [InlineData("a/b")]
        public void BadIdsAreInvalid(string id)
        {
            Assert.NotEmpty(new SnapLayout(id, "n", new[] { new Zone(0, 0, 1, 1) }).Validate());
        }

        [Fact]
        public void TooManyZonesAreInvalid()
        {
            var zones = Enumerable.Range(0, SnapLayout.MaxZones + 1).Select(i => new Zone(0, 0, 1, 1)).ToList();
            Assert.Contains(new SnapLayout("big", "big", zones, true).Validate(), e => e.Contains("maximum"));
        }

        [Fact]
        public void NameFallsBackToId()
        {
            Assert.Equal("abc", new SnapLayout("abc", null, new Zone[0]).Name);
        }

        [Fact]
        public void GridGeneratorHonoursWeightsAndOrder()
        {
            SnapLayout g = BuiltInLayouts.Grid("g", "g", new double[] { 1, 3 }, new double[] { 1, 1 });
            Assert.Equal(4, g.Zones.Count);
            Assert.Equal(0.25, g.Zones[0].W, 9);
            Assert.Equal(0.75, g.Zones[1].W, 9);
            Assert.Equal(0.25, g.Zones[1].X, 9);
            Assert.Equal(0.5, g.Zones[2].Y, 9);
            Assert.True(g.IsValid);
        }

        [Fact]
        public void GridGeneratorCleansBadWeights()
        {
            SnapLayout g = BuiltInLayouts.Grid("g", "g", new double[] { -1, double.NaN, 0 }, null);
            Assert.Equal(3, g.Zones.Count);
            Assert.True(g.IsValid);
            Assert.Equal(1.0 / 3, g.Zones[0].W, 9);
        }

        [Fact]
        public void SimpleGridClampsCounts()
        {
            Assert.Equal(64, BuiltInLayouts.Grid(100, 100).Zones.Count);
            Assert.Single(BuiltInLayouts.Grid(0, -3).Zones);
            Assert.Equal("grid-3x2", BuiltInLayouts.Grid(3, 2).Id);
        }

        [Fact]
        public void CatalogFindsCaseInsensitivelyAndCycles()
        {
            var catalog = new LayoutCatalog();
            Assert.NotNull(catalog.Find("HALVES-LR"));
            Assert.Null(catalog.Find("nope"));
            Assert.Null(catalog.Find(null));
            Assert.Equal(catalog.All[1].Id, catalog.Cycle(catalog.All[0].Id).Id);
            Assert.Equal(catalog.All[catalog.All.Count - 1].Id, catalog.Cycle(catalog.All[0].Id, -1).Id);
            Assert.Equal(catalog.All[0].Id, catalog.Cycle(catalog.All[catalog.All.Count - 1].Id).Id);
            Assert.Equal(catalog.All[0].Id, catalog.Cycle("unknown").Id);
        }

        [Fact]
        public void CatalogIgnoresInvalidAndDuplicateCustomLayouts()
        {
            var good = new SnapLayout("mine", "Mine", new[] { new Zone(0, 0, 1, 1) });
            var bad = new SnapLayout("bad", "Bad", new Zone[0]);
            var dup = new SnapLayout("thirds", "Dup", new[] { new Zone(0, 0, 1, 1) });
            var catalog = new LayoutCatalog(new[] { good, bad, dup, null });
            Assert.Equal(10, catalog.All.Count);
            Assert.NotNull(catalog.Find("mine"));
            Assert.Null(catalog.Find("bad"));
            Assert.Equal(3, catalog.Find("thirds").Zones.Count);
        }
    }

    public class ZoneCalculatorTests
    {
        private static Rect Area(int w, int h, int x = 0, int y = 0) => new Rect(x, y, x + w, y + h);

        [Fact]
        public void HalvesOfEvenAreaAreExact()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(1920, 1080), 0, 0);
            Assert.Equal(new Rect(0, 0, 960, 1080), z[0]);
            Assert.Equal(new Rect(960, 0, 1920, 1080), z[1]);
        }

        [Fact]
        public void OddAreaHalvesLoseNoPixel()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(1919, 1079), 0, 0);
            Assert.Equal(1919, z[0].Width + z[1].Width);
            Assert.Equal(z[0].Right, z[1].Left);
            Assert.Equal(1079, z[0].Height);
        }

        [Fact]
        public void ThirdsOf1919Are640_639_640()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.ThirdsLayout(), Area(1919, 1079), 0, 0);
            Assert.Equal(1919, z.Sum(r => r.Width));
            Assert.All(z, r => Assert.InRange(r.Width, 639, 640));
        }

        [Fact]
        public void WorkAreaOffsetIsRespected()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(1000, 500, -1920, 40), 0, 0);
            Assert.Equal(-1920, z[0].Left);
            Assert.Equal(40, z[0].Top);
            Assert.Equal(-920, z[1].Right);
            Assert.Equal(540, z[1].Bottom);
        }

        [Fact]
        public void GapIsExactlyThatWide()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(1000, 600), 10, 0);
            Assert.Equal(10, z[1].Left - z[0].Right);
            Assert.Equal(0, z[0].Left);
            Assert.Equal(1000, z[1].Right);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(3)]
        [InlineData(7)]
        public void OddGapsAreExact(int gap)
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.QuartersLayout(), Area(1001, 777), gap, 0);
            Assert.Equal(gap, z[1].Left - z[0].Right);
            Assert.Equal(gap, z[3].Left - z[2].Right);
            Assert.Equal(gap, z[2].Top - z[0].Bottom);
            Assert.Equal(gap, z[3].Top - z[1].Bottom);
        }

        [Fact]
        public void OuterGapInsetsEverything()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(1000, 600), 0, 12);
            Assert.Equal(12, z[0].Left);
            Assert.Equal(12, z[0].Top);
            Assert.Equal(988, z[1].Right);
            Assert.Equal(588, z[1].Bottom);
            Assert.Equal(z[0].Right, z[1].Left);
        }

        [Fact]
        public void GapAndOuterGapCombine()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(1000, 600), 8, 20);
            Assert.Equal(20, z[0].Left);
            Assert.Equal(980, z[1].Right);
            Assert.Equal(8, z[1].Left - z[0].Right);
            Assert.Equal(560, z[0].Height);
        }

        [Fact]
        public void NegativeGapsCountAsZero()
        {
            Rect[] a = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(100, 100), -5, -5);
            Rect[] b = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(100, 100), 0, 0);
            Assert.Equal(b, a);
        }

        [Fact]
        public void GapLargerThanZoneClampsToOnePixel()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.ThirdsLayout(), Area(30, 30), 50, 0);
            Assert.All(z, r => { Assert.True(r.Width >= 1); Assert.True(r.Height >= 1); });
            Assert.All(z, r => Assert.True(new Rect(0, 0, 30, 30).Contains(r)));
        }

        [Fact]
        public void HugeOuterGapKeepsAtLeastOnePixel()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), Area(100, 50), 0, 10000);
            Assert.All(z, r => Assert.True(r.Width >= 1 && r.Height >= 1));
            Assert.All(z, r => Assert.True(new Rect(0, 0, 100, 50).Contains(r)));
        }

        [Fact]
        public void OnePixelAreaGivesOnePixelZones()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.QuartersLayout(), Area(1, 1), 4, 4);
            Assert.All(z, r => Assert.Equal(new Rect(0, 0, 1, 1), r));
        }

        [Fact]
        public void TinyAreasNeverProduceEmptyZonesInside()
        {
            foreach (SnapLayout layout in BuiltInLayouts.All())
            {
                for (int w = 1; w <= 12; w++)
                {
                    for (int h = 1; h <= 12; h++)
                    {
                        Rect[] z = ZoneCalculator.Compute(layout, Area(w, h, 7, 9), 3, 2);
                        foreach (Rect r in z)
                        {
                            Assert.True(r.Width >= 1 && r.Height >= 1, $"{layout.Id} {w}x{h} {r}");
                            Assert.True(new Rect(7, 9, 7 + w, 9 + h).Contains(r), $"{layout.Id} {w}x{h} {r}");
                        }
                    }
                }
            }
        }

        [Fact]
        public void EmptyWorkAreaYieldsEmptyZones()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.Halves(), new Rect(5, 5, 5, 20), 2, 2);
            Assert.Equal(2, z.Length);
            Assert.All(z, r => Assert.True(r.IsEmpty));
        }

        [Fact]
        public void NullLayoutYieldsNothing()
        {
            Assert.Empty(ZoneCalculator.Compute(null, Area(10, 10), 0, 0));
        }

        [Fact]
        public void ColumnLayoutsTileExactlyAcrossManyWidths()
        {
            foreach (SnapLayout layout in new[] { BuiltInLayouts.Halves(), BuiltInLayouts.ThirdsLayout(), BuiltInLayouts.UltrawideColumns(), BuiltInLayouts.TwoThirdsPlusOneThird(), BuiltInLayouts.OneThirdPlusTwoThirds() })
            {
                foreach (int gap in new[] { 0, 1, 2, 5, 8 })
                {
                    foreach (int outer in new[] { 0, 3, 10 })
                    {
                        for (int w = 100; w <= 700; w += 7)
                        {
                            var area = Area(w, 400, 13, -5);
                            Rect[] z = ZoneCalculator.Compute(layout, area, gap, outer);
                            string ctx = $"{layout.Id} w={w} gap={gap} outer={outer}";

                            Assert.Equal(area.Left + outer, z[0].Left);
                            Assert.Equal(area.Right - outer, z[z.Length - 1].Right);
                            for (int i = 1; i < z.Length; i++) Assert.True(z[i].Left - z[i - 1].Right == gap, ctx);
                            int total = z.Sum(r => r.Width) + gap * (z.Length - 1);
                            Assert.True(total == w - 2 * outer, ctx);
                            Assert.All(z, r => Assert.True(r.Top == area.Top + outer && r.Bottom == area.Bottom - outer, ctx));
                        }
                    }
                }
            }
        }

        [Fact]
        public void GridLayoutsTileExactlyInBothAxes()
        {
            for (int cols = 1; cols <= 5; cols++)
            {
                for (int rows = 1; rows <= 4; rows++)
                {
                    SnapLayout layout = BuiltInLayouts.Grid(cols, rows);
                    foreach (int gap in new[] { 0, 1, 4 })
                    {
                        for (int w = 60; w <= 130; w += 3)
                        {
                            for (int h = 60; h <= 130; h += 5)
                            {
                                Rect[] z = ZoneCalculator.Compute(layout, Area(w, h), gap, 0);
                                string ctx = $"{cols}x{rows} {w}x{h} gap={gap}";
                                for (int r = 0; r < rows; r++)
                                {
                                    int widthSum = 0;
                                    for (int c = 0; c < cols; c++)
                                    {
                                        Rect cell = z[r * cols + c];
                                        widthSum += cell.Width;
                                        if (c > 0) Assert.True(cell.Left - z[r * cols + c - 1].Right == gap, ctx);
                                        if (r > 0) Assert.True(cell.Top - z[(r - 1) * cols + c].Bottom == gap, ctx);
                                    }

                                    Assert.True(widthSum + gap * (cols - 1) == w, ctx);
                                }

                                int heightSum = 0;
                                for (int r = 0; r < rows; r++) heightSum += z[r * cols].Height;
                                Assert.True(heightSum + gap * (rows - 1) == h, ctx);
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void ZoneSizesNeverDeviateMoreThanOnePixelFromIdeal()
        {
            SnapLayout thirds = BuiltInLayouts.ThirdsLayout();
            for (int w = 3; w < 400; w++)
            {
                Rect[] z = ZoneCalculator.Compute(thirds, Area(w, 10), 0, 0);
                foreach (Rect r in z) Assert.InRange(r.Width, w / 3.0 - 1, w / 3.0 + 1);
            }
        }

        [Fact]
        public void WeightedRowsAndColumnsMatchRatio()
        {
            SnapLayout g = BuiltInLayouts.Grid("w", "w", new double[] { 1, 3 }, new double[] { 1, 1, 2 });
            Rect[] z = ZoneCalculator.Compute(g, Area(400, 400), 0, 0);
            Assert.Equal(100, z[0].Width);
            Assert.Equal(300, z[1].Width);
            Assert.Equal(100, z[0].Height);
            Assert.Equal(200, z[4].Height);
        }

        [Fact]
        public void MainStackSharesEdgeWithBothStackedZones()
        {
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.MainPlusStack(), Area(1000, 800), 10, 0);
            Assert.Equal(10, z[1].Left - z[0].Right);
            Assert.Equal(z[1].Left, z[2].Left);
            Assert.Equal(10, z[2].Top - z[1].Bottom);
            Assert.Equal(800, z[0].Height);
            Assert.Equal(1000, z[1].Right);
        }

        [Fact]
        public void EdgeWithoutNeighbourGetsNoGap()
        {
            // Centre zone starts 10% down; nothing is above it, so its top is not pushed down by the gap.
            Rect[] z = ZoneCalculator.Compute(BuiltInLayouts.CenterFocusLayout(), Area(1000, 1000), 20, 0);
            Assert.Equal(100, z[1].Top);
            Assert.Equal(900, z[1].Bottom);
            Assert.Equal(20, z[1].Left - z[0].Right);
            Assert.Equal(20, z[2].Left - z[1].Right);
        }

        [Fact]
        public void OverlappingLayoutsAreComputedIndependently()
        {
            var layout = new SnapLayout("o", "o", new[] { new Zone(0, 0, 1, 1), new Zone(0.25, 0.25, 0.5, 0.5) }, true);
            Rect[] z = ZoneCalculator.Compute(layout, Area(400, 400), 10, 0);
            Assert.Equal(new Rect(0, 0, 400, 400), z[0]);
            Assert.Equal(new Rect(100, 100, 300, 300), z[1]);
        }

        [Fact]
        public void FloatingPointNoiseInSharedEdgesDoesNotMovePixels()
        {
            // 0.1 + 0.2 != 0.3 in binary floating point; the shared edge must still land on one pixel.
            var layout = new SnapLayout("f", "f", new[] { new Zone(0, 0, 0.1 + 0.2, 1), new Zone(0.3, 0, 0.7, 1) });
            for (int w = 50; w < 300; w++)
            {
                Rect[] z = ZoneCalculator.Compute(layout, Area(w, 10), 0, 0);
                Assert.Equal(z[0].Right, z[1].Left);
            }
        }
    }
}
