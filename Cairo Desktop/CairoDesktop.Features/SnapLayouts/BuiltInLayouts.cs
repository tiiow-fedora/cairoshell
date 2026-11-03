using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>The layouts that ship with Cairo Plus, plus a grid generator.</summary>
    public static class BuiltInLayouts
    {
        public const string HalvesLr = "halves-lr";
        public const string HalvesTb = "halves-tb";
        public const string Thirds = "thirds";
        public const string Quarters = "quarters";
        public const string TwoThirdsOneThird = "2-1";
        public const string OneThirdTwoThirds = "1-2";
        public const string MainStack = "main-stack";
        public const string Ultrawide = "ultrawide";
        public const string CenterFocus = "center-focus";

        public static SnapLayout Halves() => new SnapLayout(HalvesLr, "Halves (left/right)", new[] { new Zone(0, 0, 0.5, 1), new Zone(0.5, 0, 0.5, 1) });

        public static SnapLayout HalvesTopBottom() => new SnapLayout(HalvesTb, "Halves (top/bottom)", new[] { new Zone(0, 0, 1, 0.5), new Zone(0, 0.5, 1, 0.5) });

        public static SnapLayout ThirdsLayout() => Grid(Thirds, "Thirds", new double[] { 1, 1, 1 }, new double[] { 1 });

        public static SnapLayout QuartersLayout() => Grid(Quarters, "Quarters", new double[] { 1, 1 }, new double[] { 1, 1 });

        public static SnapLayout TwoThirdsPlusOneThird() => Grid(TwoThirdsOneThird, "2/3 + 1/3", new double[] { 2, 1 }, new double[] { 1 });

        public static SnapLayout OneThirdPlusTwoThirds() => Grid(OneThirdTwoThirds, "1/3 + 2/3", new double[] { 1, 2 }, new double[] { 1 });

        /// <summary>A tall main zone on the left (60%) and two stacked zones on the right.</summary>
        public static SnapLayout MainPlusStack() => new SnapLayout(MainStack, "Main + stack",
            new[] { new Zone(0, 0, 0.6, 1), new Zone(0.6, 0, 0.4, 0.5), new Zone(0.6, 0.5, 0.4, 0.5) });

        /// <summary>25% / 50% / 25% columns for ultrawide monitors.</summary>
        public static SnapLayout UltrawideColumns() => Grid(Ultrawide, "Ultrawide 25/50/25", new double[] { 1, 2, 1 }, new double[] { 1 });

        /// <summary>Side strips plus a centred focus zone that leaves a margin above and below.</summary>
        public static SnapLayout CenterFocusLayout() => new SnapLayout(CenterFocus, "Center focus",
            new[] { new Zone(0, 0, 0.15, 1), new Zone(0.15, 0.1, 0.7, 0.8), new Zone(0.85, 0, 0.15, 1) });

        /// <summary>
        /// Grid with per-column and per-row weights (relative sizes). Zones are ordered row by row, left to right.
        /// Non-positive or non-finite weights count as 1; at most 16 columns/rows are used.
        /// </summary>
        public static SnapLayout Grid(string id, string name, IList<double> columnWeights, IList<double> rowWeights)
        {
            double[] cols = Edges(columnWeights);
            double[] rows = Edges(rowWeights);
            var zones = new List<Zone>();

            for (int r = 0; r < rows.Length - 1; r++)
            {
                for (int c = 0; c < cols.Length - 1; c++)
                {
                    // Zones are built from shared cumulative edges, so neighbours meet at bit-identical fractions.
                    zones.Add(new Zone(cols[c], rows[r], cols[c + 1] - cols[c], rows[r + 1] - rows[r]));
                }
            }

            return new SnapLayout(id, name, zones);
        }

        /// <summary>Equal-sized grid with the given number of columns and rows (each clamped to 1..8).</summary>
        public static SnapLayout Grid(int columns, int rows)
        {
            columns = Math.Max(1, Math.Min(8, columns));
            rows = Math.Max(1, Math.Min(8, rows));
            return Grid($"grid-{columns}x{rows}", $"Grid {columns}x{rows}", Enumerable.Repeat(1.0, columns).ToArray(), Enumerable.Repeat(1.0, rows).ToArray());
        }

        public static IReadOnlyList<SnapLayout> All() => new List<SnapLayout>
        {
            Halves(), HalvesTopBottom(), ThirdsLayout(), QuartersLayout(), TwoThirdsPlusOneThird(), OneThirdPlusTwoThirds(),
            MainPlusStack(), UltrawideColumns(), CenterFocusLayout()
        };

        /// <summary>Cumulative edges 0..1 for the weights (first is exactly 0, last exactly 1).</summary>
        private static double[] Edges(IList<double> weights)
        {
            if (weights == null || weights.Count == 0) return new[] { 0.0, 1.0 };

            int count = Math.Min(weights.Count, 16);
            var w = new double[count];
            double total = 0;
            for (int i = 0; i < count; i++)
            {
                w[i] = double.IsNaN(weights[i]) || double.IsInfinity(weights[i]) || weights[i] <= 0 ? 1 : weights[i];
                total += w[i];
            }

            var edges = new double[count + 1];
            double sum = 0;
            for (int i = 1; i < count; i++)
            {
                sum += w[i - 1];
                edges[i] = sum / total;
            }

            edges[count] = 1.0;
            return edges;
        }
    }
}
