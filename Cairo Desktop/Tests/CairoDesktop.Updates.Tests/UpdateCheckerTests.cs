using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace CairoDesktop.Updates.Tests
{
    public class UpdateCheckerTests : IDisposable
    {
        private readonly TestKey _key = new TestKey();
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "cairo-upd-" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            _key.Dispose();
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        private UpdateChecker Checker(FakeSource src) => new UpdateChecker(src, _key.Verifier);

        [Fact]
        public async Task NewerVersionIsOffered()
        {
            var r = await Checker(ManifestBuilder.Publish(_key, ManifestBuilder.Json("0.5.0"))).CheckAsync(ManifestBuilder.Options("0.4.0"));
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, r.Status);
            Assert.Equal(ManifestBuilder.InstallerUrl, r.Asset!.Url);
            Assert.False(r.Required);
        }

        [Theory]
        [InlineData("0.5.0")]
        [InlineData("0.6.0")]
        public async Task SameOrOlderIsUpToDate(string current)
        {
            var r = await Checker(ManifestBuilder.Publish(_key, ManifestBuilder.Json("0.5.0"))).CheckAsync(ManifestBuilder.Options(current));
            Assert.Equal(UpdateCheckStatus.UpToDate, r.Status);
            Assert.Null(r.Asset);
        }

        [Fact]
        public async Task SignatureIsFetchedFromManifestUrlPlusSig()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            await Checker(src).CheckAsync(ManifestBuilder.Options());
            Assert.Equal(new[] { ManifestBuilder.ManifestUrl, ManifestBuilder.ManifestUrl + ".sig" }, src.Requested);
        }

        [Fact]
        public async Task RawBinarySignatureIsAccepted()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json(), base64Signature: false);
            var r = await Checker(src).CheckAsync(ManifestBuilder.Options());
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, r.Status);
        }

        [Fact]
        public async Task ManifestSignedByWrongKeyIsRejected()
        {
            using var attacker = new TestKey();
            var src = ManifestBuilder.Publish(attacker, ManifestBuilder.Json("9.9.9"));
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(ManifestBuilder.Options()));
        }

        [Fact]
        public async Task ManifestModifiedAfterSigningIsRejected()
        {
            var original = ManifestBuilder.Json("0.5.0");
            var src = ManifestBuilder.Publish(_key, original);
            src.Files[ManifestBuilder.ManifestUrl] = ManifestBuilder.Json("0.5.0", url: "https://evil.example.com/x.exe");
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(ManifestBuilder.Options()));
        }

        [Fact]
        public async Task MissingSignatureFileFails()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            src.Files.Remove(ManifestBuilder.ManifestUrl + ".sig");
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(ManifestBuilder.Options()));
        }

        [Fact]
        public async Task UnsignedGarbageNeverReachesParser()
        {
            // A validly signed non-JSON blob should fail in the parser, an unsigned one in the verifier;
            // either way the caller sees an UpdateException and not a JsonException.
            var src = ManifestBuilder.Publish(_key, Encoding.UTF8.GetBytes("<html>captive portal</html>"));
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(ManifestBuilder.Options()));
        }

        [Fact]
        public async Task HttpManifestUrlIsRefusedBeforeAnyRequest()
        {
            var src = new FakeSource();
            var opts = ManifestBuilder.Options();
            opts.ManifestUri = new Uri("http://updates.example.com/update.json");
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(opts));
            Assert.Empty(src.Requested);
        }

        [Fact]
        public async Task BetaChannelIgnoredUnlessOptedIn()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json("0.6.0", channel: "beta"));
            var off = await Checker(src).CheckAsync(ManifestBuilder.Options());
            Assert.Equal(UpdateCheckStatus.UpToDate, off.Status);

            var opts = ManifestBuilder.Options();
            opts.IncludePrereleases = true;
            var on = await Checker(src).CheckAsync(opts);
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, on.Status);
        }

        [Fact]
        public async Task PrereleaseVersionIgnoredUnlessOptedIn()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json("0.6.0-rc.1"));
            var r = await Checker(src).CheckAsync(ManifestBuilder.Options());
            Assert.Equal(UpdateCheckStatus.UpToDate, r.Status);
        }

        [Fact]
        public async Task MissingPlatformAssetThrows()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json(platform: "arm64"));
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(ManifestBuilder.Options()));
        }

        [Fact]
        public async Task PlatformMatchIsCaseInsensitive()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json(platform: "X64"));
            var r = await Checker(src).CheckAsync(ManifestBuilder.Options());
            Assert.Equal(UpdateCheckStatus.UpdateAvailable, r.Status);
        }

        [Fact]
        public async Task MinimumVersionMarksUpdateRequired()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json("0.5.0", minimum: "0.4.5"));
            Assert.True((await Checker(src).CheckAsync(ManifestBuilder.Options("0.4.0"))).Required);
            Assert.False((await Checker(src).CheckAsync(ManifestBuilder.Options("0.4.5"))).Required);
        }

        [Fact]
        public async Task OversizedManifestIsRejected()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            var opts = ManifestBuilder.Options();
            opts.MaxManifestBytes = 10;
            await Assert.ThrowsAsync<UpdateException>(() => Checker(src).CheckAsync(opts));
        }

        [Fact]
        public async Task DownloadVerifiesAndMovesIntoPlace()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            var checker = Checker(src);
            var opts = ManifestBuilder.Options();
            var result = await checker.CheckAsync(opts);
            string path = await checker.DownloadAsync(result, opts, _dir);
            Assert.Equal(Path.Combine(_dir, "CairoSetup_64bit.exe"), path);
            Assert.Equal("installer-bytes", File.ReadAllText(path));
            Assert.Empty(Directory.GetFiles(_dir, "*.part"));
        }

        [Fact]
        public async Task DownloadWithTamperedPayloadIsRejectedAndCleanedUp()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            src.Files[ManifestBuilder.InstallerUrl] = Encoding.UTF8.GetBytes("evil-bytes!!!"); // different content
            var checker = Checker(src);
            var opts = ManifestBuilder.Options();
            var result = await checker.CheckAsync(opts);
            await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(result, opts, _dir));
            Assert.Empty(Directory.GetFiles(_dir));
        }

        [Fact]
        public async Task DownloadSameLengthButDifferentContentIsRejected()
        {
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            src.Files[ManifestBuilder.InstallerUrl] = Encoding.UTF8.GetBytes("installer-bytez"); // same length
            var checker = Checker(src);
            var opts = ManifestBuilder.Options();
            var result = await checker.CheckAsync(opts);
            await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(result, opts, _dir));
            Assert.Empty(Directory.GetFiles(_dir));
        }

        [Fact]
        public async Task FailedDownloadDoesNotReplaceExistingFile()
        {
            Directory.CreateDirectory(_dir);
            string existing = Path.Combine(_dir, "CairoSetup_64bit.exe");
            File.WriteAllText(existing, "previous-good");
            var src = ManifestBuilder.Publish(_key, ManifestBuilder.Json());
            src.Files[ManifestBuilder.InstallerUrl] = Encoding.UTF8.GetBytes("tampered-data!");
            var checker = Checker(src);
            var opts = ManifestBuilder.Options();
            var result = await checker.CheckAsync(opts);
            await Assert.ThrowsAsync<UpdateException>(() => checker.DownloadAsync(result, opts, _dir));
            Assert.Equal("previous-good", File.ReadAllText(existing));
        }

        [Theory]
        [InlineData("https://h/a/b/setup.exe", "setup.exe")]
        [InlineData("https://h/a/..%2f..%2fevil.exe", "evil.exe")]
        [InlineData("https://h/a/set%20up.exe?x=1", "set up.exe")]
        public void SafeFileNameStripsPathTricks(string url, string expected)
        {
            string name = UpdateChecker.SafeFileName(new Uri(url));
            Assert.Equal(expected, name);
        }

        [Fact]
        public void SafeFileNameRejectsDirectoryUrls()
        {
            Assert.Throws<UpdateException>(() => UpdateChecker.SafeFileName(new Uri("https://h/dir/")));
        }
    }
}
