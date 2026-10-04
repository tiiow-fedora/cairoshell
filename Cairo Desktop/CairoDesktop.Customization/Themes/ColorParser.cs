using System;
using System.Globalization;
using System.Windows.Media;

namespace CairoDesktop.Customization.Themes
{
    /// <summary>Parses "#RGB", "#RRGGBB" and "#AARRGGBB" colour strings and does simple colour math.</summary>
    public static class ColorParser
    {
        public static bool TryParse(string text, out Color color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string hex = text.Trim();
            if (hex.StartsWith("#", StringComparison.Ordinal))
            {
                hex = hex.Substring(1);
            }

            if (!uint.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value))
            {
                return false;
            }

            switch (hex.Length)
            {
                case 3:
                    color = Color.FromRgb(Expand((value >> 8) & 0xF), Expand((value >> 4) & 0xF), Expand(value & 0xF));
                    return true;
                case 6:
                    color = Color.FromRgb((byte)(value >> 16), (byte)(value >> 8), (byte)value);
                    return true;
                case 8:
                    color = Color.FromArgb((byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value);
                    return true;
                default:
                    return false;
            }
        }

        public static Color Parse(string text)
        {
            if (!TryParse(text, out Color color))
            {
                throw new FormatException($"'{text}' is not a colour. Use #RGB, #RRGGBB or #AARRGGBB.");
            }

            return color;
        }

        public static string ToHex(Color c)
        {
            return $"#{c.A:X2}{c.R:X2}{c.G:X2}{c.B:X2}";
        }

        public static Color WithAlpha(Color c, byte alpha)
        {
            return Color.FromArgb(alpha, c.R, c.G, c.B);
        }

        /// <summary>Scales the existing alpha by <paramref name="factor"/> (0..1).</summary>
        public static Color ScaleAlpha(Color c, double factor)
        {
            factor = Math.Max(0, Math.Min(1, factor));
            return Color.FromArgb((byte)Math.Round(c.A * factor), c.R, c.G, c.B);
        }

        /// <summary>Linear blend from <paramref name="a"/> (t=0) to <paramref name="b"/> (t=1). Alpha comes from <paramref name="a"/>.</summary>
        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb(a.A,
                (byte)Math.Round(a.R + (b.R - a.R) * t),
                (byte)Math.Round(a.G + (b.G - a.G) * t),
                (byte)Math.Round(a.B + (b.B - a.B) * t));
        }

        /// <summary>Relative luminance (0 = black, 1 = white).</summary>
        public static double Luminance(Color c)
        {
            return (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;
        }

        public static bool IsDark(Color c)
        {
            return Luminance(c) < 0.5;
        }

        private static byte Expand(uint nibble)
        {
            return (byte)(nibble * 17);
        }
    }
}
