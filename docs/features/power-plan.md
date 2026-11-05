# Power plan

A bar widget (`widget:powerplan`) that shows the active Windows power plan and switches to the next one when clicked.
(A battery widget already exists in Cairo Plus; this is about the plan, not the charge level.) Off unless you build with
`-p:CairoExtras=true` and add the widget to a bar.

## Configuration

```jsonc
"widgets": {
  "power": {
    "type": "powerplan",
    "label": "Power",                          // "Power: Balanced"
    "cycle": "Balanced;High performance",      // optional: names or GUIDs a click steps through; default is every plan
    "interval": 30                             // seconds between checks, 5..600
  }
},
"menuBar": { "layout": { "right": ["widget:power", "tray", "clock"] } }
```

## Behaviour

- The text shows the plan marked active by `powercfg /list`; hover lists all plans (`*` marks the active one).
- **Click**: `powercfg /setactive <guid>` for the next plan in `cycle` (or in the full list), then the list is read again.
  If the active plan is not in `cycle`, the first entry of `cycle` is chosen. Names in `cycle` that match nothing are ignored;
  if none match, all plans are used.
- Plans are matched by GUID in the `powercfg` output, so it works in any Windows display language.
- `powercfg.exe` runs off the UI thread; the bar shows the last known state meanwhile. If `powercfg` fails it is logged once and the text stays `Power: ?`.
- Changing the plan needs no administrator rights for the plans Windows lists. Group policy can lock the plan; the click then does nothing.
- A change made elsewhere (Settings, `powercfg`) shows up at the next check.

## Code

- Logic: `CairoDesktop.Features/PowerPlan/PowerPlanModel.cs`. Tests: `Tests/CairoDesktop.Features.Tests/PowerPlan`.
- Adapter: `CairoDesktop.Customization/Features/PowerPlan/PowerPlanWidget.cs` (`#if CAIRO_EXTRAS`).
