using System;
using System.Collections.Generic;
using System.Linq;

namespace CairoDesktop.Features.SnapLayouts
{
    /// <summary>Built-in plus custom layouts, looked up by id (case-insensitive). Invalid custom layouts and ids that reuse an existing id are ignored.</summary>
    public sealed class LayoutCatalog
    {
        private readonly List<SnapLayout> _all = new List<SnapLayout>();

        public LayoutCatalog(IEnumerable<SnapLayout> custom = null)
        {
            _all.AddRange(BuiltInLayouts.All());
            if (custom == null) return;

            foreach (SnapLayout layout in custom)
            {
                if (layout != null && layout.IsValid && Find(layout.Id) == null) _all.Add(layout);
            }
        }

        public IReadOnlyList<SnapLayout> All => _all;

        public SnapLayout Default => Find(BuiltInLayouts.HalvesLr);

        public SnapLayout Find(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return null;
            string wanted = id.Trim();
            return _all.FirstOrDefault(l => string.Equals(l.Id, wanted, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The layout after (step = 1) or before (step = -1) <paramref name="currentId"/>, wrapping. Unknown id starts at the first.</summary>
        public SnapLayout Cycle(string currentId, int step = 1)
        {
            string wanted = currentId?.Trim();
            int index = _all.FindIndex(l => string.Equals(l.Id, wanted, StringComparison.OrdinalIgnoreCase));
            if (index < 0) return _all[0];
            int n = _all.Count;
            return _all[(((index + step) % n) + n) % n];
        }
    }
}
