# Cairo Plus expansion features

Twelve isolated modules (the first four, then the eight "extras" below them in the table). Each has its logic in a WPF-free library
(`Cairo Desktop/CairoDesktop.Features`) with unit tests that run on any OS, and a thin Windows adapter
in `CairoDesktop.Customization/Features/<Module>/`.

| Feature | What it does | Docs |
|---|---|---|
| Command palette | Hotkey launcher with fuzzy search, calculator, web search, frecency ranking | [command-palette.md](command-palette.md) |
| Monitor profiles | Named taskbar profiles chosen per monitor by selectors, plus hot-plug diffing | [monitor-profiles.md](monitor-profiles.md) |
| Clipboard history | Clipboard widget with pinning, search, transforms and a sensitive-data policy | [clipboard-history.md](clipboard-history.md) |
| Snap layouts | Zone layouts and hotkey window snapping with exact pixel tiling | [snap-layouts.md](snap-layouts.md) |
| Focus timer | Pomodoro timer widget (`widget:focus`) | [focus-timer.md](focus-timer.md) |
| Theme schedule | Light/dark pack variant by clock time or sunrise/sunset (`themeSchedule`) | [theme-schedule.md](theme-schedule.md) |
| Keep awake | Click-to-cycle "don't sleep" widget (`widget:keepawake`) | [keep-awake.md](keep-awake.md) |
| World clock | Multi-time-zone widget with day markers (`widget:worldclock`) | [world-clock.md](world-clock.md) |
| Wallpaper slideshow | Rotating wallpaper from a folder (`wallpaperSlideshow`, `widget:wallpaper`) | [wallpaper-slideshow.md](wallpaper-slideshow.md) |
| Quick notes | Click-to-type notes list saved to a text file (`widget:quicknotes`) | [quick-notes.md](quick-notes.md) |
| Power plan | Shows and switches the Windows power plan (`widget:powerplan`) | [power-plan.md](power-plan.md) |
| Calendar agenda | Next event from a local `.ics` file (`widget:agenda`) | [agenda.md](agenda.md) |

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

## The eight extras are wired, behind one build flag

Focus timer, theme schedule, keep awake, world clock, wallpaper slideshow, quick notes, power plan and calendar agenda are connected to the shell
(`WidgetRegistry`, `CairoPlusService`), but only when you build with **`-p:CairoExtras=true`**. A normal build does not
define `CAIRO_EXTRAS`, does not reference the Features library from the shell, and is byte-for-byte the previous code path.
They use `CAIRO_EXTRAS` instead of `CAIRO_FEATURES`, so the four older, never-compiled adapters stay out of the build.
Build steps and a click-through checklist: [WINDOWS-VERIFY.md](WINDOWS-VERIFY.md).
