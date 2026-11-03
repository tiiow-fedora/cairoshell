# Update check

`Cairo Desktop/CairoDesktop.Updates` is a small library that decides whether a newer release exists and
verifies it before anything is run. **The app does not call it yet**: it is built and tested, ready to be wired to a
menu item or startup check in a follow-up.

## Trust model

- The release workflow signs `update.json` with an RSA private key (PKCS#1 v1.5, SHA-256). The matching public key
  is compiled into the app. A compromised download host therefore cannot offer an update on its own.
- The manifest lists each installer's SHA-256 and size. After downloading, the file is checked against both,
  and is only moved into place if it matches.
- The signature is verified **before** the manifest is parsed. All URLs must be `https`. The library never
  downgrades, ignores pre-releases and the `beta` channel unless asked, and limits manifest and download sizes.
- Keys under 2048 bits are rejected.

Out of scope: the library downloads and verifies; it does not launch the installer. Whatever launches it
should also check Authenticode once releases are code-signed.

## Manifest format (`update.json`, schema 1)

```json
{
  "schemaVersion": 1,
  "version": "0.5.0",
  "channel": "stable",
  "published": "2026-11-03T12:00:00Z",
  "minimumVersion": "0.4.0",
  "notesUrl": "https://github.com/owner/repo/releases/tag/v0.5.0",
  "assets": [
    { "platform": "x64", "kind": "installer", "url": "https://.../CairoPlus-0.5.0-x64-setup.exe",
      "sha256": "<64 hex>", "size": 12345678 }
  ]
}
```

`platform` is `x64`, `x86`, `arm64`, `x64-net480` or `x86-net480`; `kind` is `installer` or `portable`.
`minimumVersion` is optional: older installs are told the update is required. `update.json.sig` holds the base64
signature over the exact bytes of `update.json`, so do not reformat the file after signing.

## Using it

```csharp
var verifier = ManifestSignatureVerifier.FromEncodedKey(EmbeddedPublicKey); // "modulus:exponent"
var checker  = new UpdateChecker(new HttpUpdateSource(), verifier);
var options  = new UpdateCheckOptions
{
    ManifestUri = new Uri("https://github.com/owner/repo/releases/latest/download/update.json"),
    CurrentVersion = SemanticVersion.Parse(currentVersionString),
    Platform = "x64",
};
var result = await checker.CheckAsync(options);
if (result.Status == UpdateCheckStatus.UpdateAvailable)
{
    string installerPath = await checker.DownloadAsync(result, options, tempDirectory);
}
```

Every failure surfaces as `UpdateException` with a message safe to show to a user. Treat it as "no update" in the
background and show it only for a user-initiated check.

## Tooling

[tools/packaging/update_manifest.py](../tools/packaging/update_manifest.py) has `keygen`, `manifest`, `sign` and
`verify` subcommands; CI uses the same script. To check a published release by hand:

```bash
python tools/packaging/update_manifest.py verify update.json --public-key "<modulus:exponent>" --assets-dir .
```

## Rotating the key

Ship an app version that trusts both keys (or the new one) before signing releases with the new key; old apps only
accept signatures from the key they were built with.
