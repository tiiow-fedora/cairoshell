using System;

namespace CairoDesktop.Features.CommandPalette
{
    public enum PaletteActionKind
    {
        /// <summary>Start a program or open a document/folder: Target = path or command, Arguments = arguments.</summary>
        Launch,
        /// <summary>Open a web address in the default browser. Target must pass <see cref="PaletteAction.IsSafeWebUrl"/>.</summary>
        Url,
        /// <summary>Copy Target to the clipboard.</summary>
        Copy
    }

    /// <summary>
    /// The payload convention used by the built-in providers: <c>kind US target US arguments</c>, where US is U+001F.
    /// Hosts may use any payload for their own providers; this type is only a helper.
    /// Thread-safety: immutable.
    /// </summary>
    public sealed class PaletteAction
    {
        private const char Sep = '\u001f';

        public PaletteAction(PaletteActionKind kind, string target, string arguments = null)
        {
            Kind = kind;
            Target = Clean(target);
            Arguments = Clean(arguments);
        }

        public PaletteActionKind Kind { get; }
        public string Target { get; }
        public string Arguments { get; }

        public string ToPayload() => Kind.ToString().ToLowerInvariant() + Sep + Target + Sep + Arguments;

        public static PaletteAction Launch(string target, string arguments = null) => new PaletteAction(PaletteActionKind.Launch, target, arguments);
        public static PaletteAction Url(string url) => new PaletteAction(PaletteActionKind.Url, url);
        public static PaletteAction Copy(string text) => new PaletteAction(PaletteActionKind.Copy, text);

        public static bool TryParse(string payload, out PaletteAction action)
        {
            action = null;
            if (string.IsNullOrEmpty(payload)) return false;

            string[] parts = payload.Split(Sep);
            if (parts.Length != 3) return false;
            if (!Enum.TryParse(parts[0], true, out PaletteActionKind kind) || !Enum.IsDefined(typeof(PaletteActionKind), kind)) return false;
            if (string.IsNullOrWhiteSpace(parts[1])) return false;

            action = new PaletteAction(kind, parts[1], parts[2]);
            return true;
        }

        /// <summary>True for an absolute http or https URL with a host and no control or whitespace characters.</summary>
        public static bool IsSafeWebUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;
            foreach (char c in url)
            {
                if (char.IsControl(c) || char.IsWhiteSpace(c)) return false;
            }

            return Uri.TryCreate(url, UriKind.Absolute, out Uri uri)
                   && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
                   && !string.IsNullOrEmpty(uri.Host);
        }

        private static string Clean(string s) => (s ?? "").Replace(Sep.ToString(), "");
    }
}
