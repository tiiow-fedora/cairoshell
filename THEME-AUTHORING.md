# Writing a Cairo Plus theme pack

This guide takes you from an empty folder to a pack you can share. [CAIRO-PLUS.md](CAIRO-PLUS.md) has the
reference for bar layout, widgets and hotkeys; this file covers the pack itself.

- [Create a pack](#create-a-pack)
- [Choose colours](#choose-colours)
- [Add a day and night version](#add-a-day-and-night-version)
- [Add a wallpaper, font and icons](#add-a-wallpaper-font-and-icons)
- [Shape the bars](#shape-the-bars)
- [Check the pack](#check-the-pack)
- [Fix what the checker reports](#fix-what-the-checker-reports)
- [Share the pack](#share-the-pack)
- [Field reference](#field-reference)

## Create a pack

1. Run `CairoDesktop.exe --new-theme "My Theme"` from a terminal. To start from a bundled pack, add
   `--from Nord`.
   You get the folder `%LOCALAPPDATA%\Cairo Desktop\ThemePacks\MyTheme\` containing a commented `theme.json`.
2. Run `CairoDesktop.exe --apply-theme MyTheme`. You get the pack applied, and Cairo Plus is switched on.
3. Open `theme.json` in an editor and save after each edit. You get the desktop updated within about half
   a second, because Cairo Plus watches the active pack's folder.

The folder name is the pack's id, the value `"theme"` takes in `cairo-plus.json`. `"name"` inside
`theme.json` is only the label shown in the Theme Packs tab. A pack in your own folder replaces a bundled
pack with the same folder name.

`theme.json` allows `//` and `/* */` comments and trailing commas.

## Choose colours

Set the nine roles under `"palette"`. Colours are `#RGB`, `#RRGGBB` or `#AARRGGBB`.

| Role | Used for |
|---|---|
| `background` | menu bar and taskbar background, search title bar, desktop toolbar |
| `surface` | menus, dialogs, search results, task thumbnails |
| `overlay` | hover backgrounds in menus, the taskbar and thumbnails, dialog buttons |
| `border` | bar borders, separators, menu borders, dialog button borders |
| `text` | menu, taskbar, search and dialog text |
| `subtext` | secondary text: menu categories, search subtext, disabled controls, "no items" notes |
| `accent` | highlights: the hovered menu item, the active task button, progress bars, default-button border |
| `accentText` | text on top of `accent` (dialog buttons while hovered or pressed) |
| `urgent` | flashing task buttons and the close button on thumbnails |

Only `background` is needed. A role you leave out is derived:

| Role left out | Derived as |
|---|---|
| `text` | white on a dark background, black on a light one |
| `surface` | `background` mixed 6% toward `text` |
| `overlay` | `surface` mixed 12% toward `text` |
| `border` | `background` mixed 20% toward `text` |
| `subtext` | `text` mixed 35% toward `background` |
| `accent` | `#3B82F6` |
| `accentText` | black or white, whichever has the higher contrast against `accent` |
| `urgent` | `#E5534B` |

To change one colour the roles don't reach, put the Cairo resource key under `"resources"`, for example
`"resources": { "MenuTopBorderBrush": "#FF79C6" }`. The keys are the `x:Key` names in
`Cairo Desktop/Themes/Cairo.xaml`. A key that isn't in that file is reported as a typo.

`"baseTheme"` picks the Cairo XAML theme that sits underneath your colours: `Default`, `Fade`, `Flat`,
`Flat Accent`, `MiniMeow` or `White`. It defaults to `Default`, so the XAML theme chosen on Cairo's General
tab doesn't show through.

Set `"darkMode"` to `true` for a dark palette and `false` for a light one. `true` asks Windows to draw Cairo's
native menus in dark mode (on Windows versions that support it), and the checker warns when it disagrees with
the background colour. Windows only allows this to be turned on once per run: after a dark version has been
applied, native menus stay dark until Cairo restarts, even if the pack switches to its light version.

## Add a day and night version

A pack can carry a light and a dark version of itself. Put the version you want as the default at the top
level, then list what changes under `"light"` or `"dark"`:

```jsonc
{
  "name": "Catppuccin",
  "darkMode": true,
  "palette": { "background": "#1E1E2E", "text": "#CDD6F4", "accent": "#89B4FA", "accentText": "#1E1E2E" },
  "resources": { "MenuTopBorderBrush": "#CBA6F7" },
  "wallpaper": { "file": "wallpaper-night.jpg" },

  "light": {
    "palette": { "background": "#EFF1F5", "text": "#4C4F69", "accent": "#1E66F5", "accentText": "#FFFFFF" },
    "resources": { "MenuTopBorderBrush": "#8839EF" },
    "wallpaper": { "file": "wallpaper-day.jpg" },
  },
}
```

1. Save the pack. While Windows is in light mode (Settings > Personalization > Colors > "Choose your default
   app mode"), you get the `"light"` values applied on top of the pack. In dark mode you get the top-level values.
2. Switch the Windows app mode. You get the desktop re-themed within about half a second, with no restart.

What a variant may contain:

| Field | Behaviour |
|---|---|
| `palette` | Merged role by role into the pack's palette. Roles the variant leaves out keep the pack's value. |
| `resources` | Merged key by key into the pack's resources. |
| `wallpaper` | Replaces the pack's wallpaper entirely. |
| `darkMode` | Replaces `darkMode`. Left out, `"light"` means `false` and `"dark"` means `true`. |

Switching from a dark version to a light one leaves Cairo's native menus dark until Cairo restarts (see
`darkMode` above); everything drawn by the theme changes immediately.

Everything else (font, icons, bars, widgets, hooks) is shared by both versions.

Derived roles come from the merged palette. If the top level sets `surface` and the light variant changes
`background` without setting `surface`, the light version keeps the top level's `surface`. Set every role you
want to change in the variant.

A pack may define only `"light"` or only `"dark"`; the other mode then uses the top-level values.

To force one version regardless of Windows, put `"appearance": "light"` or `"appearance": "dark"` in
`cairo-plus.json`. The default is `"auto"`. Hook scripts receive the version in effect as `CAIRO_APPEARANCE`
(`light` or `dark`).

The checker validates each variant as it will look when applied, with findings located under `light.` or
`dark.` (for example `light.palette.text`).

## Add a wallpaper, font and icons

**Wallpaper.** Put the image in the pack folder and set `"wallpaper": { "file": "wallpaper.jpg", "style": "fill" }`.
Styles: `fill`, `fit`, `stretch`, `center`, `tile`, `span`. The path is relative to the pack folder and may
not leave it. When Cairo runs on top of Explorer, applying the pack changes the Windows wallpaper, and it
stays after you switch packs.

**Font.** `"font": { "family": "Segoe UI", "size": 12 }` uses an installed font. To ship a font, add
`"file": "fonts/MyFont.ttf"` as well; it loads from the pack and nothing is installed system-wide. `family` must
then match the font file's internal family name. `size` is the base size; other sizes scale with it. The
checker accepts 6 to 48.

**Icons.** A PNG named after a Cairo icon key, such as `icons/MenuIcon.png`, replaces that icon with no manifest
entry. The keys are `MenuIcon`, `SearchIcon`, `ActionCenterIcon`, `VolumeIcon`, `VolumeLowIcon`,
`VolumeOffIcon`, `VolumeMuteIcon`, `DateTimeIcon`, `DesktopOverlayIcon`, `TaskListMenuIcon`, `TaskViewIcon`,
`DesktopToolbarBackIcon`, `DesktopToolbarUpIcon`, `DesktopToolbarForwardIcon`, `DesktopToolbarBrowseIcon` and
`DesktopToolbarHomeIcon`. Draw them at twice their display size. To use a different file name, list it under `"icons"`.
`"appIcons": { "notepad.exe": "icons/apps/notepad.png" }` replaces the icon of that program's taskbar and
quick-launch buttons.

## Shape the bars

`"menuBar"` and `"taskbar"` take the same settings as in `cairo-plus.json`: `layout`, `floating`, `margin`,
`cornerRadius`, `spacing`, `opacity` and `blur`. See [Bar layout](CAIRO-PLUS.md#bar-layout) and
[Shapes and effects](CAIRO-PLUS.md#shapes-and-effects). Define the widgets your layout uses under `"widgets"`.

Values in the user's `cairo-plus.json` win over the pack, field by field, and so do the user's
[per-monitor settings](CAIRO-PLUS.md#per-monitor-settings). A pack cannot define per-monitor settings,
because it doesn't know the user's monitors.

## Check the pack

Run `CairoDesktop.exe --validate-theme MyTheme` from a terminal. With no name it checks every pack. The exit
code is 1 when any pack has an error. In the settings window, select a pack on the Theme Packs tab: the
"Checks for the selected pack" section lists its errors and warnings.

Each finding is one of:

- **error**: something that will not work, such as a missing file.
- **warning**: probably a mistake, or hard to read.
- **info**: worth knowing; shown by `--validate-theme`, not in the settings window.

## Fix what the checker reports

| Finding | What to do |
|---|---|
| `file '...' was not found in the pack` | Put the file at that path inside the pack folder, or correct the path. |
| `points outside the theme pack folder` | Move the file into the pack folder and use a relative path without `..`. |
| `is not a resource key in Cairo.xaml` | Correct the spelling against `Cairo Desktop/Themes/Cairo.xaml`. |
| `text on background has a contrast ratio of N:1` | Make `text` lighter or darker against `background`. The checker asks for 4.5:1 for text on `background`, text on `surface` and `accentText` on `accent`, and 3:1 for `subtext` on `background`. |
| `darkMode is true but the background colour is light` | Set `darkMode` to match the background, or change the background. |
| `Unknown palette role` | Use one of the nine role names. |
| `'x' is not a colour` | Use `#RGB`, `#RRGGBB` or `#AARRGGBB`. |
| `'widget:x' is not defined under "widgets"` | Add the widget under `"widgets"` or use a built-in type: `clock`, `cpu`, `memory`, `battery`, `network`, `command`. |
| `margin has no effect unless "floating" is true` (info) | Set `"floating": true` or remove `margin`. |
| `hooks run programs on your PC` (info) | Nothing to fix. The hooks only run when the user sets `"allowPackHooks": true`. |

The bundled packs are held to the same standard: a unit test fails when any of them has an error or a warning.

## Share the pack

1. Run `CairoDesktop.exe --export-theme MyTheme`. You get `MyTheme.zip` in the current folder.
2. The recipient runs `CairoDesktop.exe --install-theme MyTheme.zip`. They get the pack in their own packs folder,
   under a new name with `-2` appended if one with that name exists. The command refuses archives with `..` or
   absolute paths, more than 2000 files or more than 200 MB, and says when the pack defines hooks.

A pack that defines hooks, a `xaml` file or a `command` widget does nothing with them until the recipient reads `theme.json` and sets
`"allowPackHooks": true` in their `cairo-plus.json`.

To contribute a pack to the repository, copy the folder to
`Cairo Desktop/CairoDesktop.Customization/ThemePacks/` and run the tests; the bundled-pack test fails on any
validator error or warning.

## Field reference

| Field | Type | Notes |
|---|---|---|
| `name`, `author`, `description` | text | `name` is the label in the Theme Packs tab. |
| `baseTheme` | text | Cairo XAML theme underneath. Default `Default`. |
| `darkMode` | true / false | Sets Cairo's dark mode flag. |
| `palette` | object | The nine roles. |
| `resources` | object | Cairo resource key to value. Colours, numbers, booleans, thicknesses and font keys are typed automatically. |
| `light`, `dark` | object | Day and night version: `palette`, `resources`, `wallpaper`, `darkMode`. |
| `font` | object | `family`, `file`, `size`. |
| `wallpaper` | object | `file`, `style`. |
| `icons` | object | Icon key to image file. |
| `appIcons` | object | Exe name to image file. |
| `xaml` | text | Path to an extra ResourceDictionary merged last. |
| `menuBar`, `taskbar` | object | `layout`, `floating`, `margin`, `cornerRadius`, `spacing`, `opacity`, `blur`. |
| `animations` | object | `enabled`, `durationMs` (0 to 5000). |
| `widgets` | object | Widget id to options. |
| `hooks` | object | `onStart`, `onThemeApplied`, `onConfigReloaded`, each a list of `{ action, arg, args }`. Off unless the user allows pack hooks. |
