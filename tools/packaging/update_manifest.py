#!/usr/bin/env python3
"""Create and sign the update manifest consumed by CairoDesktop.Updates.

Subcommands:
  keygen    Create an RSA-3072 signing key. Writes the private key PEM and prints the public key
            in the "modulus:exponent" base64 form that ManifestSignatureVerifier.FromEncodedKey reads.
  manifest  Build update.json from release files (hashes and sizes are computed here).
  sign      Write <manifest>.sig: base64 RSA PKCS#1 v1.5 / SHA-256 signature over the exact file bytes.
  verify    Check a manifest, its signature and (optionally) the asset files. Used in CI before publishing.

The private key never goes in the repository. In CI it comes from the UPDATE_SIGNING_KEY secret.
Requires: pip install cryptography
"""
import argparse
import base64
import datetime
import hashlib
import json
import os
import re
import sys

from cryptography.exceptions import InvalidSignature
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import padding, rsa

SCHEMA_VERSION = 1
PLATFORMS = {"x64", "x86", "arm64", "x64-net480", "x86-net480"}
KINDS = {"installer", "portable"}
VERSION_RE = re.compile(r"^v?\d+\.\d+\.\d+(\.\d+)?(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$")


def b64_int(n):
    return base64.b64encode(n.to_bytes((n.bit_length() + 7) // 8, "big")).decode("ascii")


def encoded_public_key(public_key):
    nums = public_key.public_numbers()
    return f"{b64_int(nums.n)}:{b64_int(nums.e)}"


def decode_public_key(encoded):
    n_b64, e_b64 = encoded.strip().split(":")
    n = int.from_bytes(base64.b64decode(n_b64), "big")
    e = int.from_bytes(base64.b64decode(e_b64), "big")
    return rsa.RSAPublicNumbers(e, n).public_key()


def load_private_key(path_or_pem):
    data = path_or_pem.encode() if "BEGIN" in path_or_pem else open(path_or_pem, "rb").read()
    return serialization.load_pem_private_key(data, password=None)


def sha256_file(path):
    h = hashlib.sha256()
    with open(path, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def parse_asset_spec(spec):
    """'platform:kind:url:path' -> dict. The URL contains ':' so split from both ends."""
    platform, kind, rest = spec.split(":", 2)
    url, _, path = rest.rpartition("::")
    if not url or not path:
        raise ValueError(f"asset must be platform:kind:url::path, got {spec!r}")
    if platform not in PLATFORMS:
        raise ValueError(f"unknown platform {platform!r}; expected one of {sorted(PLATFORMS)}")
    if kind not in KINDS:
        raise ValueError(f"unknown kind {kind!r}; expected one of {sorted(KINDS)}")
    return {"platform": platform, "kind": kind, "url": url, "path": path}


def build_manifest(version, assets, channel="stable", minimum_version=None, notes_url=None, published=None):
    if not VERSION_RE.match(version):
        raise ValueError(f"not a version: {version!r}")
    if channel not in ("stable", "beta"):
        raise ValueError("channel must be stable or beta")
    if not assets:
        raise ValueError("at least one asset is required")
    doc = {
        "schemaVersion": SCHEMA_VERSION,
        "version": version.lstrip("v"),
        "channel": channel,
        "published": published or datetime.datetime.now(datetime.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ"),
    }
    if minimum_version:
        if not VERSION_RE.match(minimum_version):
            raise ValueError(f"not a version: {minimum_version!r}")
        doc["minimumVersion"] = minimum_version.lstrip("v")
    if notes_url:
        doc["notesUrl"] = notes_url
    out = []
    for a in assets:
        if not a["url"].startswith("https://"):
            raise ValueError(f"asset url must be https: {a['url']}")
        out.append({
            "platform": a["platform"], "kind": a["kind"], "url": a["url"],
            "sha256": sha256_file(a["path"]), "size": os.path.getsize(a["path"]),
        })
    doc["assets"] = out
    return doc


def sign_bytes(private_key, data):
    return private_key.sign(data, padding.PKCS1v15(), hashes.SHA256())


def verify_bytes(public_key, data, signature):
    try:
        public_key.verify(signature, data, padding.PKCS1v15(), hashes.SHA256())
        return True
    except InvalidSignature:
        return False


def cmd_keygen(a):
    key = rsa.generate_private_key(public_exponent=65537, key_size=3072)
    pem = key.private_bytes(serialization.Encoding.PEM, serialization.PrivateFormat.PKCS8, serialization.NoEncryption())
    fd = os.open(a.private_out, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "wb") as f:
        f.write(pem)
    print("Private key written to", a.private_out, "(keep it secret; store it as the UPDATE_SIGNING_KEY secret)")
    print("Public key (embed in the app):")
    print(encoded_public_key(key.public_key()))


def cmd_manifest(a):
    assets = [parse_asset_spec(s) for s in a.asset]
    doc = build_manifest(a.version, assets, a.channel, a.minimum_version, a.notes_url)
    with open(a.output, "w", encoding="utf-8", newline="\n") as f:
        json.dump(doc, f, indent=2)
        f.write("\n")
    print("Wrote", a.output)


def cmd_sign(a):
    key = load_private_key(os.environ["UPDATE_SIGNING_KEY"] if a.key_from_env else a.key)
    data = open(a.manifest, "rb").read()
    sig_path = a.manifest + ".sig"
    with open(sig_path, "wb") as f:
        f.write(base64.b64encode(sign_bytes(key, data)) + b"\n")
    print("Wrote", sig_path)


def cmd_verify(a):
    data = open(a.manifest, "rb").read()
    sig = base64.b64decode(open(a.manifest + ".sig", "rb").read().strip())
    if not verify_bytes(decode_public_key(a.public_key), data, sig):
        print("ERROR: signature does not match", file=sys.stderr)
        return 1
    doc = json.loads(data)
    for asset in doc["assets"]:
        name = asset["url"].rsplit("/", 1)[-1]
        path = os.path.join(a.assets_dir, name) if a.assets_dir else None
        if path and os.path.exists(path):
            if sha256_file(path) != asset["sha256"] or os.path.getsize(path) != asset["size"]:
                print(f"ERROR: {name} does not match the manifest", file=sys.stderr)
                return 1
    print("OK: signature valid for version", doc["version"])
    return 0


def main(argv=None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    k = sub.add_parser("keygen"); k.add_argument("--private-out", required=True); k.set_defaults(fn=cmd_keygen)

    m = sub.add_parser("manifest")
    m.add_argument("--version", required=True)
    m.add_argument("--channel", default="stable")
    m.add_argument("--minimum-version")
    m.add_argument("--notes-url")
    m.add_argument("--asset", action="append", default=[], help="platform:kind:https-url::local-file")
    m.add_argument("--output", default="update.json")
    m.set_defaults(fn=cmd_manifest)

    s = sub.add_parser("sign")
    s.add_argument("manifest")
    g = s.add_mutually_exclusive_group(required=True)
    g.add_argument("--key", help="path to private key PEM")
    g.add_argument("--key-from-env", action="store_true", help="read the PEM from $UPDATE_SIGNING_KEY")
    s.set_defaults(fn=cmd_sign)

    v = sub.add_parser("verify")
    v.add_argument("manifest")
    v.add_argument("--public-key", required=True, help="modulus:exponent")
    v.add_argument("--assets-dir")
    v.set_defaults(fn=cmd_verify)

    a = p.parse_args(argv)
    return a.fn(a) or 0


if __name__ == "__main__":
    sys.exit(main())
