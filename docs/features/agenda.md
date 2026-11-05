# Calendar agenda

A bar widget (`widget:agenda`) that shows your next calendar event, read from a local `.ics` file. There is no account
sign-in and no network access: point it at an exported file, or at one your calendar app or sync tool keeps updated
(Outlook, Thunderbird, Google "secret address in iCal format" downloaded by a tool of your choice, and so on). Off unless
you build with `-p:CairoExtras=true` and add the widget to a bar.

## Configuration

```jsonc
"widgets": {
  "agenda": {
    "type": "agenda",
    "file": "%USERPROFILE%\\Documents\\calendar.ics",   // required; environment variables are expanded
    "horizonDays": 7,        // 1..60 days ahead
    "maxItems": 12,          // 1..50 lines in the tooltip
    "maxTextLength": 40,     // 8..200; longer text ends in "…"
    "timeFormat": "HH:mm",   // .NET format, e.g. "h:mm tt"
    "label": "Agenda",       // shown when nothing is coming up
    "interval": 30           // seconds between checks, 5..600
  }
},
"menuBar": { "layout": { "right": ["widget:agenda", "tray", "clock"] } }
```

## Behaviour

| Situation | Text |
|---|---|
| An event is running | `Now Standup` |
| The next event is today | `Next 14:30 Standup` |
| The next event is another day | `Next Thu 09:00 Review` |
| Only all-day events today | `Today Holiday` |
| Nothing in the horizon | `Agenda` |
| No `file` set / file missing | `Agenda: no file` / `Agenda: ?` (the reason is logged) |

- Hover lists upcoming events by day; all-day events say `all day`.
- The file is re-read when its modified time changes. **Left click** re-reads it now; **right click** has *Open calendar file*.
  If the file cannot be read, the last good data stays.
- Times: UTC (`...Z`), zoned (`TZID=...`, IANA or Windows names) and floating (no zone, taken as this PC's zone) are converted to local time.
  Events with an unknown `TZID` are skipped and reported in the log. Cancelled events are skipped.
- Recurrence: `DAILY`, `WEEKLY` (with `BYDAY=MO,WE,...`), `MONTHLY`, `YEARLY`, with `INTERVAL`, `COUNT` and `UNTIL`. The wall-clock time is kept across daylight saving changes.
  **Not supported**: `EXDATE`/`RDATE` (excluded or extra dates), `BYMONTHDAY`, `BYSETPOS`, ordinal weekdays such as `1MO`, and the like.
  A series using them shows only its first occurrence, and a line is logged. Edited single occurrences of a series (`RECURRENCE-ID`) are not merged.
- `DURATION` instead of `DTEND` is not read; such an event counts as an instant.

## Code

- Logic: `CairoDesktop.Features/Agenda/IcsParser.cs` and `AgendaPlanner.cs`. Tests: `Tests/CairoDesktop.Features.Tests/Agenda`.
- Adapter: `CairoDesktop.Customization/Features/Agenda/AgendaWidget.cs` (`#if CAIRO_EXTRAS`).
