"""Read bounded notice candidates from an npm archive without extracting its paths."""
import hashlib
import json
from pathlib import Path
import re
import sys
import tarfile

MAX_UNPACKED = 64 * 1024**2
MAX_CAPTURED = 32 * 1024**2
MAX_FILE = 4_000_000


def inspect_archive(archive, destination, unpacked_size=None):
    if unpacked_size is not None and (type(unpacked_size) is not int or not 0 <= unpacked_size <= MAX_UNPACKED):
        raise ValueError('Invalid or excessive registry unpacked size')
    limit = MAX_UNPACKED if unpacked_size is None else min(MAX_UNPACKED, unpacked_size + 2_000_000)
    root = Path(destination)
    root.mkdir()  # Never overwrite a prior inspection, even a failed one.
    names, files, total, captured = set(), [], 0, 0
    with tarfile.open(archive, 'r|gz') as package:
        for member in package:
            parts = member.name.rstrip('/').split('/')
            if ('\\' in member.name or not parts or parts[0] != 'package'
                    or any(part in ('', '.', '..') for part in parts)):
                raise ValueError('Unsupported archive path')
            canonical = '/'.join(parts)
            if canonical in names or len(names) >= 50_000:
                raise ValueError('Duplicate path or entry limit')
            names.add(canonical)
            if member.size < 0:
                raise ValueError('Negative member size')
            total += member.size
            if total > limit:
                raise ValueError('Archive exceeds explicit unpacked bound')
            name = parts[-1]
            notice = bool(re.match(r'^(licen[cs]e|copying|copyright|notice|third[-_]party[-_]notices?)([._-]|$)', name, re.I))
            metadata = name == 'package.json'
            readme = len(parts) == 2 and bool(re.match(r'^readme([._-]|$)', name, re.I))
            if not (notice or metadata or readme):
                continue
            if not member.isfile():
                files.append(dict(path=member.name, kind='non-regular-candidate', type=repr(member.type)))
                continue
            if member.size > MAX_FILE or captured + member.size > MAX_CAPTURED:
                raise ValueError('Candidate notice/metadata byte limit')
            with package.extractfile(member) as stream:
                data = stream.read(MAX_FILE + 1)
            if len(data) != member.size:
                raise ValueError('Archive member size mismatch')
            digest = hashlib.sha256(data).hexdigest()
            output = root / (digest + '.txt')
            if output.exists():
                if output.read_bytes() != data:
                    raise ValueError('Content-address collision')
            else:
                with output.open('xb') as target:
                    target.write(data)
            captured += len(data)
            files.append(dict(path=member.name, kind='notice' if notice else 'metadata' if metadata else 'readme',
                              bytes=len(data), sha256=digest, file=digest + '.txt'))
    result = dict(entries=len(names), unpackedBytes=total, capturedBytes=captured,
                  registryUnpackedBytes=unpacked_size, unpackedLimitBytes=limit, files=files)
    (root / 'inventory.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    archive, destination, *size = sys.argv[1:]
    if len(size) > 1:
        raise ValueError('Usage: inspect_archive.py ARCHIVE NEW_DIRECTORY [REGISTRY_UNPACKED_BYTES]')
    inspect_archive(archive, destination, int(size[0]) if size else None)
    print('Original notice candidates collected; the raven leaves upstream prose untouched.')
