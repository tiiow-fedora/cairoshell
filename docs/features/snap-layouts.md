# Window Snap Layouts

A small, hotkey-driven take on PowerToys FancyZones. Each monitor gets a **layout**: a set of **zones** that tile the
work area (the screen minus the taskbar and menu bar). Hotkeys move the foreground window into a zone, step between
zones, or put the window back where it was.

> **Status: logic written and unit-tested, not wired in.** The library (`CairoDesktop.Features/SnapLayouts`) and its
> tests (190 cases, run on Linux with `dotnet test`) are complete. The Win32 adapter
> (`CairoDesktop.Customization/Features/SnapLayouts/WindowSnapper.cs`) has only been compiled as a syntax check on
> .NET 8 and has never run on Windows. Nothing registers the hotkeys or reads the config section yet; see
> [Wiring status](#wiring-status).

## Concept

- A layout is a list of zones, each a rectangle in fractions of the work area (`x`, `y`, `w`, `h`, all 0..1).
- `ZoneCalculator` turns that into pixels. Edges shared by two zones are rounded once, so zones touch exactly, the set
  spans the work area exactly, and `gap` pixels separate neighbours (an odd gap is split floor/ceil). A zone is never
  smaller than 1 pixel: a gap larger than the zone clamps it rather than producing a negative size.
- Which layout a monitor uses: your `monitors` map (by device name, `WIDTHxHEIGHT` or `primary`), then `defaultLayout`,
  then the monitor's shape: portrait gets top/bottom halves, aspect ratio 2.2 or wider gets 25/50/25, otherwise left/right halves.
- Snapping remembers the window's rectangle from before the first snap. Moving between zones keeps that original; if you
  drag the window elsewhere first, the dragged position becomes the new "original". `restore` puts it back.

## Configuration

```jsonc
// cairo-plus.json
"snapLayouts": {
  "enabled": true,
  "gap": 8,                    // pixels between zones (0..200)
  "outerGap": 8,               // pixels between zones and the screen/taskbar edge
  "defaultLayout": "thirds",   // omit to choose by monitor shape
  "wrap": true,                // arrows and next/prev wrap at the ends
  "snapMaximized": false,      // true: un-maximize a maximized window, then snap it
  "layouts": [
    { "id": "sidebar", "name": "Sidebar",
      "zones": [ { "x": 0, "y": 0, "w": 0.2, "h": 1 }, { "x": 0.2, "y": 0, "w": 0.8, "h": 1 } ] },
    { "id": "pip", "allowOverlap": true,
      "zones": [ { "x": 0, "y": 0, "w": 1, "h": 1 }, { "x": 0.7, "y": 0.7, "w": 0.3, "h": 0.3 } ] }
  ],
  "hotkeys": { "snapLeft": "Win+Alt+Left", "restore": "Win+Alt+Backspace", "cycleLayout": "" },
  "monitors": { "primary": "main-stack", "\\\\.\\DISPLAY2": "halves-tb", "3440x1440": "ultrawide" }
}
```

Comments and trailing commas are accepted. A bad value never stops the rest of the section from loading: the parser
reports a finding (warning or error, with a path such as `snapLayouts.layouts[1]`) and falls back to the default or skips
only the offending layout, hotkey or monitor entry. Custom layouts are skipped when a zone is missing a number, lies
outside 0..1, has no area, overlaps another zone without `allowOverlap`, or reuses an existing id. An empty or `null`
hotkey disables that action; two actions on the same key disable the later one (key aliases such as `PgDn`/`PageDown` or `Enter`/`Return` count as the same key).

## Built-in layouts

| Id | Zones | Diagram |
|---|---|---|
| `halves-lr` | 2 | `[ 1 \| 2 ]` |
| `halves-tb` | 2 | `[ 1 ]` over `[ 2 ]` |
| `thirds` | 3 | `[ 1 \| 2 \| 3 ]` |
| `quarters` | 4 | `[ 1 \| 2 ]` over `[ 3 \| 4 ]` |
| `2-1` | 2 | `[ 1 (2/3) \| 2 (1/3) ]` |
| `1-2` | 2 | `[ 1 (1/3) \| 2 (2/3) ]` |
| `main-stack` | 3 | `[ 1 (60%) \| 2 ]` with `3` stacked under `2` |
| `ultrawide` | 3 | `[ 1 (25%) \| 2 (50%) \| 3 (25%) ]` |
| `center-focus` | 3 | side strips `1` and `3` (15% each), `2` centred at 70% x 80% with a margin above and below |

```
halves-lr      halves-tb     thirds           quarters      main-stack
+----+----+    +---------+   +---+---+---+    +----+----+   +------+----+
|    |    |    |    1    |   | 1 | 2 | 3 |    | 1  | 2  |   |      | 2  |
| 1  | 2  |    +---------+   |   |   |   |    +----+----+   |  1   +----+
|    |    |    |    2    |   |   |   |   |    | 3  | 4  |   |      | 3  |
+----+----+    +---------+   +---+---+---+    +----+----+   +------+----+
```

Built-in zones are numbered in reading order (left to right, top to bottom for grids); custom layouts use the order the zones are written. That is the order `nextZone` follows.
The library can also generate grids with per-column and per-row weights (`BuiltInLayouts.Grid`); the config does not
expose that yet, but a custom layout covers the same ground.

## Hotkey actions

Defaults use `Win+Alt`, chosen to avoid the Windows shortcuts I know of; check them against your own setup.

| Action | Default | What it does |
|---|---|---|
| `snapLeft` / `snapRight` / `snapUp` / `snapDown` | `Win+Alt+Arrow` | Move to the nearest zone whose centre lies in that direction. An unsnapped window goes to the zone furthest in that direction (first on ties). |
| `nextZone` / `prevZone` | `Win+Alt+PageDown` / `PageUp` | Step through zones in index order. |
| `restore` | `Win+Alt+Backspace` | Return the window to its pre-snap rectangle. |
| `cycleLayout` | `Win+Alt+Space` | Switch the current monitor to the next layout (built-ins, then custom) and re-snap the window to the same zone number. |

## Why DPI and invisible borders matter

**Invisible borders.** On Windows 10 and 11 a normal window's `GetWindowRect` includes roughly 7 px of transparent resize
border on the left, right and bottom. If you place that rectangle on a zone, the visible window ends up 7 px short and
the neighbouring windows look like they are separated by 14 px or overlap. The adapter asks DWM for
`DWMWA_EXTENDED_FRAME_BOUNDS` (the visible frame) and `FrameInsets.Compensate` grows the target by the difference on each
side, so the *visible* frame lands exactly on the zone. Insets may be asymmetric or negative. If the DWM call fails, or the
two rectangles differ by more than 100 px (a sign they came from different DPI contexts), no compensation is applied.

**DPI.** All maths is in physical pixels, taken from `GetMonitorInfo`. That is correct only if the calling thread is per-monitor DPI
aware; otherwise Windows hands back scaled coordinates and zones are wrong on any monitor not at 100%. Cairo's manifest is
expected to declare per-monitor awareness (not verified here), and the adapter additionally switches the calling thread to
`PER_MONITOR_AWARE_V2` for the duration of each action when the OS supports it (Windows 10 1607+).

## What gets skipped

The adapter returns `false` and does nothing for: Cairo's own windows (same process), the desktop and taskbar
(`Progman`, `WorkerW`, `Shell_TrayWnd`, `Shell_SecondaryTrayWnd`), invisible, minimized and full-screen windows (a window exactly the size of its monitor, even if maximized, such as an F11 browser), windows
without a sizing border and tool windows, hung windows (`IsHungAppWindow`), and maximized windows unless
`snapMaximized` is on. Elevated windows cannot be moved by a non-elevated Cairo (UIPI makes `SetWindowPos` fail); that is
reported as `false`, not an exception.

## Library API (`CairoDesktop.Features.SnapLayouts`)

| Type | Role |
|---|---|
| `Rect` | Integer, half-open rectangle (matches Win32 `RECT`). |
| `SnapLayout`, `Zone`, `BuiltInLayouts`, `LayoutCatalog` | Layout model, validation, built-ins and grid generator, lookup and cycling. |
| `ZoneCalculator.Compute` | Layout + work area + gap + outer gap to pixel rectangles. |
| `ZoneHitTester` | `HitTest` (with gap tolerance), `NearestZone`, `Navigate`, `EdgeZone`, `Next`, `Previous`. |
| `LayoutPicker` | Per-monitor choice from map, default and monitor shape. |
| `SnapMemory`, `WindowKey`, `SnapPolicy` | Bounded LRU of pre-snap rectangles keyed by an opaque string, and the state policy. |
| `FrameInsets` | Invisible-border measurement and compensation. |
| `SnapConfigParser`, `SnapConfig`, `SnapFinding` | Lenient parser for the config section. |

Run the tests with `cd "Cairo Desktop" && dotnet test Tests/CairoDesktop.Features.Tests`.

## Wiring status

Done: all of the above, with tests. Not done, and needed before this works for a user:

1. Add a `ProjectReference` from `CairoDesktop.Customization` to `CairoDesktop.Features`, and define
   `CAIRO_FEATURES` there. `WindowSnapper.cs` is wrapped in `#if CAIRO_FEATURES` so that the Customization project still
   builds without the reference; until then it compiles to nothing.
2. Read the `snapLayouts` section with `SnapConfigParser.ParseDocument`, show its findings alongside the other config
   findings, and call `WindowSnapper.Configure`.
3. Register one hotkey per entry of `SnapConfig.Hotkeys` through the existing `HotkeyParser`/`HotkeyRegistry`, whose handler
   calls `WindowSnapper.Execute(actionName)` on the UI thread. (`HotkeyRegistry` currently takes `action`/`arg` pairs from
   `hotkeys`, so this needs either a new action kind or a second registry; the library's `HotkeySyntax` check is only a
   syntax pre-check and the real parser has the final say.)
4. Test on Windows: mixed-DPI monitors, a window with no invisible border (for example a Chromium app in some modes),
   elevated windows, and windows that enforce a minimum size.
