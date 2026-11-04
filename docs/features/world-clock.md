# World clock

One bar widget (`widget:worldclock`) showing the time in several places, with a "+1d" marker when it is already
tomorrow there, and a tooltip with date, UTC offset and a mark for zones that are in working hours.

```
Tokyo 00:00 +1d   NYC 10:00   London 15:00
```

## Configuration

```jsonc
"widgets": {
  "world": {
    "type": "worldclock",
    "zones": "Tokyo=Asia/Tokyo;NYC=America/New_York;London=Europe/London",
    "format": "HH:mm",      // any .NET time format, e.g. "h:mm tt"
    "separator": "   ",
    "labels": true,         // false: just the times
    "dayDelta": true,       // false: hide +1d / -1d
    "workStart": 9,         // tooltip [work hours] marker, weekdays
    "workEnd": 18,
    "interval": 10          // seconds between refreshes, 1..60
  }
},
"menuBar": { "layout": { "right": ["widget:world", "tray", "clock"] } }
```

- `zones` is a `;`-separated (commas and line breaks also work) list of `Label=ZoneId`, or just `ZoneId`
  (`Asia/Tokyo` is labelled "Tokyo", `America/New_York` "New York").
- A zone id may be written as an IANA name (`Europe/London`) or a Windows name (`GMT Standard Time`). If the PC only knows
  the other form, Cairo tries it (a built-in table covers about 45 common zones; any id the PC knows works as given).
- Unknown zones are skipped and logged once; the rest still show. A bad `format` falls back to `HH:mm` and is logged.
- The day marker compares each zone's date with the PC's own date.
- Time and date use your Windows regional settings.

## Code

- Logic: `CairoDesktop.Features/WorldClock/WorldClockFormatter.cs` (`ClockZoneSpec`, `ZoneResolver`, `WorldClockFormatter`).
  Tests: `Tests/CairoDesktop.Features.Tests/WorldClock`.
- Adapter: `CairoDesktop.Customization/Features/WorldClock/WorldClockWidget.cs` (`#if CAIRO_FEATURES`, **not compiled**).
