using System;
using System.Linq;
using CairoDesktop.Features.SnapLayouts;
using Xunit;

namespace CairoDesktop.Features.Tests.SnapLayouts
{
    /// <summary>Regression tests from the adversarial review of the snap layouts library.</summary>
    public class SnapReviewTests
    {
        [Fact]
        public void FrameInsetsMeasureDoesNotThrowOnExtremeDifference()
        {
            var window = new Rect(0, 0, 100, 100);
            var frame = new Rect(int.MinValue, 0, 50, 50);
            FrameInsets i = FrameInsets.Measure(frame, window);
            Assert.Equal(FrameInsets.None, i);
        }

        [Fact]
        public void FrameInsetsApplyNeverInverts()
        {
            var insets = new FrameInsets(-7, -7, -7, -7);
            Rect r = insets.Apply(new Rect(100, 100, 105, 105));
            Assert.False(r.IsEmpty);
            Assert.True(r.Width >= 1 && r.Height >= 1);
        }

        [Fact]
        public void ZonesTileHugeWorkArea()
        {
            var layout = BuiltInLayouts.Halves();
            Rect[] z = ZoneCalculator.Compute(layout, new Rect(-2000000000, 0, 2000000000, 100), 0, 0);
            Assert.Equal(-2000000000, z[0].Left);
            Assert.Equal(z[0].Right, z[1].Left);
            Assert.Equal(2000000000, z[1].Right);
        }

        [Fact]
        public void HugeGapDoesNotThrowOrInvert()
        {
            var layout = BuiltInLayouts.ThirdsLayout();
            Rect[] z = ZoneCalculator.Compute(layout, new Rect(0, 0, 1000, 1000), int.MaxValue, int.MaxValue);
            foreach (Rect r in z) Assert.True(r.Width >= 1 && r.Height >= 1);
        }

        [Theory]
        [InlineData("Win+Alt+PageDown", "Win+Alt+PgDn")]
        [InlineData("Win+Alt+Next", "Win+Alt+PageDown")]
        [InlineData("Win+Alt+Enter", "Win+Alt+Return")]
        [InlineData("Win+Alt+Esc", "Win+Alt+Escape")]
        [InlineData("Win+Alt+Del", "Win+Alt+Delete")]
        [InlineData("Win+Alt+Backspace", "Win+Alt+Back")]
        [InlineData("Win+Alt+1", "Win+Alt+D1")]
        [InlineData("Win+Alt+Plus", "Win+Alt+=")]
        [InlineData("Win+Alt+Minus", "Win+Alt+-")]
        public void CanonicalTreatsRealParserAliasesAsEqual(string a, string b)
        {
            Assert.Equal(HotkeySyntax.Canonical(a), HotkeySyntax.Canonical(b));
        }

        [Fact]
        public void AliasedDuplicateHotkeyIsDetected()
        {
            var r = SnapConfigParser.ParseSection("{ \"hotkeys\": { \"nextZone\": \"Win+Alt+PgUp\" } }");
            Assert.Equal("Win+Alt+PgUp", r.Config.Hotkeys["nextZone"]);
            Assert.Equal("", r.Config.Hotkeys["prevZone"]); // default PageUp is the same key as PgUp
        }

        [Theory]
        [InlineData("Pause")]
        [InlineData("Scroll")]
        [InlineData("VolumeUp")]
        [InlineData("MediaPlayPause")]
        [InlineData("LaunchMail")]
        [InlineData("BrowserBack")]
        public void KeysTheRealParserAllowsBareAreAllowed(string key)
        {
            Assert.Null(HotkeySyntax.Check(key));
        }
    }
}
