# Focus timer

A Pomodoro timer as a bar widget (`widget:focus`). Work, short break, work, ... and a long break after every
fourth session. Click to start or pause, right-click to skip a phase or reset.

## Configuration

Placing the widget is the opt-in. Options go under `widgets`:

```jsonc
"widgets": {
  "focus": {
    "type": "focus",
    "workMinutes": 25,            // 1..240
    "shortBreakMinutes": 5,       // 1..120
    "longBreakMinutes": 15,       // 1..240
    "sessionsBeforeLongBreak": 4, // 1..20
    "autoStart": false,           // true: the next phase starts by itself
    "sound": true                 // system "asterisk" sound when a phase ends
  }
},
"menuBar": { "layout": { "right": ["widget:focus", "tray", "clock"] } }
```

Out-of-range numbers are clamped and the change is written to the log.

## Behaviour

| Widget text | Meaning |
|---|---|
| `Focus` | Idle, nothing run yet |
| `Focus 24:59` / `Break 04:12` | Running |
| `Focus 12:00 (paused)` | Paused |
| `Break 05:00 (ready)` | A phase finished and the next waits for a click (`autoStart` off) |

- Seconds round up, so a fresh 25 minute phase reads `25:00`.
- A skipped work session is not counted towards the long break.
- After sleep or hibernate, one late tick finishes the phase that ran out and starts the next one fresh; it does not
  race through several phases.
- State lives in memory only. Restarting Cairo (or a config reload, which rebuilds widgets) resets the timer.

## Code

- Logic: `CairoDesktop.Features/FocusTimer/FocusTimerModel.cs` (no clock access, no WPF). Tests: `Tests/CairoDesktop.Features.Tests/FocusTimer`.
- Adapter: `CairoDesktop.Customization/Features/FocusTimer/FocusTimerWidget.cs`, inside `#if CAIRO_FEATURES`; **not compiled**.
