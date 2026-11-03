# Command palette

A Spotlight/Alfred-style launcher for Cairo Plus. One hotkey opens a small search box; type a few letters and
run a command, launch an app, do a quick calculation or search the web.

Status: the logic is finished and tested; the WPF window is written but **not compiled or wired in yet**
(see [Wiring status](#wiring-status)).

## Config

In `cairo-plus.json`, section `commandPalette`. Comments and trailing commas are allowed. Every key is optional.

```jsonc
{
  "commandPalette": {
    "enabled": true,
    "hotkey": "Alt+Space",                       // needs at least one modifier; Ctrl, Alt, Shift, Win (Meta/Super also accepted)
    "maxResults": 8,                             // 1-50
    "webSearchUrl": "https://duckduckgo.com/?q={q}",   // http(s) only, {q} must come after the host (path, query or fragment)
    "providers": { "commands": true, "apps": true, "calculator": true, "webSearch": true },
    "commands": [
      { "title": "Open notes", "run": "notepad.exe", "args": "%USERPROFILE%\\notes.txt" },
      { "title": "Task Manager", "run": "taskmgr.exe" }
    ]
  }
}
```

`run` and `args` may contain `%ENVIRONMENT%` variables. `run` is started directly (no shell) unless it is an existing
file or folder, which opens with its default app. `providers.apps: false` also hides `App` items from host providers.

Invalid values never stop Cairo: each one produces a finding (for example
`commandPalette.maxResults 1000 is out of range 1-50; using 50.`) and the default is used.

## Query syntax

| Type | Meaning | Example |
|------|---------|---------|
| `text` | Everything: commands, apps, plus a calculator answer if the text looks like maths | `notes`, `12*12` |
| `>text` | Commands only | `>reload` |
| `@text` | Apps only | `@fire` |
| `=expr` | Calculator; Enter copies the result | `=2^10`, `=20% of 50` |
| `?text` | Web search using `webSearchUrl` (text is URL-encoded) | `?cats & dogs` |

Calculator: `+ - * / % ^`, parentheses, unary minus, `sqrt abs round(x[,digits]) floor ceil min max`, constants
`pi` and `e`, and `N% of M`. `^` is right-associative and binds tighter than unary minus (`-2^2` is `-4`,
`2^3^2` is `512`). The decimal separator is always `.`, whatever the Windows locale. Division by zero, overflow,
bad syntax and deep nesting give an error line, never a crash.

## How ranking works

1. Each provider returns its items; items whose `Id` was already seen are dropped.
2. With text, the fuzzy matcher scores the title (full weight), keywords (0.6) and subtitle (0.4); items that do not
   match are dropped. The matcher picks the best alignment of the letters, rewarding consecutive letters, word
   starts, camelCase humps and a prefix match, and penalising gaps and a late start.
3. Final score = match score x (1 + frecency boost). Frecency counts launches with an exponential decay
   (half-life 14 days); the boost is `0.5 * log2(1 + frecency)` capped at 1, so a favourite can at most double a
   score. It reorders similar matches but cannot lift a poor one over a clearly better one.
4. With empty text everything has the same base score, so you get most-used first, then alphabetical.
5. Ties break on frecency, title (ordinal, ignoring case), id, then provider order. Results are deterministic.
6. Calculator and web-search answers are "pinned": they skip matching and frecency and sort to the top.

Frecency is serialised with `FrecencyStore.ToJson()` and loaded with `FrecencyStore.FromJson(...)`. Corrupt data
gives an empty store plus a finding. The store keeps at most 500 entries (lowest scores are dropped first).

## Extension points

All in `CairoDesktop.Features/CommandPalette`, namespace `CairoDesktop.Features.CommandPalette`.

- `ICommandProvider { string Name; IReadOnlyList<PaletteItem> Query(PaletteQuery q); }`: add your own source. Return
  items with category `Command` or `App` so the `>` and `@` modes can filter them. A provider that throws becomes
  an entry in `PaletteSearchResult.Diagnostics`; other providers still answer.
- `PaletteItem.Payload` is an opaque string. The built-in providers use `PaletteAction` (`Launch`, `Url`, `Copy`);
  a host may use anything for its own items.
- `PaletteEngine.Create(config, hostProviders, frecency)` builds the engine from a parsed config.
- `PaletteConfigParser.Parse(sectionJson)` / `ParseFromRoot(wholeFileJson)` return the config plus findings.

Thread-safety: the engine is single-threaded by contract (call it from the UI thread). `FrecencyStore` takes a
lock. Everything else is immutable or stateless. The library has no static mutable state and never reads the system
clock or culture; the frecency clock is injected.

## Wiring status

Done and tested on Linux (259 xUnit cases in `Tests/CairoDesktop.Features.Tests/CommandPalette`): fuzzy matcher,
calculator, frecency store, config parser, engine and the built-in providers.

Written but **never compiled** (no Windows machine was available): `CairoDesktop.Customization/Features/CommandPalette/`
(`PaletteWindow.cs`, `PaletteController.cs`). Both are wrapped in `#if CAIRO_FEATURES`, so they add nothing to the
build today. To switch them on:

1. In `CairoDesktop.Customization.csproj` add a `ProjectReference` to `CairoDesktop.Features` and
   `<DefineConstants>$(DefineConstants);CAIRO_FEATURES</DefineConstants>`. Build on Windows and fix any typos.
2. In `CairoPlusService`: parse the section with `PaletteConfigParser.ParseFromRoot`, report findings the same way
   as other config problems, load/save frecency JSON in the Cairo Plus data folder, build the engine with
   `PaletteEngine.Create`, create a `PaletteController`, and register the hotkey (via `HotkeyRegistry`, which
   needs the palette hotkey added to its list) with `controller.Toggle()` as the action.
3. Dispose the controller when the config reloads or Cairo exits.
4. Add an **apps provider** over `CairoDesktop.AppGrabber` (an `ICommandProvider` returning `App` items with a
   `PaletteAction.Launch` payload); there is none yet, so `@` mode is empty until it exists.
5. Items show as plain text; highlighting `TitleMatchIndices` and theming the window are not done.
6. Not done: `commands` such as "reload" or "switch theme" that call `ActionRunner` (user `commands` currently only
   launch a program).
