import io
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

from inspect_archive import inspect_archive


class ArchiveInspectionTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='thaddeus-notice-')
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.archive = self.root / 'package.tgz'
        self.output = self.root / 'notice-candidates'

    def make_archive(self, entries):
        with tarfile.open(self.archive, 'w:gz') as archive:
            for name, data in entries:
                member = tarfile.TarInfo(name)
                if data is None:
                    member.type, member.linkname = tarfile.SYMTYPE, '/outside/secret'
                    archive.addfile(member)
                else:
                    member.size = len(data)
                    archive.addfile(member, io.BytesIO(data))

    def test_missing_registry_size_still_uses_a_hard_cap(self):
        self.make_archive([('package/package.json', b'{"name":"example","version":"1"}'),
                           ('package/README.md', b'Original notice'), ('package/code.js', b'never executed')])
        result = inspect_archive(self.archive, self.output)
        self.assertIsNone(result['registryUnpackedBytes'])
        self.assertEqual(result['unpackedLimitBytes'], 64 * 1024**2)
        self.assertEqual([f['kind'] for f in result['files']], ['metadata', 'readme'])
        self.assertEqual((self.output / result['files'][1]['file']).read_bytes(), b'Original notice')
        self.assertFalse((self.output / 'code.js').exists())

    def test_unselected_payload_counts_toward_limit(self):
        self.make_archive([('package/code.js', b'x' * 64)])
        with patch('inspect_archive.MAX_UNPACKED', 32):
            with self.assertRaisesRegex(ValueError, 'unpacked bound'):
                inspect_archive(self.archive, self.output)
        self.assertFalse((self.output / 'inventory.json').exists())

    def test_traversal_refused_without_creating_outside_file(self):
        self.make_archive([('package/../outside', b'unsafe')])
        with self.assertRaisesRegex(ValueError, 'archive path'):
            inspect_archive(self.archive, self.output)
        self.assertFalse((self.root / 'outside').exists())

    def test_duplicate_entry_refused(self):
        self.make_archive([('package/LICENSE', b'a'), ('package/LICENSE', b'b')])
        with self.assertRaisesRegex(ValueError, 'Duplicate'):
            inspect_archive(self.archive, self.output)

    def test_link_is_recorded_without_following_it(self):
        self.make_archive([('package/LICENSE', None)])
        result = inspect_archive(self.archive, self.output)
        self.assertEqual(result['files'][0]['kind'], 'non-regular-candidate')
        self.assertEqual(list(self.output.iterdir()), [self.output / 'inventory.json'])

    def test_existing_evidence_is_preserved(self):
        self.make_archive([('package/LICENSE', b'text')])
        self.output.mkdir()
        sentinel = self.output / 'prior'
        sentinel.write_bytes(b'preserved')
        with self.assertRaises(FileExistsError):
            inspect_archive(self.archive, self.output)
        self.assertEqual(sentinel.read_bytes(), b'preserved')

    def test_invalid_declared_size_refused_before_output(self):
        self.make_archive([])
        for size in (-1, 'undefined', True, 65 * 1024**2):
            with self.subTest(size=size), self.assertRaises(ValueError):
                inspect_archive(self.archive, self.output, size)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
