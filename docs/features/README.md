# Cairo Plus expansion features

Four new, isolated modules. Each has its logic in a WPF-free library
(`Cairo Desktop/CairoDesktop.Features`) with unit tests that run on any OS, and a thin Windows adapter
in `CairoDesktop.Customization/Features/<Module>/`.

| Feature | What it does | Docs |
|---|---|---|
| Command palette | Hotkey launcher with fuzzy search, calculator, web search, frecency ranking | [command-palette.md](command-palette.md) |
| Monitor profiles | Named taskbar profiles chosen per monitor by selectors, plus hot-plug diffing | [monitor-profiles.md](monitor-profiles.md) |
| Clipboard history | Clipboard widget with pinning, search, transforms and a sensitive-data policy | [clipboard-history.md](clipboard-history.md) |
| Snap layouts | Zone layouts and hotkey window snapping with exact pixel tiling | [snap-layouts.md](snap-layouts.md) |

## Status: logic tested, adapters not yet enabled

- **Library and tests.** Built and tested on Linux with the .NET 8 SDK:
  `dotnet test "Cairo Desktop/Tests/CairoDesktop.Features.Tests"`.
- **Windows adapters.** Never compiled or run. WPF could not be built where this was written. Every adapter file is
  wrapped in `#if CAIRO_FEATURES`, so **the normal Windows build is unchanged** and ignores them.
- **Nothing is wired into `CairoPlusService` yet.** There are no config properties, hotkey registrations or
  widget registrations for these features.

## Enabling the adapters (first Windows build)

1. In `CairoDesktop.Customization.csproj` add
   `<ProjectReference Include="..\CairoDesktop.Features\CairoDesktop.Features.csproj" />`
   and `<DefineConstants>$(DefineConstants);CAIRO_FEATURES</DefineConstants>`.
2. Build for one framework at a time (see the Building section of `CAIRO-PLUS.md`) and fix any compile errors in
   `Features\`. Expect a few: the adapters were written without a compiler.
3. Wire each feature (config section, hotkey, widget registration). Each page lists what is left.
