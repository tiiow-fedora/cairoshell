using System;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>
    /// Width of the invisible resize border on each side of a window: how far GetWindowRect extends beyond the
    /// visible frame reported by DWMWA_EXTENDED_FRAME_BOUNDS. Windows 10/11 typically report 7,0,7,7 at 100% DPI.
    /// Values may be negative or asymmetric (some windows draw outside their window rect, or have no shadow border at all).
    /// </summary>
    public readonly struct FrameInsets
    {
        public FrameInsets(int left, int top, int right, int bottom)
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

        public static FrameInsets None => default;

        /// <summary>
        /// Insets between the window rectangle (what SetWindowPos positions) and the visible frame.
        /// Returns None when the visible frame is empty (the DWM query failed) or implausible (any inset beyond
        /// <paramref name="maxPlausible"/> pixels, e.g. the two rectangles belong to different DPI contexts).
        /// </summary>
        public static FrameInsets Measure(Rect extendedFrameBounds, Rect windowRect, int maxPlausible = 100)
        {
            if (extendedFrameBounds.IsEmpty || windowRect.IsEmpty) return None;

            var insets = new FrameInsets(
                extendedFrameBounds.Left - windowRect.Left,
                extendedFrameBounds.Top - windowRect.Top,
                windowRect.Right - extendedFrameBounds.Right,
                windowRect.Bottom - extendedFrameBounds.Bottom);

            int limit = Math.Max(0, maxPlausible);
            if (Math.Abs(insets.Left) > limit || Math.Abs(insets.Top) > limit || Math.Abs(insets.Right) > limit || Math.Abs(insets.Bottom) > limit)
            {
                return None;
            }

            return insets;
        }

        /// <summary>
        /// The rectangle to pass to SetWindowPos so that the <em>visible</em> frame lands exactly on <paramref name="desired"/>:
        /// grow the target by the invisible border on each side.
        /// </summary>
        public static Rect Compensate(Rect desired, Rect extendedFrameBounds, Rect windowRect)
        {
            return Measure(extendedFrameBounds, windowRect).Apply(desired);
        }

        /// <summary>Grows <paramref name="visible"/> outward by the insets (negative insets shrink it).</summary>
        public Rect Apply(Rect visible)
        {
            if (visible.IsEmpty) return visible;
            return new Rect(visible.Left - Left, visible.Top - Top, visible.Right + Right, visible.Bottom + Bottom);
        }
    }
}
