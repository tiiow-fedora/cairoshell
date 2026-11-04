# Wallpaper slideshow

Changes the desktop wallpaper every few minutes from a folder of pictures, in order or shuffled. An optional bar
button (`widget:wallpaper`) skips to the next picture on demand.

## Configuration

Off unless `wallpaperSlideshow.enabled` is true and a folder is set:

```jsonc
"wallpaperSlideshow": {
  "enabled": true,
  "folder": "%USERPROFILE%\\Pictures\\Wallpapers",   // required; %VARIABLES% are expanded
  "intervalMinutes": 30,         // 1..1440
  "shuffle": true,               // false: name order, wrapping round
  "includeSubfolders": false,
  "style": "fill",               // fill, fit, stretch, center, tile, span
  "changeOnStart": false         // true: change as soon as Cairo starts instead of finishing the current interval
},
"widgets": { "nextwall": { "type": "wallpaper", "label": "Next wallpaper" } }
```

A missing folder switches the slideshow off with an error in the log; bad values fall back to the defaults.

## Behaviour

- Pictures are `.jpg`, `.jpeg`, `.png` and `.bmp` (what Windows accepts as a desktop picture). Hidden files, `~` temp files
  and other types are ignored.
- **Shuffle** shows every picture once per round in random order, and a new round never opens with the picture that ended the
  previous one. Pictures added or deleted while it runs are picked up on the next change.
- **In order** walks the file names (case-insensitive) and wraps. If the current picture is deleted it carries on from where it was.
- The remaining time survives a Cairo restart: the last change time is kept in `%LOCALAPPDATA%\Cairo Desktop\wallpaper-slideshow.json`.
- The folder is listed on a background thread, so a slow network share does not freeze the shell.
- Wallpapers are applied by the same code as theme packs. On top of Explorer that sets the **Windows wallpaper permanently**
  (the pack wallpaper behaves the same way). A theme pack with its own wallpaper replaces the current picture when you apply
  it, and the slideshow carries on at its next change.

## Code

- Logic: `CairoDesktop.Features/WallpaperSlideshow` (`SlideshowPlanner`, `ImageFilter`, `SlideshowConfigParser`). Tests:
  `Tests/CairoDesktop.Features.Tests/WallpaperSlideshow`.
- Adapter: `CairoDesktop.Customization/Features/WallpaperSlideshow/SlideshowController.cs` (`#if CAIRO_FEATURES`, **not compiled**).
  Starting it from `CairoPlusService` is done in the integration pull request.
