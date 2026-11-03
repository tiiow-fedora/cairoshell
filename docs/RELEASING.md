# Releasing

Releases are built by [.github/workflows/release.yml](../.github/workflows/release.yml).

## What a release contains

For each of `x64`, `x86`, `arm64`, `x64_net480` and `x86_net480`:

- `CairoPlus-<version>-<arch>[-net480]-portable.zip`: unzip and run `CairoDesktop.exe`.
- `CairoPlus-<version>-<arch>[-net480]-setup.exe`: Inno Setup installer ([Installer/inno/CairoPlus.iss](../Installer/inno/CairoPlus.iss)).

Plus `SHA256SUMS.txt`, `update.json` (the update manifest) and `update.json.sig` (its signature).

The existing NSIS installers and the `build.yml` workflow are untouched. The Inno installer uses its own
install folder and uninstall entry, so it does not replace a stock Cairo installation.

## Cutting a release

1. Make sure CI is green on the commit you want to release.
2. Preview the notes: `python tools/changelog/generate_changelog.py --version X.Y.Z`.
3. Tag and push:

   ```bash
   git tag vX.Y.Z
   git push origin vX.Y.Z
   ```

   A version with a hyphen (`0.5.0-beta.1`) is published as a pre-release on the `beta` update channel.
4. The workflow builds all packages, writes `release-notes.md`, builds and signs `update.json`, and creates the
   GitHub release.

To rehearse without publishing, run the workflow manually (**Actions → Release → Run workflow**) and leave
"publish" unticked. Everything is uploaded as workflow artifacts instead.

## One-time setup

1. Generate a signing key (keep the PEM private):

   ```bash
   pip install cryptography
   python tools/packaging/update_manifest.py keygen --private-out update-signing-key.pem
   ```

2. Store the PEM contents as the repository secret `UPDATE_SIGNING_KEY`, then delete the local file or move it
   to a password manager.
3. Put the printed public key (`modulus:exponent`) wherever the app reads its trusted key; see
   [UPDATES.md](UPDATES.md).

Without the secret the release still publishes, but without `update.json.sig`, so in-app update checks reject it.

## Building locally (Windows)

```powershell
./tools/packaging/Build-Release.ps1 -Profile x64 -Version 0.5.0 -OutputDir artifacts
```

Add `-SkipInstaller` if Inno Setup is not installed, or `-SkipPublish` to repackage an existing publish folder.

## Changelog file

Release notes come from git history on demand. To also keep a `CHANGELOG.md` in the repository:

```bash
python tools/changelog/generate_changelog.py --version X.Y.Z --update-file CHANGELOG.md
```

## Not covered

- Code signing (Authenticode) of the executables and installers. SmartScreen will warn on unsigned installers.
- Publishing to winget: `winget.yml` still targets the upstream package identifier.
