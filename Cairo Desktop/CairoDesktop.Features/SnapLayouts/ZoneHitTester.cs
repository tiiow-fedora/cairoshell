using System;

namespace CairoDesktop.Features.SnapLayouts
{
    public enum Direction { Left, Right, Up, Down }

    /// <summary>Geometry queries over the pixel zones produced by <see cref="ZoneCalculator"/>. All return -1 when nothing matches.</summary>
    public static class ZoneHitTester
    {
        /// <summary>
        /// The zone under a point. A point in a gap (or up to <paramref name="tolerance"/> pixels outside a zone) belongs to the
        /// closest zone within tolerance; ties go to the lower index. Points inside a zone always win over tolerance matches.
        /// </summary>
        public static int HitTest(Rect[] zones, int x, int y, int tolerance = 0)
        {
            if (zones == null) return -1;

            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i].Contains(x, y)) return i;
            }

            if (tolerance <= 0) return -1;

            int best = -1;
            long bestDist = long.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                if (zones[i].IsEmpty) continue;
                long dx = Math.Max(Math.Max(zones[i].Left - x, x - (zones[i].Right - 1)), 0);
                long dy = Math.Max(Math.Max(zones[i].Top - y, y - (zones[i].Bottom - 1)), 0);
                if (dx > tolerance || dy > tolerance) continue;

                long dist = dx * dx + dy * dy;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>
        /// The zone a window most likely belongs to: largest intersection area; ties (and windows overlapping nothing)
        /// go to the zone whose centre is closest to the window's centre, then the lower index.
        /// </summary>
        public static int NearestZone(Rect[] zones, Rect window)
        {
            if (zones == null || zones.Length == 0) return -1;

            int best = -1;
            long bestArea = -1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < zones.Length; i++)
            {
                long area = Rect.Intersect(zones[i], window).Area;
                double dist = Distance2(zones[i].CenterX, zones[i].CenterY, window.CenterX, window.CenterY);
                if (area > bestArea || (area == bestArea && dist < bestDist))
                {
                    best = i;
                    bestArea = area;
                    bestDist = dist;
                }
            }

            return best;
        }

        /// <summary>
        /// Zone to move to from <paramref name="current"/> in a direction: the nearest zone whose centre lies strictly in that
        /// half-plane. With <paramref name="wrap"/> and no such zone, continues from the opposite side (the zone furthest
        /// back, closest across). Returns <paramref name="current"/> when there is nowhere to go. An invalid current index
        /// behaves like <see cref="EdgeZone"/>.
        /// </summary>
        public static int Navigate(Rect[] zones, int current, Direction direction, bool wrap)
        {
            if (zones == null || zones.Length == 0) return -1;
            if (current < 0 || current >= zones.Length) return EdgeZone(zones, direction);

            double cx = zones[current].CenterX, cy = zones[current].CenterY;
            int best = -1;
            double bestDist = double.MaxValue;

            for (int i = 0; i < zones.Length; i++)
            {
                if (i == current) continue;
                if (!InHalfPlane(zones[i], cx, cy, direction)) continue;

                double dist = Distance2(zones[i].CenterX, zones[i].CenterY, cx, cy);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            if (best >= 0 || !wrap) return best >= 0 ? best : current;

            // Wrap: among the zones on the opposite side, the one furthest back; ties by closeness across the axis.
            // A layout with a single row (or column) therefore doesn't wrap vertically (or horizontally).
            int wrapped = -1;
            double bestAlong = double.MaxValue;
            double bestAcross = double.MaxValue;
            for (int i = 0; i < zones.Length; i++)
            {
                if (!InHalfPlane(zones[i], cx, cy, Opposite(direction))) continue;
                double along = Along(zones[i], direction);
                double across = direction == Direction.Left || direction == Direction.Right
                    ? Math.Abs(zones[i].CenterY - cy)
                    : Math.Abs(zones[i].CenterX - cx);
                if (along < bestAlong - 1e-9 || (Math.Abs(along - bestAlong) <= 1e-9 && across < bestAcross))
                {
                    bestAlong = along;
                    bestAcross = across;
                    wrapped = i;
                }
            }

            return wrapped >= 0 ? wrapped : current;
        }

        /// <summary>The zone furthest toward a direction (e.g. Left: smallest centre X); ties go to the lower index.</summary>
        public static int EdgeZone(Rect[] zones, Direction direction)
        {
            if (zones == null || zones.Length == 0) return -1;

            int best = 0;
            double bestScore = Toward(zones[0], direction);
            for (int i = 1; i < zones.Length; i++)
            {
                double score = Toward(zones[i], direction);
                if (score > bestScore + 1e-9)
                {
                    bestScore = score;
                    best = i;
                }
            }

            return best;
        }

        /// <summary>Reading-order navigation (index + 1), for "next zone" hotkeys.</summary>
        public static int Next(Rect[] zones, int current, bool wrap)
        {
            if (zones == null || zones.Length == 0) return -1;
            if (current < 0) return 0;
            if (current + 1 < zones.Length) return current + 1;
            return wrap ? 0 : current;
        }

        /// <summary>Reading-order navigation (index - 1), for "previous zone" hotkeys.</summary>
        public static int Previous(Rect[] zones, int current, bool wrap)
        {
            if (zones == null || zones.Length == 0) return -1;
            if (current < 0 || current >= zones.Length) return zones.Length - 1;
            if (current > 0) return current - 1;
            return wrap ? zones.Length - 1 : current;
        }

        private static Direction Opposite(Direction d)
        {
            switch (d)
            {
                case Direction.Left: return Direction.Right;
                case Direction.Right: return Direction.Left;
                case Direction.Up: return Direction.Down;
                default: return Direction.Up;
            }
        }

        private static bool InHalfPlane(Rect r, double cx, double cy, Direction d)
        {
            switch (d)
            {
                case Direction.Left: return r.CenterX < cx;
                case Direction.Right: return r.CenterX > cx;
                case Direction.Up: return r.CenterY < cy;
                default: return r.CenterY > cy;
            }
        }

        /// <summary>Position along the travel axis, growing in the direction of travel.</summary>
        private static double Toward(Rect r, Direction d)
        {
            switch (d)
            {
                case Direction.Left: return -r.CenterX;
                case Direction.Right: return r.CenterX;
                case Direction.Up: return -r.CenterY;
                default: return r.CenterY;
            }
        }

        /// <summary>Position along the travel axis measured from the starting side (smaller = further back).</summary>
        private static double Along(Rect r, Direction d) => Toward(r, d);

        private static double Distance2(double ax, double ay, double bx, double by)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy;
        }
    }
}
