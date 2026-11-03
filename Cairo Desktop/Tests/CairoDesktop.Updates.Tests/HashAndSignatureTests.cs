using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace CairoDesktop.Updates.Tests
{
    public class HashAndSignatureTests
    {
        [Fact]
        public void Sha256OfKnownInput()
        {
            // SHA-256("abc")
            Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
                FileHashVerifier.ComputeSha256(Encoding.ASCII.GetBytes("abc")));
        }

        [Fact]
        public void VerifyAcceptsMatchAndIgnoresCase()
        {
            var data = Encoding.UTF8.GetBytes("payload");
            string hash = FileHashVerifier.ComputeSha256(data);
            using var ms = new MemoryStream(data);
            FileHashVerifier.Verify(ms, hash.ToUpperInvariant(), data.Length);
        }

        [Fact]
        public void VerifyRejectsWrongHash()
        {
            var data = Encoding.UTF8.GetBytes("payload");
            string other = FileHashVerifier.ComputeSha256(Encoding.UTF8.GetBytes("tampered"));
            using var ms = new MemoryStream(data);
            Assert.Throws<UpdateException>(() => FileHashVerifier.Verify(ms, other, data.Length));
        }

        [Fact]
        public void VerifyRejectsWrongSize()
        {
            var data = Encoding.UTF8.GetBytes("payload");
            using var ms = new MemoryStream(data);
            Assert.Throws<UpdateException>(() => FileHashVerifier.Verify(ms, FileHashVerifier.ComputeSha256(data), data.Length + 1));
        }

        [Fact]
        public void VerifyRejectsMalformedExpectedHash()
        {
            using var ms = new MemoryStream(new byte[] { 1 });
            Assert.Throws<UpdateException>(() => FileHashVerifier.Verify(ms, "xyz", 1));
        }

        [Theory]
        [InlineData("", false)]
        [InlineData("abc", false)]
        public void IsValidHexRejectsShort(string s, bool expected) => Assert.Equal(expected, FileHashVerifier.IsValidHex(s));

        [Fact]
        public void IsValidHexAcceptsMixedCase() =>
            Assert.True(FileHashVerifier.IsValidHex(new string('a', 32) + new string('F', 32)));

        [Fact]
        public void DigestsEqualHandlesLengthAndCase()
        {
            Assert.True(FileHashVerifier.DigestsEqual("ABCdef", "abcDEF"));
            Assert.False(FileHashVerifier.DigestsEqual("abc", "abcd"));
        }

        [Fact]
        public void ValidSignatureVerifies()
        {
            using var key = new TestKey();
            var data = Encoding.UTF8.GetBytes("manifest");
            Assert.True(key.Verifier.IsValid(data, key.Sign(data)));
        }

        [Fact]
        public void TamperedDataFails()
        {
            using var key = new TestKey();
            var data = Encoding.UTF8.GetBytes("manifest");
            var sig = key.Sign(data);
            data[0] ^= 1;
            Assert.False(key.Verifier.IsValid(data, sig));
            Assert.Throws<UpdateException>(() => key.Verifier.Verify(data, sig));
        }

        [Fact]
        public void TamperedSignatureFails()
        {
            using var key = new TestKey();
            var data = Encoding.UTF8.GetBytes("manifest");
            var sig = key.Sign(data);
            sig[10] ^= 0xFF;
            Assert.False(key.Verifier.IsValid(data, sig));
        }

        [Fact]
        public void SignatureFromDifferentKeyFails()
        {
            using var trusted = new TestKey();
            using var attacker = new TestKey();
            var data = Encoding.UTF8.GetBytes("manifest");
            Assert.False(trusted.Verifier.IsValid(data, attacker.Sign(data)));
        }

        [Fact]
        public void GarbageAndWrongLengthSignaturesFailWithoutThrowing()
        {
            using var key = new TestKey();
            var data = Encoding.UTF8.GetBytes("manifest");
            Assert.False(key.Verifier.IsValid(data, new byte[0]));
            Assert.False(key.Verifier.IsValid(data, new byte[256]));
            Assert.False(key.Verifier.IsValid(data, new byte[5]));
        }

        [Fact]
        public void RejectsWeakKeys()
        {
            using var weak = RSA.Create(1024);
            Assert.Throws<ArgumentException>(() => new ManifestSignatureVerifier(weak.ExportParameters(false)));
        }

        [Fact]
        public void EncodedKeyRoundTrips()
        {
            using var key = new TestKey();
            var p = key.Rsa.ExportParameters(false);
            string encoded = Convert.ToBase64String(p.Modulus!) + ":" + Convert.ToBase64String(p.Exponent!);
            var verifier = ManifestSignatureVerifier.FromEncodedKey(encoded);
            var data = Encoding.UTF8.GetBytes("manifest");
            Assert.True(verifier.IsValid(data, key.Sign(data)));
        }

        [Theory]
        [InlineData("")]
        [InlineData("onlyonepart")]
        [InlineData("a:b:c")]
        [InlineData("!!!:???")]
        public void MalformedEncodedKeyThrowsUpdateException(string encoded)
        {
            Assert.Throws<UpdateException>(() => ManifestSignatureVerifier.FromEncodedKey(encoded));
        }

        [Fact]
        public void DecodeSignatureAcceptsBase64AndRaw()
        {
            using var key = new TestKey();
            var data = Encoding.UTF8.GetBytes("m");
            var sig = key.Sign(data);
            var b64 = Encoding.ASCII.GetBytes(Convert.ToBase64String(sig) + "\n");
            Assert.Equal(sig, ManifestSignatureVerifier.DecodeSignature(b64));
            Assert.Equal(sig, ManifestSignatureVerifier.DecodeSignature(sig));
        }
    }
}
