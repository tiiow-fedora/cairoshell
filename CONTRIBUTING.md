# Contributing to Cairo Plus

Thanks for helping out. This page covers building, testing, commit style and how changes
reach a release. For what the fork adds, see [CAIRO-PLUS.md](CAIRO-PLUS.md); for theme packs,
[THEME-AUTHORING.md](THEME-AUTHORING.md).

## Set up

- Windows 10/11 for the WPF app. The .NET 10 SDK restores all three target frameworks
  (`net480`, `net6.0-windows`, `net10.0-windows`). With an older SDK, build one framework at a time;
  see [Building in CAIRO-PLUS.md](CAIRO-PLUS.md#building).
- Python 3.10+ (with `pip install cryptography`) for the release tooling. Works on any OS.
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) to build installers locally.

## Run the tests

| What | Command | Needs Windows |
| --- | --- | --- |
| Customization layer | `dotnet test "Cairo Desktop/Tests/CairoDesktop.Customization.Tests/CairoDesktop.Customization.Tests.csproj" -f net6.0-windows -p:TargetFrameworks=net6.0-windows -p:Platform=x64` | yes |
| Update check | `dotnet test "Cairo Desktop/Tests/CairoDesktop.Updates.Tests/CairoDesktop.Updates.Tests.csproj"` | no |
| Changelog generator | `cd tools/changelog && python -m unittest -v` | no |
| Manifest/signing tooling | `cd tools/packaging && python -m unittest -v test_update_manifest` | no |

CI ([.github/workflows/ci.yml](.github/workflows/ci.yml)) runs all of these on every push and pull request.
A pull request should leave CI green. If you could not run something locally (for example you are not on
Windows), say so in the description.

## Branches and pull requests

- Branch from the branch you are changing (`cairo-plus` for fork work; `master` tracks upstream Cairo).
- Keep a pull request to one topic. Prefer new files over edits to upstream Cairo files, so that
  [pulling upstream updates](CAIRO-PLUS.md#pulling-upstream-updates) stays easy. Where an upstream file must
  change, mark the hook with a `CAIRO-PLUS` comment.
- Add or update tests with the change. New behaviour that is off by default must stay off by default.

## Commit messages

The release notes are generated from commit subjects ([tools/changelog](tools/changelog/generate_changelog.py)),
so write subjects you would be happy to read in a changelog: short, imperative, no trailing period.

Either style works:

- Plain imperative: `Add Nord theme pack`, `Fix taskbar flicker on monitor change`. The first word picks the
  section (`Add` → Features, `Fix` → Fixes, `Document`/`Docs` → Documentation, `Test` → Tests).
- [Conventional Commits](https://www.conventionalcommits.org/): `feat(taskbar): add clock widget`,
  `fix: crash when config is empty`, `docs:`, `build:`, `test:`, `perf:`.

Mark a breaking change with `!` (`feat!: rename config key`) or a `BREAKING CHANGE:` line in the body.
Merge commits are skipped. See what a release would say:

```bash
python tools/changelog/generate_changelog.py --version 0.5.0
```

## Releases

Maintainers cut releases; see [docs/RELEASING.md](docs/RELEASING.md). Update checks and signing are described in
[docs/UPDATES.md](docs/UPDATES.md). Never commit signing keys.

## Reporting bugs

Use the issue templates. Include your Windows version, whether you use the installer or the portable zip, and the
contents of `%LOCALAPPDATA%\Cairo Desktop\` logs if relevant.
