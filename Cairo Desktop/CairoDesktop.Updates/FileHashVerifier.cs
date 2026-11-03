using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace CairoDesktop.Updates
{
    public static class FileHashVerifier
    {
        public static bool IsValidHex(string s)
        {
            if (s == null || s.Length != 64) return false;
            foreach (char c in s)
            {
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        public static string ComputeSha256(Stream stream)
        {
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(stream));
        }

        public static string ComputeSha256(byte[] data)
        {
            using var sha = SHA256.Create();
            return ToHex(sha.ComputeHash(data));
        }

        /// <summary>Compares two hex digests ignoring case, without early exit on the first mismatch.</summary>
        public static bool DigestsEqual(string a, string b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++)
                diff |= char.ToLowerInvariant(a[i]) ^ char.ToLowerInvariant(b[i]);
            return diff == 0;
        }

        /// <summary>Throws <see cref="UpdateException"/> unless the stream has the expected size and SHA-256.</summary>
        public static void Verify(Stream stream, string expectedSha256, long expectedSize)
        {
            if (!IsValidHex(expectedSha256)) throw new UpdateException("Expected hash is not a valid SHA-256.");
            if (stream.CanSeek && stream.Length != expectedSize)
                throw new UpdateException("Downloaded file has the wrong size (" + stream.Length + " bytes, expected " + expectedSize + ").");
            string actual = ComputeSha256(stream);
            if (!DigestsEqual(actual, expectedSha256))
                throw new UpdateException("Downloaded file does not match its published SHA-256.");
        }

        internal static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }
    }
}
