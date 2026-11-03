import json
import os
import tempfile
import unittest

from cryptography.hazmat.primitives.asymmetric import rsa

import update_manifest as um


class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.dir = self.tmp.name
        self.exe = os.path.join(self.dir, "CairoSetup_64bit.exe")
        with open(self.exe, "wb") as f:
            f.write(b"abc")

    def tearDown(self):
        self.tmp.cleanup()

    def asset(self, url="https://example.com/dl/CairoSetup_64bit.exe"):
        return um.parse_asset_spec(f"x64:installer:{url}::{self.exe}")

    def test_parse_asset_spec_keeps_url_colons(self):
        a = self.asset("https://example.com:8443/x.exe")
        self.assertEqual(a["url"], "https://example.com:8443/x.exe")
        self.assertEqual(a["path"], self.exe)

    def test_parse_asset_spec_rejects_bad(self):
        for bad in ["x64:installer:https://h/x", "mips:installer:https://h/x::f", "x64:iso:https://h/x::f"]:
            with self.assertRaises(ValueError):
                um.parse_asset_spec(bad)

    def test_build_manifest_hashes_and_sizes(self):
        doc = um.build_manifest("v0.5.0", [self.asset()], published="2026-01-01T00:00:00Z")
        self.assertEqual(doc["version"], "0.5.0")
        self.assertEqual(doc["assets"][0]["sha256"],
                         "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad")
        self.assertEqual(doc["assets"][0]["size"], 3)
        self.assertEqual(doc["schemaVersion"], 1)

    def test_build_manifest_rejects_http_and_bad_input(self):
        with self.assertRaises(ValueError):
            um.build_manifest("0.5.0", [self.asset("http://example.com/x.exe")])
        with self.assertRaises(ValueError):
            um.build_manifest("banana", [self.asset()])
        with self.assertRaises(ValueError):
            um.build_manifest("0.5.0", [])
        with self.assertRaises(ValueError):
            um.build_manifest("0.5.0", [self.asset()], channel="nightly")

    def test_four_part_nbgv_version_allowed(self):
        self.assertEqual(um.build_manifest("0.4.17.5", [self.asset()])["version"], "0.4.17.5")


class SigningTests(unittest.TestCase):
    def test_public_key_encoding_roundtrip_and_shape(self):
        key = rsa.generate_private_key(65537, 2048)
        enc = um.encoded_public_key(key.public_key())
        n_b64, e_b64 = enc.split(":")
        import base64
        self.assertEqual(len(base64.b64decode(n_b64)), 256)          # no leading zero byte: .NET wants raw big-endian
        self.assertEqual(base64.b64decode(e_b64), bytes([1, 0, 1]))
        data = b"manifest"
        sig = um.sign_bytes(key, data)
        self.assertTrue(um.verify_bytes(um.decode_public_key(enc), data, sig))
        self.assertFalse(um.verify_bytes(um.decode_public_key(enc), data + b"x", sig))

    def test_cli_end_to_end_and_tamper_detection(self):
        with tempfile.TemporaryDirectory() as d:
            exe = os.path.join(d, "CairoSetup_64bit.exe")
            open(exe, "wb").write(b"installer")
            keyfile = os.path.join(d, "key.pem")
            import io, contextlib
            buf = io.StringIO()
            with contextlib.redirect_stdout(buf):
                um.main(["keygen", "--private-out", keyfile])
            pub = buf.getvalue().strip().splitlines()[-1]
            manifest = os.path.join(d, "update.json")
            um.main(["manifest", "--version", "0.5.0", "--output", manifest,
                     "--asset", f"x64:installer:https://example.com/CairoSetup_64bit.exe::{exe}"])
            um.main(["sign", manifest, "--key", keyfile])
            self.assertEqual(um.main(["verify", manifest, "--public-key", pub, "--assets-dir", d]), 0)

            open(exe, "wb").write(b"tampered!")
            self.assertEqual(um.main(["verify", manifest, "--public-key", pub, "--assets-dir", d]), 1)

            open(exe, "wb").write(b"installer")
            doc = json.load(open(manifest))
            doc["version"] = "9.9.9"
            json.dump(doc, open(manifest, "w"))
            self.assertEqual(um.main(["verify", manifest, "--public-key", pub, "--assets-dir", d]), 1)

    def test_keygen_refuses_to_overwrite(self):
        with tempfile.TemporaryDirectory() as d:
            path = os.path.join(d, "k.pem")
            open(path, "w").close()
            with self.assertRaises(FileExistsError):
                um.main(["keygen", "--private-out", path])


if __name__ == "__main__":
    unittest.main()
