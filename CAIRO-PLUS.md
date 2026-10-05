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
- [Bar layout](#bar-layout)
- [Widgets](#widgets)
- [Writing a widget](#writing-a-widget)
- [Shapes and effects](#shapes-and-effects)
- [Hotkeys](#hotkeys)
- [Command line](#command-line)
- [Pulling upstream updates](#pulling-upstream-updates)
- [Known limitations](#known-limitations)

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

Run the tests (xUnit, 64 tests: config parsing, JSON editing, palette expansion, hotkey parsing, bar layout):

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

Bundled packs: **Nord**, **Gruvbox** (with its own layout and widgets), **Tokyo Night** (floating rounded
bars) and **Mocha Ember** (floating taskbar).

## The config file

`%LOCALAPPDATA%\Cairo Desktop\cairo-plus.json` is JSON that also allows `//` and `/* */` comments and
trailing commas. The first time you enable Cairo Plus, it's created with commented examples of every
setting.

```jsonc
{
  "enabled": true,
  "theme": "Nord",                    // theme pack folder name, or null

  "menuBar": {                        // layout + shape of the menu bar
    "layout": { "center": ["widget:clock"] },
    "cornerRadius": 8,
  },
  "taskbar": { "floating": true, "margin": 6 },
  "animations": { "enabled": true },

  "widgets": { "clock": { "type": "clock", "format": "ddd d MMM  HH:mm" } },

  "hotkeys": [ { "keys": "Ctrl+Alt+T", "action": "theme", "arg": "next" } ],

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
}
```

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

## Command line

```
CairoDesktop.exe --apply-theme <pack>    activate a pack (turns Cairo Plus on); "none" for no pack
CairoDesktop.exe --reload-config         re-apply cairo-plus.json
```

Both edit or touch `cairo-plus.json`, keeping your comments, and exit immediately if Cairo is already running;
the running copy reloads live. If Cairo isn't running, it starts with the new settings.

## Pulling upstream updates

All new code lives in new projects: `CairoDesktop.Customization`, `CairoDesktop.Widgets.Sdk`,
`Tests/CairoDesktop.Customization.Tests` and `Samples/CairoPlus.SampleWidget`. Upstream files only gain
short calls into `CairoPlusHooks`, each marked `CAIRO-PLUS` (`git grep CAIRO-PLUS`):

- `Program.cs`: command-line switch, service registration
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
- Blur only on docked, square bars (see above).
- Cairo's optional menu bar shadow isn't moved for floating menu bars; leave it off with floating bars.
