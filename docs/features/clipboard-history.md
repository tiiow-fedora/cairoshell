# Clipboard history

A small widget (`widget:clipboard`) that remembers the text you copy, lets you search it, pin the clips you
keep reaching for, copy one back, or run a quick transform on it.

## Privacy stance (read this first)

A clipboard recorder sees passwords. This feature is built so that is the exception, not the rule:

- **Off until you place it.** Nothing is recorded unless the widget is on a bar (or `clipboardHistory.enabled` is true).
- **Memory only by default.** `persist` is `false`. Nothing touches disk unless you turn it on.
- **Sensitive clips are never stored.** A clip that fails the policy is dropped before it reaches the history: it is
  not shown, not saved, not logged (log lines never contain clip text), and not written to the history file even if
  `persist` is on. The file is also re-checked on load, so a hand-edited file cannot bring a secret back.
- **Password managers are honoured.** A clip is skipped when the owning app matches `excludeApps`, or when it carries
  any of the Windows markers password managers set: `ExcludeClipboardContentFromMonitorProcessing`,
  `CanIncludeInClipboardHistory` = 0, `CanUploadToCloudClipboard` = 0.
- **Secret heuristics** (on by default, each can be disabled in code options):

| Heuristic | Blocks | Deliberately allows |
|---|---|---|
| Card number | 13-19 digits that pass the Luhn check, alone or grouped by spaces/dashes (`4111 1111 1111 1111`) | Failing Luhn, all-same digits, a bare digit run inside prose, 13-digit epoch-millisecond timestamps |
| AWS key | `AKIA...` / `ASIA...` plus 16 upper-case alphanumerics | Near misses, lower case |
| Private key | `-----BEGIN ... PRIVATE KEY-----` | Public keys and certificates |
| JWT | `eyJ...` `.` `eyJ...` `.` signature (also inside a `Bearer` header) | A lone `eyJ...` segment |
| High-entropy token | A single token of 24+ characters with 3 of 4 character classes and >= 4.0 bits/char | Sentences, URLs, paths, e-mail, JSON, identifiers, git SHA-1/SHA-256 and any other pure-hex string |

Known gap, by design: a hex-encoded API key looks like a checksum and is not caught. Heuristics reduce risk; they are
not a guarantee. Exclude your password manager by name and keep `persist` off if the clipboard matters to you.

## Configuration

Intended `cairo-plus.json` section (parsed and validated by `ClipboardConfigParser`; bad values fall back to the
default with a finding, unknown keys produce a warning):

```jsonc
"clipboardHistory": {
  "enabled": true,
  "maxItems": 100,          // 1..1000, pinned entries count too
  "maxBytes": 5242880,      // total UTF-8 bytes, 1024..2147483647
  "ttlMinutes": 1440,       // unpinned clips older than this are dropped; 0 = never
  "persist": false,         // true writes %LOCALAPPDATA%\Cairo Desktop\clipboard-history.json
  "excludeApps": ["KeePass*", "1Password*", "Bitwarden*"],   // replaces the default list; [] = none
  "pinnedMax": 20,          // pinned clips are never evicted, so they are capped separately
  "hotkey": "Ctrl+Alt+V",   // null disables; needs a modifier
  "previewLength": 80       // 10..500 characters shown per row
}
```

Defaults: `enabled` false, `persist` false, `ttlMinutes` 1440, the password-manager globs above.

## Behaviour

- Whitespace-only clips are ignored. Text is trimmed (`Trim`, `TrimEnd` or `None` in code).
- Copying the same text again moves the existing entry to the top and keeps its pin.
- Eviction removes the **oldest unpinned** entry first, by count and by total bytes. Pinned entries are never evicted.
  A clip that cannot fit even with every unpinned entry gone is rejected instead of emptying the history for nothing.
- List order: pinned first, then newest. If the system clock moves backwards, order is unaffected and nothing
  expires early.

## Clip kinds

| Kind | Detected as |
|---|---|
| Url | `http`, `https`, `ftp`, `ftps` or `www.` with no spaces |
| FilePath | `C:\...`, `\\server\share`, `/unix/style/path` |
| Color | `#rgb`, `#rgba`, `#rrggbb`, `#rrggbbaa`, `rgb()`, `rgba()` |
| Email | `name@host.tld` |
| Number | integers, decimals, thousands commas, exponent |
| Json | starts/ends with braces or brackets and parses |
| Code | multi-line text with brace pairs or semicolon-terminated lines |
| Plain | everything else |

## Transforms

Pure functions, looked up by name through `TransformRegistry`. Failures return a message; nothing throws.

| Name | Does |
|---|---|
| `trim` | Trim ends |
| `upper` / `lower` | Invariant-culture case change |
| `title` | Capitalise each word, lower-case the rest (apostrophes stay inside words) |
| `strip-formatting` | LF newlines, drop zero-width and ANSI codes, odd spaces to plain, trim line ends |
| `join-lines` | Non-empty lines joined with one space |
| `sort-lines` | Case-insensitive, ties broken ordinally; keeps CRLF if the input used it |
| `dedupe-lines` | Keep the first copy of each line |
| `url-encode` / `url-decode` | Percent-encoding (space is `%20`; `+` is left alone when decoding) |
| `base64-encode` / `base64-decode` | UTF-8 text; decode accepts URL-safe alphabet, missing padding, whitespace; errors on non-UTF-8 |
| `json-pretty` / `json-minify` | Two-space indent / compact; unicode preserved; invalid JSON is an error |

## Hotkey

`hotkey` is validated and normalised (`alt+ctrl+v` becomes `Ctrl+Alt+V`; a bare key is refused). **Not yet wired**:
the widget does not register it. Use the bar button for now.

## Wiring status

Honest summary of what exists and what does not:

| Piece | State |
|---|---|
| Store, kinds, sensitive policy, transforms, persistence, config parser | Done, WPF-free, covered by unit tests (`Tests/CairoDesktop.Features.Tests/ClipboardHistory`) |
| `ClipboardMonitor` (message-only window, `AddClipboardFormatListener`) and `widget:clipboard` | Written, **never compiled or run** (authored on Linux). The files are wrapped in `#if CAIRO_FEATURES` so they cannot break a build |
| Enabling the adapter | `CairoDesktop.Customization.csproj` needs `<ProjectReference Include="..\CairoDesktop.Features\CairoDesktop.Features.csproj" />` and `<DefineConstants>$(DefineConstants);CAIRO_FEATURES</DefineConstants>`, and the factory must be added to the widget registry. None of this is done yet |
| `clipboardHistory` section in `cairo-plus.json` | Parser exists; the widget currently reads the same names from its own widget options (`maxItems`, `ttlMinutes`, `persist`, `pinnedMax`, `previewLength`, comma-separated `excludeApps`) |
| Transforms in the popup, hotkey, settings tab | Not in the UI yet |
| Plain text only | Images, files and rich text are ignored |
