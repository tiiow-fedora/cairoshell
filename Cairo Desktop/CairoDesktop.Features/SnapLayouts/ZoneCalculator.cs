using System;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>
    /// Turns a fractional layout into pixel rectangles without drift.
    ///
    /// Every distinct zone edge (as a fraction) is rounded to a pixel exactly once, so two zones that share an edge
    /// always agree on its position and the whole layout spans the work area exactly. This is the cumulative form of
    /// largest-remainder rounding: no zone is ever off by more than one pixel from its ideal size and the errors never add up.
    /// A gap is then taken from the shared edge: the left/top zone gives up floor(gap / 2) pixels, the right/bottom zone
    /// gives up the rest, so neighbours are separated by exactly <c>gap</c> pixels even when the gap is odd.
    /// Edges that touch the work area (or have no neighbour) only get the outer gap.
    /// </summary>
    public static class ZoneCalculator
    {
        private const double Tolerance = 1e-9;

        /// <param name="layout">Layout to compute; an invalid layout yields its zones as-is (call Validate first).</param>
        /// <param name="workArea">Usable monitor area in pixels. An empty area yields empty zones at its top-left.</param>
        /// <param name="gap">Pixels between neighbouring zones (negative counts as 0).</param>
        /// <param name="outerGap">Pixels between the zones and the work area edge (negative counts as 0; reduced if the area is tiny).</param>
        /// <remarks>
        /// Zones are never smaller than 1x1 pixel: when the gap is larger than the zone, the zone is clamped to 1 pixel
        /// (and may then touch or overlap its neighbour) instead of getting a negative size.
        /// </remarks>
        public static Rect[] Compute(SnapLayout layout, Rect workArea, int gap, int outerGap)
        {
            if (layout == null) return new Rect[0];

            int count = layout.Zones.Count;
            var result = new Rect[count];

            if (workArea.IsEmpty)
            {
                var empty = new Rect(workArea.Left, workArea.Top, workArea.Left, workArea.Top);
                for (int i = 0; i < count; i++) result[i] = empty;
                return result;
            }

            gap = Math.Max(0, gap);
            outerGap = Math.Max(0, outerGap);
            int maxOuterX = (workArea.Width - 1) / 2;
            int maxOuterY = (workArea.Height - 1) / 2;
            int outer = Math.Min(outerGap, Math.Min(maxOuterX, maxOuterY));
            Rect area = workArea.Inset(outer);

            int gapNear = gap / 2;        // taken by the left/top zone of a shared edge
            int gapFar = gap - gapNear;   // taken by the right/bottom zone

            for (int i = 0; i < count; i++)
            {
                Zone z = layout.Zones[i];
                double fl = Clamp01(Round(z.X));
                double ft = Clamp01(Round(z.Y));
                double fr = Clamp01(Round(z.X + z.W));
                double fb = Clamp01(Round(z.Y + z.H));

                int left = ToPixel(area.Left, area.Width, fl);
                int top = ToPixel(area.Top, area.Height, ft);
                int right = ToPixel(area.Left, area.Width, fr);
                int bottom = ToPixel(area.Top, area.Height, fb);

                if (gap > 0)
                {
                    if (HasNeighbour(layout, i, Side.Left, fl, ft, fb)) left += gapFar;
                    if (HasNeighbour(layout, i, Side.Right, fr, ft, fb)) right -= gapNear;
                    if (HasNeighbour(layout, i, Side.Top, ft, fl, fr)) top += gapFar;
                    if (HasNeighbour(layout, i, Side.Bottom, fb, fl, fr)) bottom -= gapNear;
                }

                Clamp(ref left, ref right, area.Left, area.Right);
                Clamp(ref top, ref bottom, area.Top, area.Bottom);
                result[i] = new Rect(left, top, right, bottom);
            }

            return result;
        }

        /// <summary>The ideal pixel position of a fractional edge: origin + round(fraction * size), half rounding up.</summary>
        private static int ToPixel(int origin, int size, double fraction) => origin + (int)Math.Floor(fraction * size + 0.5);

        private static double Round(double v) => Math.Round(v, 9);

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;

        /// <summary>Keeps at least one pixel of size inside [min, max]. Assumes max - min >= 1.</summary>
        private static void Clamp(ref int start, ref int end, int min, int max)
        {
            if (start < min) start = min;
            if (end > max) end = max;
            if (end - start >= 1) return;

            end = start + 1;
            if (end > max)
            {
                end = max;
                start = max - 1;
            }
        }

        private enum Side { Left, Right, Top, Bottom }

        /// <summary>
        /// Does another zone touch this zone's edge? <paramref name="edge"/> is the edge position (fraction),
        /// [spanStart, spanEnd] is the extent of this zone along the edge.
        /// </summary>
        private static bool HasNeighbour(SnapLayout layout, int self, Side side, double edge, double spanStart, double spanEnd)
        {
            // An edge on the work area border never has a neighbour.
            if ((side == Side.Left || side == Side.Top) && edge <= Tolerance) return false;
            if ((side == Side.Right || side == Side.Bottom) && edge >= 1 - Tolerance) return false;

            for (int j = 0; j < layout.Zones.Count; j++)
            {
                if (j == self) continue;

                Zone o = layout.Zones[j];
                double ol = Round(o.X), ot = Round(o.Y), or = Round(o.X + o.W), ob = Round(o.Y + o.H);

                double opposite;
                double oStart;
                double oEnd;
                switch (side)
                {
                    case Side.Left: opposite = or; oStart = ot; oEnd = ob; break;
                    case Side.Right: opposite = ol; oStart = ot; oEnd = ob; break;
                    case Side.Top: opposite = ob; oStart = ol; oEnd = or; break;
                    default: opposite = ot; oStart = ol; oEnd = or; break;
                }

                if (Math.Abs(opposite - edge) > Tolerance) continue;
                if (Math.Min(spanEnd, oEnd) - Math.Max(spanStart, oStart) > Tolerance) return true;
            }

            return false;
        }
    }
}
