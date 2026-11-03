# Per-monitor taskbar profiles

Named taskbar settings chosen by rules about the monitor (primary or not, resolution, orientation, aspect ratio,
DPI, device name), plus hot-plug handling that rebuilds only the bars that need it.

> **Status: logic complete and tested; not wired into the shell.** See [Wiring status](#wiring-status) before
> expecting `monitorProfiles` in `cairo-plus.json` to do anything.

## Concept

You describe your monitors once, by what they are, not by which port they are in:

- a `default` profile that applies to every monitor,
- an ordered list of `rules`; each has a `match` (which monitors) and a `profile` (what to change).

For every monitor, the resolver layers the settings and remembers which layer supplied each property, so
"why does my second monitor have a 36 px taskbar?" has a concrete answer (the `--explain-monitors` text below).

## Config

The section lives under `monitorProfiles` in `cairo-plus.json` (comments and trailing commas are fine):

```jsonc
"monitorProfiles": {
  "default": { "height": 32, "showClock": true, "iconSize": 24 },
  "rules": [
    { "name": "secondary",
      "match":   { "primary": false },
      "profile": { "showClock": false, "hideOnFullscreen": true } },

    { "name": "ultrawide",
      "match":   { "minAspectRatio": 2.1 },
      "profile": { "floating": true, "margin": 8, "layoutName": "centered-dock" } },

    { "name": "vertical",
      "match":   { "orientation": "portrait" },
      "profile": { "position": "left", "height": 44, "showTaskList": false } },

    { "name": "tv",
      "match":   { "deviceName": "*DISPLAY4", "minWidth": 3840 },
      "profile": { "autoHide": true, "iconSize": 48, "themeVariant": "dark" } }
  ]
}
```

### Profile properties

All optional. A property that is not set inherits from the layer below.

| Key | Values | Notes |
|---|---|---|
| `position` | `top`, `bottom`, `left`, `right` | |
| `height` | integer > 0 | thickness of the bar (width for left/right) |
| `autoHide` | bool | |
| `floating` | bool | |
| `margin` | integer >= 0 | |
| `layoutName` | string | name of a bar layout; left unset it is `null` |
| `themeVariant` | `auto`, `light`, `dark` | |
| `showClock`, `showTaskList` | bool | |
| `iconSize` | integer > 0 | |
| `hideOnFullscreen` | bool | |

Built-in defaults (bottom layer): bottom, height 30, autoHide false, floating false, margin 0, themeVariant auto,
clock and task list shown, iconSize 24, hideOnFullscreen false, no layout name. These are placeholders for the
library; the shell mapping should substitute Cairo's real defaults.

## Selectors

Every condition in one `match` must hold (AND). An empty `match` matches every monitor (the parser notes that
`default` is the better place for that). A `deviceName` is trimmed; if a key is repeated in one object the last one wins, as in the rest of `cairo-plus.json`.

| Key | Matches | Specificity |
|---|---|---|
| `deviceName` (no wildcard) | exact Windows device name, `DISPLAY2` or `\\.\DISPLAY2`, case-insensitive | 100 |
| `index` | monitor number; primary is 1, the others follow in ordinal, case-insensitive device-name order (same as `monitors`; so `DISPLAY10` comes before `DISPLAY2`) | 90 |
| `deviceName` with `*` / `?` | glob, case-insensitive, tried with and without the `\\.\` prefix | 40 + literal characters (max 59); a lone `*` counts 0 |
| `primary` | `true`: primary only. `false`: every other monitor | 30 |
| `minAspectRatio` | width / height at least this (21:9 is about 2.33, so `2.1` finds ultrawides) | 20 |
| `orientation` | `landscape` (wider than tall) or `portrait` (taller than wide); a square monitor is neither | 15 |
| `minWidth`, `maxWidth`, `minHeight`, `maxHeight` | pixel bounds, inclusive | 12 each |
| `minDpiScale` | DPI scale at least this (`1.5` = 150%) | 10 |

A selector's specificity is the sum of its conditions, so `primary: false` plus `minWidth: 3000` (42) beats
`primary: false` alone (30).

## Layering

1. Built-in defaults.
2. The `default` profile.
3. Every rule that matches the monitor, **sorted by specificity, least specific first**; rules with equal
   specificity keep file order (later wins).

Per property, the last layer that sets it wins. Properties a layer does not mention fall through. `false` counts as
set, so a more specific rule can turn something off.

### Worked example

Monitors: `DISPLAY1` primary 1920x1080, `DISPLAY2` 3440x1440, `DISPLAY3` 1080x1920. Config as in the example above.

`DISPLAY2` is secondary with aspect 2.39, so `secondary` (specificity 30) and `ultrawide` (20) both match. They
apply in specificity order, so `ultrawide` goes first and `secondary` second:

| Property | Value | Supplied by |
|---|---|---|
| `height` | 32 | `default` |
| `floating` | true | rule `ultrawide` |
| `margin` | 8 | rule `ultrawide` |
| `layoutName` | centered-dock | rule `ultrawide` |
| `showClock` | false | rule `secondary` |
| `hideOnFullscreen` | true | rule `secondary` |
| `position` | bottom | built-in |

Had both rules set `margin`, `secondary` would win because it is more specific. If you want the other order, make
the ultrawide rule more specific (for example add `"primary": false` and a `minWidth`), not later in the file.

`--explain-monitors` output for that monitor (`ProfileResolver.ExplainAll`):

```
\\.\DISPLAY2 #2 3440x1440@1920,0 @1x
  rules applied: ultrawide -> secondary
  position          bottom     <- built-in
  height            32         <- default
  ...
  floating          true       <- rule 'ultrawide'
  showClock         false      <- rule 'secondary'
```

## Validation

`ProfilesConfigParser.Parse` never throws. It reports, as findings with a JSON path and a severity:

- unknown keys (typos such as `hieght`), at every level;
- bad enum values and wrong types (in `default` and `profile` the value is dropped and the property stays unset; a rule whose `match` has an invalid value is skipped entirely, because dropping the condition would widen it to more monitors than intended);
- non-positive `height` / `iconSize` / selector bounds (`minWidth: 0` is an error too), negative `margin`;
- rules that can never match (`minWidth` above `maxWidth`, `primary: true` with `index: 2`, portrait with an aspect
  ratio of 1 or more, ...);
- duplicate rule names (explanations would be ambiguous);
- unreachable rules: a rule that a later-applied rule completely overrides (it matches at least the same monitors
  and sets every property the first one sets). The check is conservative and mostly catches repeated selectors;
- rules that set nothing.

## Hot-plug behavior

Windows raises several display-change events for one plug or unplug, and may renumber `DISPLAYn`. The library gives
the adapter three tools:

- `ChangeDebouncer`: a pure state machine (`Notify(now)`, `ShouldFire(now)`, `TimeUntilFire(now)`) with a quiet
  period (400 ms in the adapter) and a maximum wait (2 s) so a constant storm cannot postpone the rebuild
  forever. A clock that goes backwards re-anchors the burst instead of freezing it.
- `MonitorTopologyDiff.Compare(old, new)`: pairs monitors by device name (case-insensitive), then pairs leftovers
  with identical bounds (Windows renumbered the monitor, reported as `Renamed`). The result lists `Added`,
  `Removed`, `Moved` (bounds changed, including resolution), `DpiChanged`, `Renamed`, and `PrimaryChanged` with the
  old and new primary. `AffectedIds` is exactly the set of bars to rebuild; a work-area-only change is ignored on
  purpose, because the taskbar itself changes the work area.
- A reused device name with different bounds counts as the same monitor that moved or changed resolution. If a
  `HardwareId` is supplied on both sides and differs, it is a different physical monitor (remove and add). The
  Windows Forms adapter does not fill `HardwareId` today.

After a diff, `ProfileResolver.ChangedMonitors` compares the old and new resolved profiles, so a primary swap
that changes which rules match is caught even when no monitor moved.

## Relation to the existing per-monitor overrides

`"monitors"` (`Config/MonitorOverrides.cs`) overrides `menuBar` and `taskbar` bar *settings* (opacity, layout,
floating, ...) by selector, with a fixed order: device name, number, primary/secondary, `*`.
`monitorProfiles` is a separate, complementary layer:

| | `monitors` (existing) | `monitorProfiles` (this module) |
|---|---|---|
| Selectors | `*`, `primary`, `secondary`, number, device name | adds glob names, resolution, orientation, aspect ratio, DPI, all combinable |
| Precedence | fixed by selector kind | computed from how specific the whole rule is |
| Covers | menu bar and taskbar appearance | taskbar placement and behavior (position, auto-hide, clock, task list, icon size, ...) |
| Explainable | no | yes, per property |
| Hot-plug | bars re-read the config | diff tells the shell which bars to rebuild |

The code does not touch `MonitorOverrides`. Both use the same monitor numbering (primary first, then ordinal case-insensitive
device-name order), so `index` and the existing numeric selectors agree. The library has its own
`MonitorInfo`, in `CairoDesktop.Features.MonitorProfiles`; the existing one is in
`CairoDesktop.Customization.Config`. Do not import both namespaces in one file.

Suggested precedence once wired (not implemented): profile output is the base for the taskbar, then `monitors`
overrides apply on top, field by field, so an explicit `monitors` entry always wins over a profile.

## Wiring status

Done and tested on Linux (142 unit tests, `dotnet test Tests/CairoDesktop.Features.Tests`):
selectors, glob, layering with explanations, topology diff, debouncer, lenient parser with findings.

Written but **never compiled** (WPF cannot build on the authoring machine), in
`CairoDesktop.Customization/Features/MonitorProfiles/`:
`ScreenSnapshot` (Screen.AllScreens to `MonitorInfo`, per-monitor DPI via `GetDpiForMonitor`) and
`MonitorProfileWatcher` (SystemEvents.DisplaySettingsChanged, UI-thread marshaling, DispatcherTimer debounce,
disposal).

The watcher never lets an exception escape a dispatcher callback, retries up to five times when Windows briefly
reports no screens, and its `Changed` event carries both `ChangedProfileIds` (settings differ) and `Diff.AffectedIds`
(moved, resized or re-DPI'd); rebuild the union, since a bar whose monitor only moved keeps identical settings.

**Not done**:

- `CairoDesktop.Customization.csproj` has no `ProjectReference` to `CairoDesktop.Features`, so the adapter will not
  build until one is added.
- `CairoPlusConfig` has no `MonitorProfiles` property, and nothing calls `ProfilesConfigParser`.
- Nothing instantiates the watcher or reads `ResolvedProfile`; `BarCustomizer` / `BarHost` do not use it.
- No `--explain-monitors` command; `ProfileResolver.ExplainAll` produces the text for one.
- Taskbar properties without an existing bar setting (`autoHide`, `hideOnFullscreen`, `iconSize`, `showTaskList`,
  `showClock` per bar) need mapping to what Cairo's taskbar can actually do.
- Bounds are physical pixels only if Cairo runs per-monitor DPI aware; otherwise resolution selectors see scaled
  sizes. Check on a mixed-DPI setup.
