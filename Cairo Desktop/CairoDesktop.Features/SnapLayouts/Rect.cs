using System;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>
    /// Integer rectangle in pixels. Half-open like a Win32 RECT: Left/Top are inside, Right/Bottom are one past the last pixel.
    /// </summary>
    public readonly struct Rect : IEquatable<Rect>
    {
        public Rect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }

        public int Left { get; }
        public int Top { get; }
        public int Right { get; }
        public int Bottom { get; }

        public int Width => Right - Left;
        public int Height => Bottom - Top;

        /// <summary>True when the rectangle has no pixels (zero or negative width or height).</summary>
        public bool IsEmpty => Right <= Left || Bottom <= Top;

        public long Area => IsEmpty ? 0 : (long)Width * Height;

        public double CenterX => (Left + (double)Right) / 2.0;
        public double CenterY => (Top + (double)Bottom) / 2.0;

        public static Rect FromXywh(int x, int y, int width, int height) => new Rect(x, y, x + width, y + height);

        public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

        public bool Contains(Rect other) => !other.IsEmpty && other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

        public bool IntersectsWith(Rect other) => !Intersect(this, other).IsEmpty;

        /// <summary>Overlap of two rectangles; an empty rectangle at (0,0) when they do not overlap.</summary>
        public static Rect Intersect(Rect a, Rect b)
        {
            int l = Math.Max(a.Left, b.Left);
            int t = Math.Max(a.Top, b.Top);
            int r = Math.Min(a.Right, b.Right);
            int bt = Math.Min(a.Bottom, b.Bottom);
            return r <= l || bt <= t ? default : new Rect(l, t, r, bt);
        }

        /// <summary>Shrinks by the given amounts per side (negative values grow the rectangle). Never returns negative size.</summary>
        public Rect Inset(int left, int top, int right, int bottom)
        {
            int l = Left + left;
            int t = Top + top;
            int r = Right - right;
            int b = Bottom - bottom;
            if (r < l) r = l;
            if (b < t) b = t;
            return new Rect(l, t, r, b);
        }

        public Rect Inset(int all) => Inset(all, all, all, all);

        /// <summary>True when every edge differs by at most <paramref name="tolerance"/> pixels.</summary>
        public bool NearlyEquals(Rect other, int tolerance) =>
            Math.Abs(Left - other.Left) <= tolerance && Math.Abs(Top - other.Top) <= tolerance &&
            Math.Abs(Right - other.Right) <= tolerance && Math.Abs(Bottom - other.Bottom) <= tolerance;

        public bool Equals(Rect other) => Left == other.Left && Top == other.Top && Right == other.Right && Bottom == other.Bottom;
        public override bool Equals(object obj) => obj is Rect other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Left;
                h = (h * 397) ^ Top;
                h = (h * 397) ^ Right;
                return (h * 397) ^ Bottom;
            }
        }

        public static bool operator ==(Rect a, Rect b) => a.Equals(b);
        public static bool operator !=(Rect a, Rect b) => !a.Equals(b);

        public override string ToString() => $"[{Left},{Top} {Width}x{Height}]";
    }
}
