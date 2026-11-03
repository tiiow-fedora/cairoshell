using System;
using System.Collections.Generic;
using System.Globalization;

namespace CairoDesktop.Updates
{
    /// <summary>SemVer 2.0 version with precedence comparison (build metadata ignored).</summary>
    public sealed class SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        private readonly string[] _pre;

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string Prerelease { get; }
        public bool IsPrerelease => _pre.Length > 0;

        private SemanticVersion(int major, int minor, int patch, string prerelease)
        {
            Major = major;
            Minor = minor;
            Patch = patch;
            Prerelease = prerelease;
            _pre = prerelease.Length == 0 ? new string[0] : prerelease.Split('.');
        }

        /// <summary>
        /// Parses "1.2.3", "v1.2.3", "1.2.3-beta.1", "1.2.3+build" and the four-part "1.2.3.4"
        /// that Nerdbank.GitVersioning emits (the fourth part is dropped).
        /// </summary>
        public static bool TryParse(string? text, out SemanticVersion version)
        {
            version = null!;
            if (string.IsNullOrWhiteSpace(text)) return false;
            string s = text!.Trim();
            if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);

            int plus = s.IndexOf('+');
            if (plus >= 0) s = s.Substring(0, plus);

            string pre = "";
            int dash = s.IndexOf('-');
            if (dash >= 0)
            {
                pre = s.Substring(dash + 1);
                s = s.Substring(0, dash);
                if (!ValidPrerelease(pre)) return false;
            }

            string[] parts = s.Split('.');
            if (parts.Length < 3 || parts.Length > 4) return false;
            var nums = new int[3];
            for (int i = 0; i < 3; i++)
            {
                if (!TryNumber(parts[i], out nums[i])) return false;
            }
            if (parts.Length == 4 && !TryNumber(parts[3], out _)) return false;

            version = new SemanticVersion(nums[0], nums[1], nums[2], pre);
            return true;
        }

        public static SemanticVersion Parse(string text)
        {
            if (!TryParse(text, out var v)) throw new FormatException("Not a valid version: '" + text + "'");
            return v;
        }

        private static bool TryNumber(string part, out int value)
        {
            value = 0;
            if (part.Length == 0) return false;
            foreach (char c in part) if (c < '0' || c > '9') return false;
            if (part.Length > 1 && part[0] == '0') return false;
            return int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out value);
        }

        private static bool ValidPrerelease(string pre)
        {
            if (pre.Length == 0) return false;
            foreach (string id in pre.Split('.'))
            {
                if (id.Length == 0) return false;
                bool numeric = true;
                foreach (char c in id)
                {
                    bool alnum = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '-';
                    if (!alnum) return false;
                    if (c < '0' || c > '9') numeric = false;
                }
                if (numeric && id.Length > 1 && id[0] == '0') return false;
            }
            return true;
        }

        public int CompareTo(SemanticVersion? other)
        {
            if (other is null) return 1;
            int c = Major.CompareTo(other.Major);
            if (c != 0) return c;
            c = Minor.CompareTo(other.Minor);
            if (c != 0) return c;
            c = Patch.CompareTo(other.Patch);
            if (c != 0) return c;

            // A version without a prerelease tag is greater than one with.
            if (!IsPrerelease && !other.IsPrerelease) return 0;
            if (!IsPrerelease) return 1;
            if (!other.IsPrerelease) return -1;

            int n = Math.Min(_pre.Length, other._pre.Length);
            for (int i = 0; i < n; i++)
            {
                c = ComparePreIdentifier(_pre[i], other._pre[i]);
                if (c != 0) return c;
            }
            return _pre.Length.CompareTo(other._pre.Length);
        }

        private static int ComparePreIdentifier(string a, string b)
        {
            bool an = int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out int ai) && IsDigits(a);
            bool bn = int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out int bi) && IsDigits(b);
            if (an && bn) return ai.CompareTo(bi);
            if (an) return -1; // numeric identifiers sort below alphanumeric ones
            if (bn) return 1;
            return string.CompareOrdinal(a, b);
        }

        private static bool IsDigits(string s)
        {
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return s.Length > 0;
        }

        public bool Equals(SemanticVersion? other) => other is not null && CompareTo(other) == 0;
        public override bool Equals(object? obj) => Equals(obj as SemanticVersion);
        public override int GetHashCode() => (Major, Minor, Patch, Prerelease).GetHashCode();
        public override string ToString() => Major + "." + Minor + "." + Patch + (IsPrerelease ? "-" + Prerelease : "");

        public static bool operator >(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) > 0;
        public static bool operator <(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) < 0;
        public static bool operator >=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) >= 0;
        public static bool operator <=(SemanticVersion a, SemanticVersion b) => a.CompareTo(b) <= 0;
    }
}
