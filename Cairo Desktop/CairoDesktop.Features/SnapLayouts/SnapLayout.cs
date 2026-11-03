using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>A zone as fractions of the work area (all values 0..1). Right/Bottom are X+W / Y+H.</summary>
    public readonly struct Zone
    {
        public Zone(double x, double y, double w, double h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public double X { get; }
        public double Y { get; }
        public double W { get; }
        public double H { get; }
        public double Right => X + W;
        public double Bottom => Y + H;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0},{1} {2}x{3})", X, Y, W, H);
    }

    /// <summary>A named set of zones. Construction never throws; call <see cref="Validate"/> before trusting a layout.</summary>
    public sealed class SnapLayout
    {
        public const double Epsilon = 1e-6;
        public const int MaxZones = 64;

        public SnapLayout(string id, string name, IEnumerable<Zone> zones, bool allowOverlap = false)
        {
            Id = (id ?? "").Trim();
            Name = string.IsNullOrWhiteSpace(name) ? Id : name.Trim();
            var list = new List<Zone>();
            if (zones != null) list.AddRange(zones);
            Zones = list;
            AllowOverlap = allowOverlap;
        }

        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<Zone> Zones { get; }
        public bool AllowOverlap { get; }

        public bool IsValid => Validate().Count == 0;

        /// <summary>Returns human-readable problems; empty when the layout is usable.</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();

            if (Id.Length == 0)
            {
                errors.Add("layout has no id.");
            }
            else
            {
                foreach (char c in Id)
                {
                    if (!(char.IsLetterOrDigit(c) || c == '-' || c == '_'))
                    {
                        errors.Add($"layout id '{Id}' may only contain letters, digits, '-' and '_'.");
                        break;
                    }
                }
            }

            if (Zones.Count == 0) errors.Add($"layout '{Id}' has no zones.");
            if (Zones.Count > MaxZones) errors.Add($"layout '{Id}' has {Zones.Count} zones; the maximum is {MaxZones}.");

            bool zonesOk = true;
            for (int i = 0; i < Zones.Count && i < MaxZones; i++)
            {
                Zone z = Zones[i];
                if (!IsFinite(z.X) || !IsFinite(z.Y) || !IsFinite(z.W) || !IsFinite(z.H))
                {
                    errors.Add($"layout '{Id}' zone {i}: values must be finite numbers.");
                    zonesOk = false;
                    continue;
                }

                if (z.W <= Epsilon || z.H <= Epsilon)
                {
                    errors.Add($"layout '{Id}' zone {i}: width and height must be greater than 0.");
                    zonesOk = false;
                    continue;
                }

                if (z.X < -Epsilon || z.Y < -Epsilon || z.Right > 1 + Epsilon || z.Bottom > 1 + Epsilon)
                {
                    errors.Add($"layout '{Id}' zone {i} {z} lies outside the 0..1 work area.");
                    zonesOk = false;
                }
            }

            if (!AllowOverlap && zonesOk && Zones.Count <= MaxZones)
            {
                for (int i = 0; i < Zones.Count; i++)
                {
                    for (int j = i + 1; j < Zones.Count; j++)
                    {
                        if (Overlaps(Zones[i], Zones[j]))
                        {
                            errors.Add($"layout '{Id}' zones {i} and {j} overlap (set allowOverlap to permit this).");
                        }
                    }
                }
            }

            return errors;
        }

        internal static bool Overlaps(Zone a, Zone b)
        {
            double w = Math.Min(a.Right, b.Right) - Math.Max(a.X, b.X);
            double h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Y, b.Y);
            return w > Epsilon && h > Epsilon;
        }

        private static bool IsFinite(double v) => !double.IsNaN(v) && !double.IsInfinity(v);

        public override string ToString() => $"{Id} ({Zones.Count} zones)";
    }
}
