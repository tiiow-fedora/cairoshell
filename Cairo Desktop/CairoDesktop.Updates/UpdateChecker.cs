using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CairoDesktop.Updates
{
    /// <summary>Where manifest and installer bytes come from. Faked in tests.</summary>
    public interface IUpdateSource
    {
        Task<byte[]> GetBytesAsync(Uri uri, long maxBytes, CancellationToken ct);
        Task DownloadAsync(Uri uri, Stream destination, long maxBytes, CancellationToken ct);
    }

    public sealed class HttpUpdateSource : IUpdateSource
    {
        private readonly HttpClient _client;

        public HttpUpdateSource(HttpClient? client = null)
        {
            _client = client ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        }

        public async Task<byte[]> GetBytesAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            using var ms = new MemoryStream();
            await DownloadAsync(uri, ms, maxBytes, ct).ConfigureAwait(false);
            return ms.ToArray();
        }

        public async Task DownloadAsync(Uri uri, Stream destination, long maxBytes, CancellationToken ct)
        {
            if (uri.Scheme != Uri.UriSchemeHttps) throw new UpdateException("Updates must be fetched over https.");
            using var response = await _client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long len && len > maxBytes)
                throw new UpdateException("The server's file is larger than allowed.");
            using var input = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, 0, buffer.Length, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > maxBytes) throw new UpdateException("The server's file is larger than allowed.");
                await destination.WriteAsync(buffer, 0, read, ct).ConfigureAwait(false);
            }
        }
    }

    public sealed class UpdateCheckOptions
    {
        /// <summary>URL of update.json; the signature is expected at the same URL plus ".sig".</summary>
        public Uri ManifestUri { get; set; } = null!;
        public SemanticVersion CurrentVersion { get; set; } = null!;
        /// <summary>Platform id as used in manifest assets, e.g. "x64" or "arm64".</summary>
        public string Platform { get; set; } = "x64";
        /// <summary>"installer" or "portable".</summary>
        public string Kind { get; set; } = "installer";
        public bool IncludePrereleases { get; set; }
        public long MaxManifestBytes { get; set; } = 256 * 1024;
        public long MaxDownloadBytes { get; set; } = 500L * 1024 * 1024;
    }

    public enum UpdateCheckStatus { UpToDate, UpdateAvailable }

    public sealed class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; }
        public UpdateManifest? Manifest { get; }
        public UpdateAsset? Asset { get; }
        /// <summary>True when the running version is below the manifest's minimumVersion.</summary>
        public bool Required { get; }

        internal UpdateCheckResult(UpdateCheckStatus status, UpdateManifest? manifest, UpdateAsset? asset, bool required)
        {
            Status = status;
            Manifest = manifest;
            Asset = asset;
            Required = required;
        }
    }

    public sealed class UpdateChecker
    {
        private readonly IUpdateSource _source;
        private readonly ManifestSignatureVerifier _verifier;

        public UpdateChecker(IUpdateSource source, ManifestSignatureVerifier verifier)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _verifier = verifier ?? throw new ArgumentNullException(nameof(verifier));
        }

        /// <summary>
        /// Downloads and verifies the manifest, then reports whether a newer release exists for this platform.
        /// Throws <see cref="UpdateException"/> if the signature is bad, the manifest is malformed, or the
        /// manifest offers no asset for this platform. Never downgrades.
        /// </summary>
        public async Task<UpdateCheckResult> CheckAsync(UpdateCheckOptions options, CancellationToken ct = default)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.ManifestUri == null || options.CurrentVersion == null) throw new ArgumentException("ManifestUri and CurrentVersion are required.", nameof(options));
            if (options.ManifestUri.Scheme != Uri.UriSchemeHttps) throw new UpdateException("The update manifest URL must use https.");

            byte[] manifestBytes = await _source.GetBytesAsync(options.ManifestUri, options.MaxManifestBytes, ct).ConfigureAwait(false);
            var sigUri = new UriBuilder(options.ManifestUri) { Path = options.ManifestUri.AbsolutePath + ".sig" }.Uri;
            byte[] sigFile = await _source.GetBytesAsync(sigUri, 16 * 1024, ct).ConfigureAwait(false);

            // Verify before parsing: unauthenticated bytes never reach the JSON parser.
            _verifier.Verify(manifestBytes, ManifestSignatureVerifier.DecodeSignature(sigFile));
            var manifest = UpdateManifest.Parse(manifestBytes);

            if (manifest.Channel != "stable" && !options.IncludePrereleases)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, manifest, null, false);
            if (manifest.Version.IsPrerelease && !options.IncludePrereleases)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, manifest, null, false);

            if (manifest.Version <= options.CurrentVersion)
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, manifest, null, false);

            var asset = manifest.Assets.FirstOrDefault(a =>
                string.Equals(a.Platform, options.Platform, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(a.Kind, options.Kind, StringComparison.OrdinalIgnoreCase));
            if (asset == null)
                throw new UpdateException("The release " + manifest.Version + " has no " + options.Kind + " for " + options.Platform + ".");

            bool required = manifest.MinimumVersion != null && options.CurrentVersion < manifest.MinimumVersion;
            return new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, manifest, asset, required);
        }

        /// <summary>
        /// Downloads the asset into <paramref name="destinationDirectory"/> and verifies size and SHA-256.
        /// The file is written under a temporary name and only renamed once it verifies; on failure
        /// nothing is left behind.
        /// </summary>
        public async Task<string> DownloadAsync(UpdateCheckResult result, UpdateCheckOptions options, string destinationDirectory, CancellationToken ct = default)
        {
            if (result?.Asset == null) throw new ArgumentException("No update to download.", nameof(result));
            var uri = new Uri(result.Asset.Url);
            string fileName = SafeFileName(uri);
            Directory.CreateDirectory(destinationDirectory);
            string finalPath = Path.Combine(destinationDirectory, fileName);
            string tempPath = finalPath + ".part";

            try
            {
                long limit = Math.Min(options.MaxDownloadBytes, result.Asset.Size);
                using (var fs = new FileStream(tempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
                {
                    await _source.DownloadAsync(uri, fs, limit, ct).ConfigureAwait(false);
                    fs.Flush();
                    fs.Position = 0;
                    FileHashVerifier.Verify(fs, result.Asset.Sha256, result.Asset.Size);
                }
                if (File.Exists(finalPath)) File.Delete(finalPath);
                File.Move(tempPath, finalPath);
                return finalPath;
            }
            catch
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { }
                throw;
            }
        }

        internal static string SafeFileName(Uri uri)
        {
            string name = Path.GetFileName(uri.AbsolutePath);
            name = Uri.UnescapeDataString(name ?? "");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c.ToString(), "");
            name = name.Trim('.', ' ');
            if (name.Length == 0) throw new UpdateException("The update URL does not name a file.");
            return name;
        }
    }
}
