using System;
using System.Security.Cryptography;

namespace CairoDesktop.Updates
{
    /// <summary>
    /// Verifies a detached RSA signature (PKCS#1 v1.5, SHA-256) over the exact bytes of the manifest.
    /// The trusted public key is embedded in the app, so a compromised download host cannot
    /// publish an update on its own.
    /// </summary>
    public sealed class ManifestSignatureVerifier
    {
        private const int MinimumKeyBits = 2048;
        private readonly RSAParameters _publicKey;

        public ManifestSignatureVerifier(RSAParameters publicKey)
        {
            if (publicKey.Modulus == null || publicKey.Exponent == null)
                throw new ArgumentException("Public key must contain modulus and exponent.", nameof(publicKey));
            if (publicKey.Modulus.Length * 8 < MinimumKeyBits)
                throw new ArgumentException("Public key must be at least " + MinimumKeyBits + " bits.", nameof(publicKey));
            _publicKey = new RSAParameters { Modulus = publicKey.Modulus, Exponent = publicKey.Exponent };
        }

        /// <summary>
        /// Creates a verifier from "base64(modulus):base64(exponent)", the format
        /// printed by tools/packaging/New-UpdateKey.ps1.
        /// </summary>
        public static ManifestSignatureVerifier FromEncodedKey(string encoded)
        {
            if (string.IsNullOrWhiteSpace(encoded)) throw new UpdateException("No update public key is configured.");
            string[] parts = encoded.Trim().Split(':');
            if (parts.Length != 2) throw new UpdateException("Update public key must look like 'modulus:exponent' (base64).");
            try
            {
                return new ManifestSignatureVerifier(new RSAParameters
                {
                    Modulus = Convert.FromBase64String(parts[0]),
                    Exponent = Convert.FromBase64String(parts[1]),
                });
            }
            catch (FormatException ex)
            {
                throw new UpdateException("Update public key is not valid base64.", ex);
            }
            catch (ArgumentException ex)
            {
                throw new UpdateException(ex.Message, ex);
            }
        }

        public bool IsValid(byte[] manifestBytes, byte[] signature)
        {
            if (manifestBytes == null || signature == null || signature.Length != _publicKey.Modulus!.Length) return false;
            try
            {
                using var rsa = RSA.Create();
                rsa.ImportParameters(_publicKey);
                return rsa.VerifyData(manifestBytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        /// <summary>Accepts the signature as raw bytes or as base64 text (what the signing script writes).</summary>
        public static byte[] DecodeSignature(byte[] fileContents)
        {
            string text = System.Text.Encoding.ASCII.GetString(fileContents).Trim();
            try { return Convert.FromBase64String(text); }
            catch (FormatException) { return fileContents; }
        }

        public void Verify(byte[] manifestBytes, byte[] signature)
        {
            if (!IsValid(manifestBytes, signature))
                throw new UpdateException("The update manifest signature is invalid. The update was ignored.");
        }
    }
}
