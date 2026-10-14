#!/usr/bin/env python3
"""Check exported Unity package contents without extracting their large payloads."""
import collections
import hashlib
import json
import tarfile
from pathlib import Path, PurePosixPath

import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT = project_cfg.ROOT
EXPECTED_PREFIX = f"Assets/{project_cfg.RECOVERED_SUBDIR}"


def verify(path):
    entries = collections.defaultdict(set)
    paths = {}
    apple_metadata = 0
    with tarfile.open(path, 'r|gz') as archive:
        for member in archive:
            parts = PurePosixPath(member.name).parts
            if any(part.startswith('._') for part in parts):
                apple_metadata += 1
                continue
            if len(parts) != 2:
                if member.isdir():
                    continue
                raise ValueError(f'Unexpected package entry: {member.name}')
            key, kind = parts
            if kind in entries[key]:
                raise ValueError(f'Duplicate entry: {member.name}')
            entries[key].add(kind)
            if kind == 'pathname':
                name = archive.extractfile(member).read().decode('utf8').rstrip('\x00\r\n')
                assert name.startswith(EXPECTED_PREFIX), name
                assert '..' not in PurePosixPath(name).parts, name
                paths[key] = name
    counts = collections.Counter()
    for key, name in paths.items():
        assert 'asset.meta' in entries[key], name
        if 'asset' in entries[key]:
            counts[PurePosixPath(name).suffix] += 1
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    return {'file': path.name, 'bytes': path.stat().st_size, 'sha256': digest,
            'paths': len(paths), 'files_by_extension': dict(counts), 'apple_metadata_entries': apple_metadata}


def validate_counts(rows):
    unity = json.loads((ROOT/'reports/unity_3d_validation.json').read_text())
    media = json.loads((ROOT/'reports/unity_media_data_validation.json').read_text())
    assert not unity['failures'] and not media['failures']
    assert rows[0]['files_by_extension']['.anim'] == unity['animations']
    assert rows[0]['files_by_extension']['.prefab'] == unity['usablePrefabs']
    assert rows[0]['files_by_extension']['.unity'] == 12
    assert rows[1]['files_by_extension']['.wav'] == media['audio']
    assert rows[1]['files_by_extension']['.mp4'] == media['video']
    assert sum(rows[1]['files_by_extension'].get(ext,0) for ext in ('.json','.txt','.bytes')) == media['dataFiles']


def main():
    p = project_cfg.PROJECT_NAME
    rows = [verify(ROOT/name) for name in [f'{p}_3D.unitypackage', f'{p}_MediaData.unitypackage']]
    validate_counts(rows)
    (ROOT/'reports/package_verification.json').write_text(json.dumps(rows, indent=2)+'\n')
    print(json.dumps(rows, indent=2))


if __name__ == '__main__':
    main()
