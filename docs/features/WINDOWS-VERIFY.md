# Verifying the extras on Windows

Focus timer, theme schedule, keep awake, world clock, wallpaper slideshow, quick notes, power plan and calendar agenda. Their logic is unit-tested on Linux; the
WPF/Win32 adapters and the wiring into `CairoPlusService` and `WidgetRegistry` **were never compiled**. Expect a compile error or
two on the first build. The files to look at first are listed at the end.

## 1. Build

From the repo root, in PowerShell or cmd. The `-p:CairoExtras=true` switch is what compiles the extras; without it you get the old build.

```bash
cd "Cairo Desktop"
dotnet build "Cairo Desktop/Cairo Desktop.csproj" -f net6.0-windows -p:TargetFrameworks=net6.0-windows -p:Platform=x64 -p:CairoExtras=true
```

(These are the commands from the Building section of `CAIRO-PLUS.md` plus the flag. With the .NET 10 SDK you can drop
the `TargetFrameworks` override. The override used to fail restore with `NETSDK1005` because it also reached the
netstandard2.0 `CairoDesktop.Features` project; that project now keeps its own `TargetFrameworks`
(`TreatAsLocalProperty`), so the command works from a clean checkout.) The output is `Cairo Desktop/bin/x64/Debug/net6.0-windows/win-x64/CairoDesktop.exe`.

Run the two test projects (they build with or without the flag):

```bash
dotnet test "Tests/CairoDesktop.Features.Tests/CairoDesktop.Features.Tests.csproj"
dotnet test "Tests/CairoDesktop.Customization.Tests/CairoDesktop.Customization.Tests.csproj" -f net6.0-windows -p:TargetFrameworks=net6.0-windows -p:Platform=x64
```

Confirm the default build is still clean (no flag):

```bash
dotnet build "Cairo Desktop/Cairo Desktop.csproj" -f net6.0-windows -p:TargetFrameworks=net6.0-windows -p:Platform=x64
```

Exit any running Cairo first (Cairo menu > Exit Cairo), then start the fork's `CairoDesktop.exe`.

## 2. Config used by the checklist

Open the config with Cairo menu > Cairo Settings > Theme Packs > Open config file, and make sure it contains (merge with what is there):

```jsonc
{
  "enabled": true,
  "theme": "Catppuccin",             // has light and dark versions
  "widgets": {
    "focus":  { "type": "focus", "workMinutes": 1, "shortBreakMinutes": 1, "autoStart": false },
    "awake":  { "type": "keepawake", "presetMinutes": "1,2" },
    "world":  { "type": "worldclock", "zones": "Tokyo=Asia/Tokyo;NYC=America/New_York;London=Europe/London" },
    "nextwall": { "type": "wallpaper" },
    "notes":  { "type": "quicknotes" },
    "power":  { "type": "powerplan" },
    "agenda": { "type": "agenda", "file": "C:\\Path\\To\\calendar.ics" }
  },
  "menuBar": { "layout": { "right": ["widget:focus", "widget:awake", "widget:world", "widget:nextwall", "widget:notes", "widget:power", "widget:agenda", "tray", "clock"] } },
  "wallpaperSlideshow": { "enabled": true, "folder": "C:\\Path\\To\\A\\Folder\\With\\Pictures", "intervalMinutes": 1, "changeOnStart": true }
}
```

(Short minutes are for testing; the defaults are 25 / 5.)

## 3. Checklist

### Focus timer
- [ ] The menu bar shows `Focus`.
- [ ] Left click: it counts down `Focus 00:59`, `00:58`, ...
- [ ] Left click again: shows `(paused)` and stops. Click again: resumes.
- [ ] Right click: Skip and Reset appear. Skip changes the text to `Break 01:00 (ready)`. Reset returns to `Focus`.
- [ ] Let one minute run out: a system sound plays and the text changes to `Break 01:00 (ready)`. Nothing keeps ticking by itself.
- [ ] Set `"autoStart": true`, save: the break starts by itself after a work phase.

### Keep awake
- [ ] Shows `Awake: off`. Click: `Awake 0:01`, then `Awake 0:02`, then `Awake: on`, then `Awake: off`.
- [ ] While on, run `powercfg /requests` in an admin terminal: the Cairo process shows under SYSTEM.
- [ ] After a timed state runs out, `powercfg /requests` no longer lists it (allow up to 30 s).
- [ ] With `"keepDisplayOn": true`, `powercfg /requests` lists it under DISPLAY as well.

### World clock
- [ ] Shows three clocks, e.g. `Tokyo 02:15 +1d   NYC 13:15   London 18:15`. Times match a reference.
- [ ] Hover: tooltip lists date, UTC offset and `[work hours]` for zones in 9 to 18 on weekdays.
- [ ] Change a zone to `Nowhere/Land`: the others still show and the Theme Packs tab problem list names it.

