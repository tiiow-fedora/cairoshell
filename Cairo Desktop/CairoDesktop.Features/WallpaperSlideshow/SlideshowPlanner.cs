using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace CairoDesktop.Features.WallpaperSlideshow
{
    /// <summary>Which files count as wallpapers. Windows can set jpg, png and bmp as a desktop picture; the rest are ignored.</summary>
    public static class ImageFilter
    {
        private static readonly string[] Extensions = { ".jpg", ".jpeg", ".png", ".bmp" };

        public static bool IsWallpaper(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            string name = path.Replace('\\', '/');
            name = name.Substring(name.LastIndexOf('/') + 1);
            if (name.Length == 0 || name[0] == '.' || name[0] == '~') return false; // hidden and temp files
            int dot = name.LastIndexOf('.');
            if (dot < 0) return false;
            string ext = name.Substring(dot);
            return Extensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>What is remembered between runs.</summary>
    public sealed class SlideshowState
    {
        public string Last { get; set; }
        /// <summary>Files already shown in the current shuffle round.</summary>
        public List<string> Played { get; set; } = new List<string>();
        /// <summary>When the wallpaper last changed, so a restart does not reset the interval.</summary>
        public DateTimeOffset? LastChange { get; set; }
    }

    /// <summary>
    /// Chooses the next wallpaper. Sequential mode walks the files in name order and wraps. Shuffle mode shows every file once
    /// per round in random order, and never repeats the picture that ended the previous round straight away.
    /// The file list is passed in every time, so added and deleted files are picked up without any bookkeeping.
    /// </summary>
    public sealed class SlideshowPlanner
    {
        private readonly Random _random;

        public SlideshowPlanner(Random random = null, SlideshowState state = null)
        {
            _random = random ?? new Random();
            State = state ?? new SlideshowState();
            if (State.Played == null) State.Played = new List<string>();
        }

        public SlideshowState State { get; }

        /// <summary>Returns the next file and records it, or null when there are no candidates.</summary>
        public string Next(IEnumerable<string> available, bool shuffle, DateTimeOffset now)
        {
            List<string> files = (available ?? Enumerable.Empty<string>())
                .Where(ImageFilter.IsWallpaper)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (files.Count == 0) return null;

            string pick = shuffle ? PickShuffled(files) : PickSequential(files);
            State.Last = pick;
            State.LastChange = now;
            return pick;
        }

        private string PickSequential(List<string> files)
        {
            if (State.Last == null) return files[0];
            // First file that sorts after the last one shown; if the last one was deleted this still lands in the right place.
            foreach (string f in files)
                if (StringComparer.OrdinalIgnoreCase.Compare(f, State.Last) > 0) return f;
            return files[0];
        }

        private string PickShuffled(List<string> files)
        {
            var set = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            State.Played = State.Played.Where(set.Contains).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            List<string> pool = files.Where(f => !State.Played.Contains(f, StringComparer.OrdinalIgnoreCase)).ToList();
            if (pool.Count == 0)
            {
                // Round finished: start a new one, but do not open it with the picture the old one ended on.
                State.Played.Clear();
                pool = files.Where(f => files.Count == 1 || !string.Equals(f, State.Last, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            string pick = pool[_random.Next(pool.Count)];
            State.Played.Add(pick);
            return pick;
        }

        /// <summary>How long until the next change: zero when it is already due (or nothing was ever shown), never more than the interval.</summary>
        public TimeSpan UntilNext(DateTimeOffset now, TimeSpan interval)
        {
            if (State.LastChange == null) return TimeSpan.Zero;
            TimeSpan wait = State.LastChange.Value + interval - now;
            if (wait < TimeSpan.Zero) return TimeSpan.Zero;
            return wait > interval ? interval : wait; // a clock moved backwards must not freeze the slideshow
        }

        public string Serialize()
            => JsonSerializer.Serialize(new { last = State.Last, played = State.Played, lastChange = State.LastChange?.ToString("o") });

        /// <summary>Reads what <see cref="Serialize"/> wrote. Damaged or empty text gives a fresh state; never throws.</summary>
        public static SlideshowState Deserialize(string json)
        {
            var state = new SlideshowState();
            if (string.IsNullOrWhiteSpace(json)) return state;
            try
            {
                using (JsonDocument doc = JsonDocument.Parse(json))
                {
                    JsonElement root = doc.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) return state;
                    if (root.TryGetProperty("last", out JsonElement last) && last.ValueKind == JsonValueKind.String) state.Last = last.GetString();
                    if (root.TryGetProperty("played", out JsonElement played) && played.ValueKind == JsonValueKind.Array)
                        foreach (JsonElement e in played.EnumerateArray())
                            if (e.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(e.GetString())) state.Played.Add(e.GetString());
                    if (root.TryGetProperty("lastChange", out JsonElement lc) && lc.ValueKind == JsonValueKind.String
                        && DateTimeOffset.TryParse(lc.GetString(), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out DateTimeOffset when))
                        state.LastChange = when;
                }
            }
            catch (JsonException)
            {
                return new SlideshowState();
            }

            return state;
        }
    }
}
