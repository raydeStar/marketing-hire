import copy
import hashlib
import json
import os
import pathlib
import subprocess
import tempfile
import unittest
from unittest.mock import patch

from assemble import assemble, prepare, write_bundle


class AssemblyTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="thaddeus-notice-")
        self.addCleanup(self.temporary.cleanup)
        self.root = pathlib.Path(self.temporary.name)
        self.guest = self.root / "guest"
        self.catalog_root = self.root / "catalog"
        for directory in (self.guest, self.catalog_root):
            (directory / "texts").mkdir(parents=True)
        self.inventory_path = self.guest / "inventory.json"
        self.catalog_path = self.catalog_root / "catalog.json"
        self.output = self.root / "bundle"
        metadata = self.text(self.guest, "/app/package.json",
                             b'{"name":"example","version":"1.0","license":"MIT"}')
        installed = self.text(self.guest, "/usr/share/doc/other/copyright", b"Copyright other\r\n")
        common = self.text(self.guest, "/usr/share/common-licenses/MIT", b"Full license\r\n")
        status = self.text(self.guest, "/var/lib/dpkg/status", b"Package: other\nVersion: 2.0\n")
        os_release = self.text(self.guest, "/etc/os-release", b"ID=example\n")
        lock = self.text(self.guest, "/app/pnpm-lock.yaml", b"lockfileVersion: 9\n")
        self.inventory = {
            "schemaVersion": 1, "inspectionPassed": True, "redistributionComplete": False,
            "diskSha256": "a" * 64, "scope": "fixture", "limitations": ["Incomplete fixture scope"],
            "status": status, "osRelease": os_release, "openclawLock": lock,
            "commonLicenses": [common],
            "components": [
                {"ecosystem": "npm", "name": "example", "version": "1.0", "path": "/app",
                 "declaredLicense": "MIT", "metadata": metadata, "notices": []},
                {"ecosystem": "dpkg", "name": "other", "version": "2.0", "architecture": "all",
                 "sourceName": "source-other", "sourceVersion": "2.0", "notices": [installed]}],
            "gaps": [{"component": "example@1.0", "path": "/app", "reason": "no root notice"},
                     {"component": "/app/missing", "reason": "dangling installed module link"}],
        }
        upstream = self.text(self.catalog_root, "LICENSE", b"Original copyright\r\n")
        same_common = self.text(self.catalog_root, "MIT", b"Full license\r\n")
        self.catalog = {
            "formatVersion": 1, "redistributionComplete": False, "guestDiskSha256": "a" * 64,
            "bindings": [{"identity": "example@1.0", "path": "/app", "metadataSha256": metadata["sha256"],
                          "declaredLicense": "MIT", "basis": "Fixture declared revision",
                          "source": {"repository": "https://example.invalid/repo", "commit": "b" * 40},
                          "notices": [upstream, same_common], "additionalReview": "Source coverage remains"}],
        }
        self.save()

    def text(self, root, upstream_path, data):
        sha = hashlib.sha256(data).hexdigest()
        filename = "texts/" + sha + ".txt"
        (root / filename).write_bytes(data)
        return {"path": upstream_path, "file": filename, "sha256": sha, "bytes": len(data)}

    def save(self):
        texts = list((self.guest / "texts").iterdir())
        self.inventory["textFiles"] = len(texts)
        self.inventory["capturedBytes"] = sum(p.stat().st_size for p in texts)
        data = json.dumps(self.inventory).encode()
        self.inventory_path.write_bytes(data)
        self.catalog["inventorySha256"] = hashlib.sha256(data).hexdigest()
        self.catalog_path.write_text(json.dumps(self.catalog), encoding="utf-8")

    def run_assembly(self):
        return assemble(self.inventory_path, self.catalog_path, self.output)

    def test_self_contained_exact_bytes_and_all_findings_retained(self):
        result = self.run_assembly()
        self.assertFalse(result["redistributionComplete"])
        self.assertEqual(result["summary"], {"components": 2, "supplementedComponents": 1,
                                          "originalFindings": 2, "findingsWithoutSupplement": 1,
                                          "textFiles": 3, "textBytes": 51})
        self.assertEqual([f["supplementProvided"] for f in result["inventoryFindings"]], [True, False])
        for ref in (result["commonLicenses"] + result["components"][1]["notices"]
                    + result["components"][0]["supplement"]["notices"]):
            data = (self.output / ref["file"]).read_bytes()
            self.assertEqual(hashlib.sha256(data).hexdigest(), ref["sha256"])
            self.assertIn(b"\r\n", data)
        metadata_file = self.inventory["components"][0]["metadata"]["file"]
        self.assertFalse((self.output / metadata_file).exists())
        self.assertNotIn("metadata", result["components"][0])
        self.assertIn("metadataSha256", result["components"][0])
        self.assertEqual(json.loads((self.output / "bundle.json").read_bytes()), result)
        self.assertIn("not a completed redistribution", (self.output / "THIRD-PARTY-NOTICES.md").read_text())

    def test_inventory_and_disk_pin_mismatch_leave_no_output(self):
        for field in ("inventorySha256", "guestDiskSha256"):
            with self.subTest(field=field):
                catalog = copy.deepcopy(self.catalog)
                catalog[field] = "0" * 64
                self.catalog_path.write_text(json.dumps(catalog), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "pin differs"):
                    self.run_assembly()
                self.assertFalse(self.output.exists())

    def test_installed_metadata_identity_and_legacy_license(self):
        original = self.inventory["components"][0]["metadata"]
        (self.guest / original["file"]).unlink()
        legacy = [{"type": "MIT"}]
        self.inventory["components"][0]["metadata"] = self.text(
            self.guest, "/app/package.json", json.dumps({"name": "example", "version": "1.0", "licenses": legacy}).encode())
        self.inventory["components"][0]["declaredLicense"] = legacy
        binding = self.catalog["bindings"][0]
        binding["metadataSha256"] = self.inventory["components"][0]["metadata"]["sha256"]
        binding["declaredLicense"] = legacy
        self.save()
        prepare(self.inventory_path, self.catalog_path)
        self.inventory["components"][0]["version"] = "2.0"
        self.save()
        with self.assertRaisesRegex(ValueError, "metadata identity/license"):
            self.run_assembly()
        self.assertFalse(self.output.exists())

    def test_binding_must_match_identity_location_hash_and_license(self):
        for field, value in (("identity", "unknown@1"), ("path", "/elsewhere"),
                             ("metadataSha256", "0" * 64), ("declaredLicense", "ISC")):
            with self.subTest(field=field):
                catalog = copy.deepcopy(self.catalog)
                catalog["bindings"][0][field] = value
                self.catalog_path.write_text(json.dumps(catalog), encoding="utf-8")
                with self.assertRaises(ValueError):
                    self.run_assembly()
                self.assertFalse(self.output.exists())

    def test_duplicate_bindings_refused(self):
        self.catalog["bindings"] *= 2
        self.save()
        with self.assertRaisesRegex(ValueError, "Duplicate or unknown"):
            self.run_assembly()

    def test_corrupt_installed_and_supplement_text_refused(self):
        for root, ref in ((self.guest, self.inventory["components"][1]["notices"][0]),
                          (self.catalog_root, self.catalog["bindings"][0]["notices"][0])):
            with self.subTest(root=root):
                file = root / ref["file"]
                original = file.read_bytes()
                file.write_bytes(b"modified")
                with self.assertRaisesRegex(ValueError, "Text bytes differ"):
                    self.run_assembly()
                self.assertFalse(self.output.exists())
                file.write_bytes(original)

    def test_traversal_absolute_and_alternate_stream_paths_refused(self):
        for filename in ("../outside.txt", "C:/outside.txt", "texts/good.txt:secret", "/tmp/outside"):
            with self.subTest(filename=filename):
                catalog = copy.deepcopy(self.catalog)
                catalog["bindings"][0]["notices"][0]["file"] = filename
                self.catalog_path.write_text(json.dumps(catalog), encoding="utf-8")
                with self.assertRaisesRegex(ValueError, "fixed content address"):
                    self.run_assembly()
                self.assertFalse(self.output.exists())

    def test_linked_evidence_directory_refused(self):
        link = self.root / "linked"
        try:
            link.symlink_to(self.catalog_root, target_is_directory=True)
        except OSError as error:
            if os.name != "nt":
                raise
            # Junctions exercise the actual Windows reparse boundary without UAC.
            environment = dict(os.environ, THADDEUS_NOTICE_TEST_LINK=str(link),
                               THADDEUS_NOTICE_TEST_TARGET=str(self.catalog_root))
            result = subprocess.run([
                "powershell", "-NoProfile", "-NonInteractive", "-Command",
                "New-Item -ItemType Junction -Path $env:THADDEUS_NOTICE_TEST_LINK "
                "-Target $env:THADDEUS_NOTICE_TEST_TARGET -ErrorAction Stop | Out-Null"],
                env=environment, capture_output=True, timeout=15,
                creationflags=subprocess.CREATE_NO_WINDOW)
            self.assertEqual(result.returncode, 0, result.stderr.decode(errors="replace"))
            self.addCleanup(link.rmdir)
        with self.assertRaisesRegex(ValueError, "Linked/reparse"):
            assemble(self.inventory_path, link / "catalog.json", self.output)
        self.assertFalse(self.output.exists())

    def test_existing_output_preserved(self):
        self.output.mkdir()
        sentinel = self.output / "keep.txt"
        sentinel.write_bytes(b"prior evidence")
        with self.assertRaisesRegex(ValueError, "fresh directory"):
            self.run_assembly()
        self.assertEqual(sentinel.read_bytes(), b"prior evidence")
        self.assertEqual(list(self.output.iterdir()), [sentinel])

    def test_failed_write_removes_only_new_output(self):
        neighbor = self.root / "keep.txt"
        neighbor.write_bytes(b"user input")
        bundle, files = prepare(self.inventory_path, self.catalog_path)
        original_open = pathlib.Path.open

        def fail_open(path, mode="r", *args, **kwargs):
            if path.name == "bundle.json" and mode == "xb":
                raise OSError("Simulated disk write failure")
            return original_open(path, mode, *args, **kwargs)

        with patch.object(pathlib.Path, "open", fail_open):
            with self.assertRaisesRegex(OSError, "Simulated disk write failure"):
                write_bundle(self.output, files)
        self.assertFalse(self.output.exists())
        self.assertEqual(neighbor.read_bytes(), b"user input")
        self.assertTrue(self.inventory_path.exists())
        self.assertTrue(self.catalog_path.exists())

    def test_storage_reserve_admission_before_output(self):
        _, files = prepare(self.inventory_path, self.catalog_path)
        with patch("assemble.shutil.disk_usage") as usage:
            usage.return_value.free = 10 * 1024 ** 3
            with self.assertRaisesRegex(ValueError, "10 GiB"):
                write_bundle(self.output, files)
        self.assertFalse(self.output.exists())

    def test_completed_claims_and_duplicate_json_fields_refused(self):
        self.inventory["redistributionComplete"] = True
        self.save()
        with self.assertRaisesRegex(ValueError, "explicitly incomplete"):
            self.run_assembly()
        self.inventory_path.write_bytes(b'{"schemaVersion": 1, "schemaVersion": 2}')
        with self.assertRaisesRegex(ValueError, "Duplicate JSON field"):
            self.run_assembly()


if __name__ == "__main__":
    unittest.main()
