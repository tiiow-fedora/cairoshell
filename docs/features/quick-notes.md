# Quick notes

A bar widget (`widget:quicknotes`) for jotting a line down without opening anything. Off unless you build with
`-p:CairoExtras=true` and add the widget to a bar.

## Configuration

```jsonc
"widgets": {
  "notes": {
    "type": "quicknotes",
    "label": "Notes",        // shown alone when empty, otherwise "Notes 3"
    "maxNotes": 50,          // 1..1000; the oldest note is dropped beyond this
    "maxLength": 500,        // 10..5000 characters per note; longer text is cut
    "toolTipNotes": 8        // newest notes listed in the hover tooltip
  }
},
"menuBar": { "layout": { "right": ["widget:notes", "tray", "clock"] } }
```

Out-of-range numbers are clamped and logged.

## Behaviour

- **Left click**: a small box opens under the widget. Type, Enter saves, Esc or clicking away cancels. Blank text is ignored.
- **Right click**: the newest ten notes. Click one to copy it, or use its **Delete** sub-item. **Clear all** empties the list.
- **Hover**: the newest notes with their date and time.
- Line breaks and tabs become spaces.
- Notes are saved to `%LOCALAPPDATA%\Cairo Desktop\quick-notes.txt` (one note per line: ISO time, tab, text) after every change and loaded on start.
  Every quicknotes widget (for example one per monitor) shares the same list.
  Unreadable lines are skipped and logged; the file is plain text and safe to edit.

## Code

- Logic: `CairoDesktop.Features/QuickNotes/QuickNotesModel.cs`. Tests: `Tests/CairoDesktop.Features.Tests/QuickNotes`.
- Adapter: `CairoDesktop.Customization/Features/QuickNotes/QuickNotesWidget.cs` (`#if CAIRO_EXTRAS`).
