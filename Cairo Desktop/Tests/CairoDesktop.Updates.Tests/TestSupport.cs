using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CairoDesktop.Updates;

namespace CairoDesktop.Updates.Tests
{
    internal sealed class FakeSource : IUpdateSource
    {
        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>();
        public List<string> Requested { get; } = new List<string>();

        public Task<byte[]> GetBytesAsync(Uri uri, long maxBytes, CancellationToken ct)
        {
            Requested.Add(uri.ToString());
            if (!Files.TryGetValue(uri.ToString(), out var bytes)) throw new UpdateException("404 " + uri);
            if (bytes.Length > maxBytes) throw new UpdateException("too large");
            return Task.FromResult(bytes);
        }

        public Task DownloadAsync(Uri uri, Stream destination, long maxBytes, CancellationToken ct)
        {
            Requested.Add(uri.ToString());
            if (!Files.TryGetValue(uri.ToString(), out var bytes)) throw new UpdateException("404 " + uri);
            if (bytes.Length > maxBytes) throw new UpdateException("too large");
            destination.Write(bytes, 0, bytes.Length);
            return Task.CompletedTask;
        }
    }

    internal sealed class TestKey : IDisposable
    {
        public RSA Rsa { get; } = RSA.Create(2048);
        public ManifestSignatureVerifier Verifier { get; }

        public TestKey()
        {
            Verifier = new ManifestSignatureVerifier(Rsa.ExportParameters(false));
        }

        public byte[] Sign(byte[] data) => Rsa.SignData(data, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        public void Dispose() => Rsa.Dispose();
    }

    internal static class ManifestBuilder
    {
        public const string ManifestUrl = "https://updates.example.com/update.json";
        public const string InstallerUrl = "https://updates.example.com/CairoSetup_64bit.exe";

        public static byte[] Json(string version = "0.5.0", byte[]? installer = null, string channel = "stable",
            string? minimum = null, string platform = "x64", string? sha = null, long? size = null, string url = InstallerUrl)
        {
            installer ??= Encoding.UTF8.GetBytes("installer-bytes");
            sha ??= FileHashVerifier.ComputeSha256(installer);
            size ??= installer.Length;
            string min = minimum == null ? "" : "\"minimumVersion\": \"" + minimum + "\",";
            string json = @"{
  ""schemaVersion"": 1,
  ""version"": """ + version + @""",
  ""channel"": """ + channel + @""",
  ""published"": ""2026-11-03T12:00:00Z"",
  " + min + @"
  ""assets"": [
    { ""platform"": """ + platform + @""", ""kind"": ""installer"", ""url"": """ + url + @""", ""sha256"": """ + sha + @""", ""size"": " + size + @" }
  ]
}";
            return Encoding.UTF8.GetBytes(json);
        }

        public static FakeSource Publish(TestKey key, byte[] manifest, byte[]? installer = null, bool base64Signature = true)
        {
            var src = new FakeSource();
            src.Files[ManifestUrl] = manifest;
            byte[] sig = key.Sign(manifest);
            src.Files[ManifestUrl + ".sig"] = base64Signature ? Encoding.ASCII.GetBytes(Convert.ToBase64String(sig)) : sig;
            src.Files[InstallerUrl] = installer ?? Encoding.UTF8.GetBytes("installer-bytes");
            return src;
        }

        public static UpdateCheckOptions Options(string current = "0.4.0") => new UpdateCheckOptions
        {
            ManifestUri = new Uri(ManifestUrl),
            CurrentVersion = SemanticVersion.Parse(current),
        };
    }
}