### Wallpaper slideshow
- [ ] On start (`changeOnStart`), the Windows wallpaper changes to a picture from the folder.
- [ ] After a minute it changes again; with `"shuffle": true` it does not repeat until every picture has been shown.
- [ ] Click `Next wallpaper` on the bar: it changes immediately.
- [ ] Set `"enabled": false` and save: it stops. Point `folder` at a missing path: a message appears in the log, the desktop does not hang.
- [ ] Restart Cairo within the interval: it does not change right away (unless `changeOnStart`), and
      `%LOCALAPPDATA%\Cairo Desktop\wallpaper-slideshow.json` exists.

### Theme schedule
- [ ] Add `"themeSchedule": { "enabled": true, "lightFrom": "HH:MM", "darkFrom": "HH:MM" }` with `lightFrom` two minutes from now and
      `darkFrom` a few minutes after it (use the PC's current time). Before `lightFrom` the Catppuccin dark version is shown.
- [ ] At `lightFrom` the bars switch to the light version without a restart, within a few seconds; at `darkFrom` back to dark.
- [ ] Change the Windows app mode (Settings > Personalization > Colors): nothing changes while the schedule is on.
- [ ] `"appearance": "dark"` overrides the schedule.
- [ ] Sun mode: `"mode": "sun", "latitude": ..., "longitude": ...` with your place; compare the flip times to an almanac (a few minutes' error is expected).
- [ ] `"themeSchedule": { "enabled": true, "mode": "sun" }` (no coordinates) puts an error in the problem list and the schedule stays off.

### Quick notes
- [ ] The bar shows `Notes`. Click: a box opens under it with the cursor in it. Type text, Enter: it closes and the bar shows `Notes 1`.
- [ ] Esc, or clicking elsewhere, closes the box without adding. Blank text adds nothing.
- [ ] Hover: the note is listed with date and time. Right click: the note appears; click copies it (paste somewhere to check); Delete removes it; Clear all empties.
- [ ] `%LOCALAPPDATA%\Cairo Desktop\quick-notes.txt` has the notes. Restart Cairo: they are still there.

### Power plan
- [ ] The bar shows `Power: <your active plan>`; hover lists all plans with `*` on the active one.
- [ ] Click: the plan changes (compare with `powercfg /getactivescheme` or Settings > Power) and the text updates within a second. No console window flashes.
- [ ] Change the plan in Settings: the bar follows within the interval (30 s).
- [ ] With `"cycle": "Balanced;Power saver"` only those two alternate.

### Calendar agenda
- [ ] Make a small `calendar.ics` with one event starting in about 10 minutes (UTC `DTSTART:...Z`), one all-day event today and one weekly `RRULE:FREQ=WEEKLY;BYDAY=MO,WE`.
      The bar shows `Next HH:MM <title>` in your local time; hover lists the events by day with the all-day one marked.
- [ ] Edit the file (change the title) and save: the bar updates within the interval, or at once after a click.
- [ ] When the event starts the text becomes `Now <title>`.
- [ ] Missing file: the bar shows `Agenda: ?` and the log names the path. No `file` option: `Agenda: no file`.
- [ ] Right click > Open calendar file opens it in the default app.

### Regression
- [ ] Remove the flag (default build): Cairo behaves as before; `widget:focus` etc. are reported as unknown widget types.
- [ ] Switching theme packs and the existing widgets (clock, cpu, memory) still work with the flag on.

## 4. If it does not compile

The never-compiled files, in the order to read them:

1. `CairoDesktop.Customization/Features/CairoPlusExtras.cs`
2. `CairoDesktop.Customization/CairoPlusService.cs` (the four `#if CAIRO_FEATURES || CAIRO_EXTRAS` blocks)
3. `CairoDesktop.Customization/Widgets/WidgetRegistry.cs` (one `#if` block)
4. `Features/QuickNotes/QuickNotesWidget.cs`, `Features/PowerPlan/PowerPlanWidget.cs`, `Features/Agenda/AgendaWidget.cs`,
   `Features/WallpaperSlideshow/SlideshowController.cs`, `Features/KeepAwake/KeepAwakeWidget.cs`, `Features/FocusTimer/FocusTimerWidget.cs`,
   `Features/WorldClock/WorldClockWidget.cs`, `Features/ThemeSchedule/AppearanceScheduler.cs`
5. `CairoDesktop.Customization.csproj` (the `CairoExtras` property groups)

Also confirm on the .NET Framework 4.8 target (`-f net480 -p:TargetFrameworks=net480`) that the Features library (netstandard2.0) resolves.
