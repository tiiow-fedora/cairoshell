# Cairo Plus

A fork of [Cairo Desktop](https://github.com/cairoshell/cairoshell) that adds whole-desktop
theming in the spirit of Omarchy: one theme choice restyles the menu bar, taskbar, icons, fonts,
wallpaper and colours together. You also get bar layouts, widgets, shapes and effects, hotkeys,
and live reload.

Everything is **off by default**. With no `cairo-plus.json`, or `"enabled": false`, the fork behaves
exactly like stock Cairo.

- [Building](#building)
- [Quick start](#quick-start)
- [The config file](#the-config-file)
- [Making a theme pack](#making-a-theme-pack)
- [Light and dark mode](#light-and-dark-mode)
- [Per-monitor settings](#per-monitor-settings)
- [Bar layout](#bar-layout)
- [Widgets](#widgets)
- [Writing a widget](#writing-a-widget)
- [Shapes and effects](#shapes-and-effects)
- [Hotkeys](#hotkeys)
- [Hooks](#hooks)
- [Theme authoring tools](#theme-authoring-tools)
- [Command line](#command-line)
- [Pulling upstream updates](#pulling-upstream-updates)
- [Known limitations](#known-limitations)
- [Status and next steps](#status-and-next-steps)

## Building

Requirements: Windows 10 or 11, the .NET SDK, and the .NET Framework 4.8 targeting pack (included with
Visual Studio 2022's ".NET desktop development" workload).

Cairo targets `net480`, `net6.0-windows` and `net10.0-windows`. Restoring all three needs the **.NET 10 SDK**.
With an older SDK (8.x), build one framework at a time by overriding the framework list:

```bash
cd "Cairo Desktop"
dotnet build "Cairo Desktop/Cairo Desktop.csproj" -f net6.0-windows -p:TargetFrameworks=net6.0-windows -p:Platform=x64
```

The output is `Cairo Desktop/bin/x64/Debug/net6.0-windows/win-x64/CairoDesktop.exe`. Use `-f net480
-p:TargetFrameworks=net480` for the .NET Framework build.

Run the tests (xUnit, about 136 tests: config parsing, JSON editing, palette expansion, hotkey parsing, bar layout, theme validator, hooks, theme archive and scaffold, theme commands):

```bash
dotnet test "Tests/CairoDesktop.Customization.Tests/CairoDesktop.Customization.Tests.csproj" -f net6.0-windows -p:TargetFrameworks=net6.0-windows -p:Platform=x64
```

Cairo allows only one running copy. Exit any installed Cairo (Cairo menu → Exit Cairo) before starting
the fork. The fork reads the same `%LOCALAPPDATA%\Cairo Desktop\settings.json`, but it never writes to that
file for Cairo Plus features. Cairo Plus keeps its own `cairo-plus.json`.

## Quick start

1. Start the fork, open **Cairo menu → Cairo Settings → Theme Packs**, pick a pack and press **Apply theme**.
   Or run `CairoDesktop.exe --apply-theme Nord`.
2. To customise further, press **Open config file**. Every saved edit applies within about half a second.

Bundled packs that switch between a light and a dark version with the Windows app mode: **Catppuccin**, **Ayu**
and **Atom One**. The others:
**Nord**, **Gruvbox** (own layout and widgets), **Tokyo Night** (floating rounded bars),
**Mocha Ember** (floating taskbar), **Dracula** (stat widgets), **Rose Pine** (floating rounded bars),
**Solarized Dark** (network and battery widgets), **Everforest** (floating taskbar with a centred task list)
**Catppuccin Latte** (a light theme) and **Kanagawa** (CPU and memory widgets). `CairoDesktop.exe --list-themes` lists them.

## The config file

`%LOCALAPPDATA%\Cairo Desktop\cairo-plus.json` is JSON that also allows `//` and `/* */` comments and
trailing commas. The first time you enable Cairo Plus, it's created with commented examples of every
setting.

```jsonc
{
  "enabled": true,
  "theme": "Nord",                    // theme pack folder name, or null
  "appearance": "auto",               // auto (follow Windows), light or dark; picks a pack's variant

  "menuBar": {                        // layout + shape of the menu bar
    "layout": { "center": ["widget:clock"] },
    "cornerRadius": 8,
  },
  "taskbar": { "floating": true, "margin": 6 },
  "monitors": { "secondary": { "menuBar": { "opacity": 0.8 } } },
  "animations": { "enabled": true },

  "widgets": { "clock": { "type": "clock", "format": "ddd d MMM  HH:mm" } },

  "hotkeys": [ { "keys": "Ctrl+Alt+T", "action": "theme", "arg": "next" } ],

  "hooks": { "onThemeApplied": [ { "action": "run", "arg": "C:\\Scripts\\retheme.cmd" } ] },

  "appIcons": { "notepad.exe": "C:\\Icons\\notepad.png" },
}
```

Values are layered in this order, each overriding the one before it: **Cairo's defaults, then the theme pack, then cairo-plus.json**,
field by field. A config saved with a syntax error doesn't break the desktop: the previous settings stay applied,
and the error (with line and column) appears in the log and on the Theme Packs settings tab.

## Making a theme pack

A theme pack is a folder containing `theme.json`. Cairo Plus looks in two places:

- `<Cairo folder>\ThemePacks\<name>\` (bundled packs)
- `%LOCALAPPDATA%\Cairo Desktop\ThemePacks\<name>\` (your own; wins over a bundled pack with the same name)

The folder name is what `"theme"` refers to. Changes to the active pack's files apply live.

```
ThemePacks/
  Dracula/
    theme.json
    wallpaper.jpg
    icons/
      MenuIcon.png          ← replaces Cairo's own UI icons by resource key
      DesktopOverlayIcon.png
      apps/firefox.png      ← per-app icon, referenced from "appIcons"
    fonts/
      JetBrainsMono.ttf
```

```jsonc
{
  "name": "Dracula",
  "author": "you",
  "description": "Dark with vivid accents",
  "baseTheme": "Default",          // Cairo XAML theme underneath: Default, Flat, White, ...
  "darkMode": true,

  "palette": {
    "background": "#282A36",
    "surface":    "#343746",
    "overlay":    "#44475A",
    "border":     "#6272A4",
    "text":       "#F8F8F2",
    "subtext":    "#BFBFBF",
    "accent":     "#BD93F9",
    "accentText": "#282A36",
    "urgent":     "#FF5555",
  },

  "resources": { "MenuTopBorderBrush": "#FF79C6" },   // raw overrides by Cairo resource key

  "font": { "family": "JetBrains Mono", "file": "fonts/JetBrainsMono.ttf", "size": 12 },
  "wallpaper": { "file": "wallpaper.jpg", "style": "fill" },  // fill, fit, stretch, center, tile, span

  "icons":    { "MenuIcon": "icons/logo.png" },               // optional; icons/<Key>.png is picked up anyway
  "appIcons": { "firefox.exe": "icons/apps/firefox.png" },

  "xaml": "extra.xaml",            // optional ResourceDictionary for anything else

  // Packs can also set layout, widgets, shapes and effects, exactly like cairo-plus.json:
  "menuBar": { "cornerRadius": 8 },
  "taskbar": { "floating": true, "margin": 6, "cornerRadius": 10 },
  "animations": { "enabled": true },
  "widgets": { },

  // Programs to run when the pack is applied. Off unless cairo-plus.json has "allowPackHooks": true.
  "hooks": { "onThemeApplied": [ { "action": "run", "arg": "powershell -NoProfile -File \"%CAIRO_THEME_DIR%\\apply.ps1\"" } ] },
}
```

Run `CairoDesktop.exe --new-theme "Dracula"` to start from a commented template, and `--validate-theme` to check it
(see [Theme authoring tools](#theme-authoring-tools)). [THEME-AUTHORING.md](THEME-AUTHORING.md) walks through
writing a pack, and lists every field and checker message.

**Palette.** Nine semantic roles expand into the roughly 70 brush keys Cairo's XAML themes use (menu bar,
menus, taskbar buttons in every state, search, dialogs, thumbnails). Only `background` really matters:
every other role is derived from it when left out. Colours are `#RGB`, `#RRGGBB` or `#AARRGGBB`.

**resources.** Any key from `Cairo Desktop/Themes/Cairo.xaml` can be overridden with a colour. Number,
boolean, thickness and font keys are typed automatically. Anything more complex goes in the pack's `xaml`.

**Fonts.** `family` alone uses an installed font. With `file`, the font loads straight from the pack and
nothing is installed system-wide. `family` must match the font's internal family name.

**Icons.** Cairo's UI icon keys: `MenuIcon`, `SearchIcon`, `ActionCenterIcon`, `VolumeIcon`, `VolumeLowIcon`,
`VolumeOffIcon`, `VolumeMuteIcon`, `DateTimeIcon`, `DesktopOverlayIcon`, `TaskListMenuIcon`, `TaskViewIcon`,
`DesktopToolbarBackIcon`, `DesktopToolbarUpIcon`, `DesktopToolbarForwardIcon`, `DesktopToolbarBrowseIcon`,
`DesktopToolbarHomeIcon`. Draw them at 2× size for crisp high-DPI rendering (the menu logo displays at 38×22).
`appIcons` replaces the icon of taskbar and quick-launch buttons, matched by exe name (`notepad.exe`) or full path.

**Wallpaper.** When Cairo is the Windows shell, it draws the desktop itself and the pack's wallpaper is
shown there, held in memory only. When Cairo runs on top of Explorer (the usual setup), Cairo's desktop
is transparent, so the pack sets the **Windows wallpaper**. That change persists after you switch packs or
turn Cairo Plus off.

## Light and dark mode

A pack can hold a `"light"` and a `"dark"` version of itself, and Cairo Plus applies the one that matches the
Windows app mode (Settings > Personalization > Colors). Switching the Windows mode re-themes the desktop
within about half a second.

```jsonc
// theme.json
"palette": { "background": "#1E1E2E", "text": "#CDD6F4" },       // dark, the default
"wallpaper": { "file": "wallpaper-night.jpg" },
"light": {
  "palette": { "background": "#EFF1F5", "text": "#4C4F69" },     // merged over the palette above
  "wallpaper": { "file": "wallpaper-day.jpg" },
},
```

```jsonc
// cairo-plus.json
"appearance": "auto",    // auto (default): follow Windows. light or dark: always use that version.
```

A variant may set `palette` and `resources` (both merged key by key), `wallpaper` (replaced) and `darkMode`
(otherwise `false` for `light` and `true` for `dark`). Packs without variants ignore `appearance`. Hooks receive
`CAIRO_APPEARANCE`. [THEME-AUTHORING.md](THEME-AUTHORING.md#add-a-day-and-night-version) has the details.

## Per-monitor settings

`"monitors"` overrides bar settings for particular monitors. It applies on top of `"menuBar"` and `"taskbar"`
(and the pack), field by field:

```jsonc
"menuBar": { "cornerRadius": 8, "opacity": 0.95 },
"monitors": {
  "secondary": { "menuBar": { "layout": { "center": ["widget:clock"] } }, "taskbar": { "opacity": 0.8 } },
  "3":         { "menuBar": { "opacity": 0.6 } },
  "DISPLAY2":  { "taskbar": { "floating": false } },
},
```

| Selector | Matches |
|---|---|
| `*` | every monitor |
| `primary`, `secondary` | the primary monitor, or every other monitor |
| `1`, `2`, ... | by number: the primary monitor is 1, the others follow in device name order |
| `DISPLAY2` | by Windows device name; the long form `\\.\DISPLAY2` also works but needs its backslashes doubled in JSON |

When several selectors match one monitor, the more specific one wins for each field: device name, then number,
then `primary` or `secondary`, then `*`. A `layout` replaces the whole layout; it is not merged zone by zone.
A selector that matches no monitor does nothing; a malformed selector is reported in the problem list.

## Bar layout

Each bar has three zones, each an ordered list of item ids:

```jsonc
"menuBar": {
  "layout": {
    "left":   ["cairoMenu", "programsMenu", "placesMenu"],
    "center": ["widget:clock"],
    "right":  ["widget:cpu", "widget:memory", "tray", "search"],
  },
},
"taskbar": {
  "layout": { "right": ["widget:network", "quickLaunch", "taskList"] },
},
```

| Bar | Built-in ids (stock order) |
|---|---|
| Menu bar | `cairoMenu`, `programsMenu`, `placesMenu`, `stacks`, `tray`, `volume`, `actionCenter`, `clock`, `search`, plus third-party menu extras (class name with a lowercase first letter) |
| Taskbar | `desktopButton`, `quickLaunch`, `tasks`, `taskList` |

- **Unlisted items are hidden.** That's how you remove something.
- **A zone you leave out keeps its stock contents**, minus anything you placed elsewhere. So
  `"right": ["clock"]` alone moves just the clock.
- `stacks` and `tasks` stretch to fill the space when placed in `center`. Elsewhere they take their natural size.
- Items that Cairo's own settings have switched off (for example `volume`) can't be shown; this is reported.
- Removing the layout puts every element back exactly where Cairo built it.

## Widgets

Place a widget with `widget:<id>`. Its options live under `"widgets"`. If an id is also a widget type,
no entry is needed (`widget:cpu` just works).

| Type | Shows | Options |
|---|---|---|
| `clock` | date/time | `format` (.NET date format, default `ddd h:mm tt`), `tooltipFormat` (default `D`), `interval` |
| `cpu` | total CPU % with a meter | `label` (default `CPU`), `style` (`text`, `bar`, `both`), `interval` (s, default 2) |
| `memory` | RAM in use with a meter | `label` (default `RAM`), `style`, `interval` |
| `battery` | charge level | `format` (`{label} {percent}%`, also `{time}`), `hideWhenNoBattery` (default true), `interval` |
| `network` | download/upload speed | `format` (`↓ {down}  ↑ {up}`), `interface` (name filter), `interval` |
| `command` | the output of a command | `command` (required, run via cmd.exe), `interval` (s, default 10; 0 = once), `timeout`, `label` |

Any widget can also take `"onClick": { "action": "launch", "arg": "taskmgr.exe" }`, using the same actions as
[hotkeys](#hotkeys).

**Command widgets need no code.** The first line of output is shown and the full output becomes the tooltip.
If the output is a JSON object, its `text` and `tooltip` fields are used instead:

```jsonc
"widgets": {
  "weather": { "type": "command", "command": "curl -s wttr.in/?format=3", "interval": 900 },
  "git":     { "type": "command", "command": "powershell -NoProfile -File C:\\Scripts\\git-status.ps1", "interval": 30 },
},
```

## Writing a widget

For anything a command can't do, write a .NET widget. It references only `CairoDesktop.Widgets.Sdk`,
never Cairo's core, so it keeps working across Cairo updates. A complete example lives in
`Cairo Desktop/Samples/CairoPlus.SampleWidget` (an uptime counter).

1. Create a class library targeting the same framework as your Cairo build (`net6.0-windows` for the
   standard build), with `<UseWPF>true</UseWPF>`, and reference the SDK without copying it:

   ```xml
   <ProjectReference Include="..\..\CairoDesktop.Widgets.Sdk\CairoDesktop.Widgets.Sdk.csproj" Private="false" />
   ```

   (Or reference the built `CairoDesktop.Widgets.Sdk.dll` with `<Private>false</Private>`.)

2. Implement a factory. For the common case of "text that refreshes", derive from `TextWidget`:

   ```csharp
   using System;
   using CairoDesktop.Widgets.Sdk;

   public sealed class UptimeWidgetFactory : ICairoWidgetFactory
   {
       public string Type => "uptime";                       // the "type" in cairo-plus.json
       public ICairoWidget Create(WidgetContext context) => new UptimeWidget(context);
   }

   internal sealed class UptimeWidget : TextWidget
   {
       private readonly string _prefix;

       public UptimeWidget(WidgetContext context) : base(context, TimeSpan.FromSeconds(30))
       {
           _prefix = context.Options.GetString("prefix", "up");   // options from cairo-plus.json
       }

       protected override string GetText() => $"{_prefix} {TimeSpan.FromMilliseconds(Environment.TickCount64):h\\:mm}";
   }
   ```

   For full control, implement `ICairoWidget` directly. It returns any WPF `FrameworkElement` from `View`
   and cleans up in `Dispose()`. Use `WidgetTheme.ApplyText(textBlock, context.Bar)` or the
   `WidgetTheme.*Key` resource keys with `SetResourceReference`, so your widget follows theme changes live.

3. Build it, copy the DLL to `%LOCALAPPDATA%\Cairo Desktop\Widgets\` and **restart Cairo**. .NET can't unload
   assemblies, so new widget DLLs are only picked up at startup. Their options still reload live.

4. Use it:

   ```jsonc
   "widgets": { "uptime": { "type": "uptime", "prefix": "up" } },
   "menuBar": { "layout": { "right": ["widget:uptime", "tray", "search"] } },
   ```

Load errors (missing dependencies, duplicate type names) are listed on the Theme Packs settings tab.

## Shapes and effects

All of these are per bar, live next to `"layout"`, and can be set by a theme pack:

| Setting | Effect |
|---|---|
| `"floating": true, "margin": 6` | The bar floats 6 px from the screen edges. Windows reserve the bar plus its margins. |
| `"cornerRadius": 10` | Rounded corners; contents are clipped to the shape. |
| `"spacing": 4` | Gap between items (also works without a custom layout). |
| `"opacity": 0.85` | Background opacity, 0–1. Works with or without a theme pack. |
| `"blur": true` | Blur behind the bar. **Docked, square bars only**; see below. |

`"animations": { "enabled": true, "durationMs": 220 }` adds a slide-in when a bar first appears, a
cross-fade whenever settings are re-applied (for example on a theme switch), and an animated hover on widgets.

**Blur and rounded or floating bars.** Windows 10's blur-behind always covers the whole window rectangle.
Clipping the window to a rounded region to contain the blur made Cairo's (layered) bar window stop drawing
entirely on Windows 10 21H2. So a floating or rounded bar keeps its shape, turns blur off, and says so in
the status list. On a docked, square bar, `blur` works and overrides Cairo's own "blur" setting for that bar.

## Hotkeys

```jsonc
"hotkeys": [
  { "keys": "Ctrl+Alt+T",      "action": "theme",   "arg": "next" },
  { "keys": "Ctrl+Alt+Return", "action": "launch",  "arg": "wt.exe", "args": "-p PowerShell" },
  { "keys": "Ctrl+Alt+B",      "action": "run",     "arg": "C:\\Scripts\\backup.cmd" },
  { "keys": "Ctrl+Alt+D",      "action": "command", "arg": "ToggleDesktopOverlay" },
  { "keys": "Ctrl+Alt+R",      "action": "reload" },
],
```

- **Keys:** modifiers `Win` (`Super`, `Meta`), `Ctrl`, `Alt`, `Shift`, plus exactly one key: a letter, digit, `F1`–`F24`,
  `Space`, `Enter`, `Esc`, `Left`/`Right`/`Up`/`Down`, `Plus`, `Minus`, `,`, `.`, or any WPF `Key` name.
  A bare letter or digit is refused because it would block typing. Bare F-keys and media keys are allowed.
- **Actions:**
  - `launch` starts a program, document or URL (optional `args`).
  - `run` runs a command line through cmd.exe without a window.
  - `theme` switches to a pack name, `next`, or `none`.
  - `command` runs a built-in Cairo command, such as `ToggleDesktopOverlay`, `ShowRunDialog`, `OpenCairoSettings`,
    `ToggleProgramsMenu`, `ToggleCairoMenu`, `TaskView`, `Lock` or `StartTaskManager`.
  - `reload` re-applies the config.
- Hotkeys are registered whether or not Cairo is the shell. A combination already taken by Windows or another
  program (for example `Win+R` while Explorer is the shell) is reported rather than silently ignored.

## Hooks

Hooks run a command when something happens, so a theme switch can restyle other programs too (terminal, editor,
wallpaper tools). In `cairo-plus.json`:

```jsonc
"hooks": {
  "onStart":          [ { "action": "run", "arg": "C:\\Scripts\\startup.cmd" } ],
  "onThemeApplied":   [ { "action": "run", "arg": "powershell -NoProfile -File C:\\Scripts\\retheme.ps1" } ],
  "onConfigReloaded": [ { "action": "launch", "arg": "notepad.exe", "args": "%CAIRO_THEME_DIR%\\notes.txt" } ],
},
```

| Hook | Fires |
|---|---|
| `onStart` | once, after Cairo Plus first applied its settings |
| `onThemeApplied` | when the active pack changes (a switch, or editing `"theme"`); not at startup |
| `onConfigReloaded` | after every re-apply, including theme switches; at most once per 2 seconds |

- **Actions:** `run` (command line through cmd.exe, no window), `launch` (program, document or URL) and `command`
  (built-in Cairo command). `theme` and `reload` are not allowed, because a hook could trigger itself.
- **Environment.** `run` hooks get these variables, and `%NAME%` is also expanded in `arg` and `args` of every action:

  | Variable | Value |
  |---|---|
  | `CAIRO_HOOK` | the hook that fired |
  | `CAIRO_THEME`, `CAIRO_THEME_NAME` | pack folder name and display name; empty with no pack |
  | `CAIRO_THEME_DIR` | absolute path of the pack folder |
  | `CAIRO_THEME_DARK` | `1` or `0` |
  | `CAIRO_APPEARANCE` | `light` or `dark`: the pack version in effect |
  | `CAIRO_WALLPAPER`, `CAIRO_FONT` | the pack's wallpaper path and font family, if set |
  | `CAIRO_COLOR_BACKGROUND`, `_SURFACE`, `_OVERLAY`, `_BORDER`, `_TEXT`, `_SUBTEXT`, `_ACCENT`, `_ACCENTTEXT`, `_URGENT` | `#RRGGBB` |

- **Packs can carry hooks, but they stay off.** A pack's `"hooks"` only run when your `cairo-plus.json` has
  `"allowPackHooks": true`. Until then, Settings > Theme Packs says the pack has hooks. Read `theme.json` before
  you turn that on for a pack you downloaded. Pack hooks run before yours.
- Hook failures are reported in the problem list and never stop Cairo.

## Theme authoring tools

These commands run without starting Cairo and print to the console that launched them. Run them from a terminal
and press Enter afterwards to get the prompt back, because Windows programs don't hold the console. With no
console (for example from the Run dialog) the output appears in a message box.

```
CairoDesktop.exe --list-themes                       list packs; * marks the active one
CairoDesktop.exe --validate-theme [pack|folder]      lint one pack (or all); exit code 1 if there are errors
CairoDesktop.exe --new-theme "My Theme" [--from Nord]   create a pack in %LOCALAPPDATA%\Cairo Desktop\ThemePacks
CairoDesktop.exe --export-theme Nord [Nord.zip]      zip a pack to share
CairoDesktop.exe --install-theme Dracula.zip         install a zip into your packs folder
```

`--validate-theme` reports each finding as `error` (will not work), `warning` (probably a mistake) or `info`:
missing files, paths that escape the pack folder, unknown resource keys (typos), bad colours, text with a
contrast ratio under 4.5:1 against its background, `darkMode` that disagrees with the background, bad bar
numbers, unknown layout ids and widget types, and invalid hooks.

`--install-theme` refuses archives with `..` or absolute paths, more than 2000 files or more than 200 MB, never
overwrites an existing pack (the new one gets `-2`), and tells you if the pack defines hooks.

## Command line

```
CairoDesktop.exe --apply-theme <pack>    activate a pack (turns Cairo Plus on); "none" for no pack
CairoDesktop.exe --reload-config         re-apply cairo-plus.json
```

The theme authoring commands are listed [above](#theme-authoring-tools).

Both edit or touch `cairo-plus.json`, keeping your comments, and exit immediately if Cairo is already running;
the running copy reloads live. If Cairo isn't running, it starts with the new settings.

## Pulling upstream updates

All new code lives in new projects: `CairoDesktop.Customization` (including `Cli/` for the theme commands), `CairoDesktop.Widgets.Sdk`,
`Tests/CairoDesktop.Customization.Tests` and `Samples/CairoPlus.SampleWidget`. Upstream files only gain
short calls into `CairoPlusHooks`, each marked `CAIRO-PLUS` (`git grep CAIRO-PLUS`):

- `Program.cs`: command-line switch, exit code, service registration
- `CairoApplicationThemeService.cs`: theme pack layer
- `SettingsUI.xaml`: Theme Packs tab
- `Desktop.xaml.cs`: in-memory wallpaper (shell mode)
- `MenuBar.xaml.cs`, `Taskbar.xaml.cs`: bar registration, floating height, task button width
- `TaskButton.xaml`, `QuickLaunchButton.xaml`: per-app icons
- plus project references and the solution file

```bash
git fetch upstream
git merge upstream/master
```

Conflicts, if any, will be in those spots. Every hook is a no-op while Cairo Plus is off.

## Known limitations

- **Tested** on Windows 10 21H2 with Cairo running on top of Explorer, across three monitors.
  - **Not tested:** Cairo running as the actual Windows shell (in-memory wallpaper, hotkeys in that mode), and
    the net10/ARM64 build (it needs the .NET 10 SDK).
  - The code paths for shell mode don't depend on shell state apart from the wallpaper, which uses Cairo's own
    desktop brush code.
- Pack wallpapers set the Windows wallpaper permanently when Cairo isn't the shell.
- New widget DLLs need a Cairo restart.
- Windows lets an app turn on dark mode for its native menus only once per run. After a pack's dark version has
  been applied, those menus stay dark until Cairo restarts, even if the pack then switches to its light version.
- Blur only on docked, square bars (see above).
- Cairo's optional menu bar shadow isn't moved for floating menu bars; leave it off with floating bars.

## Status and next steps

**Pillars.** All four are implemented: theme packs (colours, fonts, wallpaper, icons, radii, bar position and size,
effects), bar layout and widgets, shapes and effects, and hotkeys plus hooks and commands. Nine packs are bundled.

**Added in the latest session** (branch `claude/cloud-burn`): theme validator and `--validate-theme`, `--list-themes`,
`--new-theme`, `--export-theme`, `--install-theme`, hooks with `CAIRO_*` environment variables and the
`allowPackHooks` gate, five more bundled packs, a derived `accentText` that picks black or white by contrast, and
a fix to Mocha Ember's unreadable accent text that the new validator test found.

**What was verified, and how.** That session ran in a Linux container where `dotnet-install.sh` and NuGet were
blocked, so WPF, ManagedShell and xUnit could not be restored and **nothing was built as a whole**. Instead, all
non-UI files (config, theme manifest, palette, validator, hooks model, archive, scaffold, CLI) were compiled on
.NET 10 against a small `Color` stand-in, and their tests (104 test cases, including 32 of the earlier ones)
ran through a throwaway xUnit-compatible runner: all passed. Not compiled anywhere: `CairoPlusService` (hook firing),
`ActionRunner` (environment variables), `CairoPlusCommandLine` (console output) and `Program.cs`, and the project
file change for zip references on net480. Review those for typos first.

**To test on Windows** (build, then run the tests, then try):
1. `dotnet build` and the test command above. Fix any compile error in the files named in the previous paragraph.
2. Switch through all nine packs in Settings > Theme Packs; check the light pack (Catppuccin Latte) for unreadable text.
3. Hooks: add an `onThemeApplied` `run` hook that writes `%CAIRO_THEME%` and a colour to a file; switch themes.
   Confirm the file updates, that `onStart` fires once, and that a hook editing `cairo-plus.json` does not loop.
4. Set a pack hook and confirm it is blocked until `"allowPackHooks": true`.
5. From a terminal: `--list-themes`, `--validate-theme`, `--new-theme "Test" --from Nord`, `--export-theme Test`,
   `--install-theme Test.zip`. Also run one from the Run dialog to check the message-box fallback.
6. Net Framework 4.8 build: confirm the zip commands compile (the `System.IO.Compression` references).

**Next steps.**
- Show validator findings for the active pack in the Theme Packs tab (needs XAML work; not done because it can't be checked on Linux).
- A theme preview or gallery in the settings tab, and a "next/previous theme" widget.
- Per-monitor overrides, and a dedicated pack-level `startup` animation or sound.
- Optional scripting beyond hooks: a small expression language for widgets, or a documented PowerShell module.
- Theme packs that follow Windows dark/light mode (a day/night pair of palettes).
- Run `--validate-theme` in CI against the bundled packs (the unit test already does this).
