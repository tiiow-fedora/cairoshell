# Keep awake

A bar widget (`widget:keepawake`) that stops Windows from going to sleep while you are, say, running a long download or
presenting. Click to step through the choices; it turns itself off when the time is up.

## Configuration

```jsonc
"widgets": {
  "awake": {
    "type": "keepawake",
    "presetMinutes": "30,60,120",   // what each click selects, in order; "until off" is always added last
    "keepDisplayOn": false,         // true: the screen stays on too, not just the PC
    "label": "Awake"
  }
},
"menuBar": { "layout": { "right": ["widget:awake", "tray", "clock"] } }
```

Unusable presets (negative, over a week) are dropped and logged; if none are usable the defaults apply.

## Behaviour

| Click | Text | Windows |
|---|---|---|
| (start) | `Awake: off` | normal power settings |
| 1 | `Awake 0:30` (hours:minutes left, counts down) | will not sleep |
| 2, 3 | `Awake 1:00`, `Awake 2:00` | will not sleep |
| 4 | `Awake: on` | will not sleep until turned off |
| 5 | `Awake: off` | back to normal |

- When a timed state runs out the widget returns to `off` and releases the request (checked every 30 seconds, so up to
  that much late; the displayed time is exact).
- Uses `SetThreadExecutionState` (`ES_SYSTEM_REQUIRED`, plus `ES_DISPLAY_REQUIRED` when `keepDisplayOn`). It does not move
  the mouse or fake input, and it does not stop a manual sleep, a closed lid on a laptop set to sleep, or low-battery shutdown.
- One state is shared by every keepawake widget (for example one per monitor). Removing the last widget, or a config
  reload that rebuilds the bars, turns it off.
- State is not saved across restarts.

## Code

- Logic: `CairoDesktop.Features/KeepAwake/KeepAwakeModel.cs`. Tests: `Tests/CairoDesktop.Features.Tests/KeepAwake`.
- Adapter: `CairoDesktop.Customization/Features/KeepAwake/KeepAwakeWidget.cs` (`#if CAIRO_FEATURES`, **not compiled**).
