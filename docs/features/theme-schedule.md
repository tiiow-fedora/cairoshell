# Theme schedule

Switches a theme pack between its **light** and **dark** version by time of day, instead of following the Windows app
mode. Two modes: fixed clock times, or sunrise and sunset at a place you give.

Only packs with `light`/`dark` variants react (Catppuccin, Ayu, Atom One, ...); see
[Light and dark mode](../../CAIRO-PLUS.md#light-and-dark-mode).

## Configuration

Off unless `themeSchedule.enabled` is true. It takes precedence over `"appearance": "auto"` (an explicit
`"appearance": "light"` or `"dark"` still wins).

```jsonc
"themeSchedule": {
  "enabled": true,
  "mode": "fixed",             // "fixed" (default) or "sun"
  "lightFrom": "07:00",        // fixed: 24-hour local time the light version starts
  "darkFrom": "19:00"          // fixed: local time the dark version starts
}
```

```jsonc
"themeSchedule": {
  "enabled": true,
  "mode": "sun",
  "latitude": 51.5074,         // required in sun mode; degrees north
  "longitude": -0.1278,        // required in sun mode; degrees east
  "sunriseOffsetMinutes": 0,   // -180..180, e.g. -30 = light 30 minutes before sunrise
  "sunsetOffsetMinutes": 0
}
```

Mistakes are reported with the key path: a bad time falls back to the default, equal `lightFrom` and `darkFrom` are
replaced by 07:00/19:00, and sun mode without coordinates switches the schedule off.

## Behaviour

- Times are local wall-clock times in the PC's time zone. Daylight-saving changes are honoured; a time that falls in the
  spring-forward gap fires at the first moment after the gap.
- `darkFrom` may come before `lightFrom` (night-shift hours, light in the evening): the latest boundary that has passed decides.
- Sun mode uses the NOAA solar approximation (about a minute at normal latitudes, within a few minutes near the poles).
  Above the polar circles on days with no sunrise or sunset it stays light (midnight sun) or dark (polar night).
- The scheduler wakes at the next boundary and never sleeps longer than 6 hours, so changing the clock, time zone or
  waking from sleep fixes itself.

## Code

- Logic: `CairoDesktop.Features/ThemeSchedule` (`SolarCalculator`, `AppearanceSchedule`, `ScheduleConfigParser`). Tests:
  `Tests/CairoDesktop.Features.Tests/ThemeSchedule`, including sunrise/sunset checks for London, New York and Sydney
  against published times, and polar day and night.
- Adapter: `CairoDesktop.Customization/Features/ThemeSchedule/AppearanceScheduler.cs` (`#if CAIRO_FEATURES`, **not compiled**).
  Wiring it into `CairoPlusService` is in the integration pull request.
