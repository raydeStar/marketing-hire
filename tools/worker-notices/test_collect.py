import unittest
from collect import Collector, Entry, Ext4, control_records, parse_entries, notice_filename


class Image(Ext4):
    def __init__(self, directories, links=None):
        self.directories = directories
        self.links = links or {}

    def link(self, entry):
        return self.links[entry.inode]


def directory(inode, name):
    return Entry(inode, 0o040755, name, 0)


def link(inode, name, target):
    return Entry(inode, 0o120777, name, len(target))


class MetadataTests(unittest.TestCase):
    def test_prefixed_license_names_are_candidates_not_similarly_named_code(self):
        for name in ["MIT-License.txt", "BSD-3-Clause.LICENSE", "apache-2.0-licence.md",
                     "LICENSE", "LICENSE-MIT", "COPYING.LIB", "THIRD_PARTY_NOTICES.txt"]:
            with self.subTest(name=name):
                self.assertTrue(notice_filename(name))
        for name in ["unlicensed.js", "get-license.js", "licenseChecker.ts", "README.md"]:
            with self.subTest(name=name):
                self.assertFalse(notice_filename(name))

    def test_prefixed_installed_notice_preserves_exact_bytes_and_skips_directories(self):
        raw = b"Copyright upstream\r\nPermission text\r\n"
        image = Image({2: {"pkg": directory(3, "pkg")}, 3: {
            "MIT-License.txt": Entry(4, 0o100644, "MIT-License.txt", len(raw)),
            "LICENSE": directory(5, "LICENSE"),
            "get-license.js": Entry(6, 0o100644, "get-license.js", 1)}})
        image.read = lambda path: raw if path == "/pkg/MIT-License.txt" else self.fail(path)
        collector = Collector(image, None)
        notices = collector.candidate_notices("/pkg")
        self.assertEqual(len(notices), 1)
        self.assertEqual(notices[0]["path"], "/pkg/MIT-License.txt")
        self.assertEqual(collector.contents[notices[0]["sha256"]], raw)
        self.assertEqual(collector.total, len(raw))

    def test_directory_protocol_and_unused_entries(self):
        entries = parse_entries(b'/2/040755/0/0/.//\n/2/040755/0/0/..//\n/9/100644/0/0/COPYING/24/\n/0/000000/0/0/deleted/0/\n')
        self.assertEqual(entries, {"COPYING": Entry(9, 0o100644, "COPYING", 24)})

    def test_duplicate_and_malformed_records_refused(self):
        for body in [b'/9/100644/0/0/a/1/\n/10/100644/0/0/a/1/\n',
                     b'bad output', b'/9/020644/0/0/device/0/\n']:
            with self.subTest(body=body), self.assertRaises(ValueError):
                parse_entries(body)

    def test_relative_and_absolute_links_remain_in_guest(self):
        image = Image({2: {"doc": directory(3, "doc"), "alias": link(6, "alias", "/doc/pkg")},
                       3: {"pkg": directory(4, "pkg"), "other": link(5, "other", "pkg")},
                       4: {"copyright": Entry(7, 0o100644, "copyright", 1)}}, {5: "pkg", 6: "/doc/pkg"})
        self.assertEqual(image.resolve("/doc/other/copyright").inode, 7)
        self.assertEqual(image.resolve("/alias/copyright").inode, 7)

    def test_link_loop_escape_and_missing_entries(self):
        image = Image({2: {"loop": link(3, "loop", "loop"), "escape": link(4, "escape", "../outside")}},
                      {3: "loop", 4: "../outside"})
        for path in ["/loop", "/escape", "/../outside"]:
            with self.subTest(path=path), self.assertRaises(ValueError):
                image.resolve(path)
        with self.assertRaises(FileNotFoundError):
            image.resolve("/absent")

    def test_control_fields_preserve_source_revision_and_continuations(self):
        records = control_records(b'Package: example\nVersion: 1:2.3-4\nSource: upstream (1:2.3-3)\nDescription: first\n second\n\nPackage: next\nVersion: 5\n')
        self.assertEqual(records[0]["Source"], "upstream (1:2.3-3)")
        self.assertEqual(records[0]["Description"], "first\nsecond")
        self.assertEqual(records[1]["Package"], "next")

    def test_duplicate_control_fields_and_orphan_continuations_refused(self):
        for body in [b'Package: one\nPackage: two\n', b' continuation\n', b'not a field\n']:
            with self.subTest(body=body), self.assertRaises(ValueError):
                control_records(body)

    def test_dangling_installed_module_link_is_an_explicit_gap(self):
        image = Image({2: {"mods": directory(3, "mods")},
                       3: {"broken": link(4, "broken", "/missing")}}, {4: "/missing"})
        collector = Collector(image, None)
        collector.npm_modules("/mods")
        self.assertEqual(collector.gaps, [{"component": "/mods/broken", "reason": "dangling installed module link"}])
        self.assertEqual(collector.components, [])

    def test_fast_inode_link_and_length_check(self):
        image = Ext4("unused")
        image.command = lambda *args: b'Inode: 5\nFast link dest: "../example"\n'
        self.assertEqual(image.link(link(5, "alias", "../example")), "../example")
        with self.assertRaises(ValueError):
            image.link(Entry(5, 0o120777, "alias", 1))


if __name__ == "__main__":
    unittest.main()
