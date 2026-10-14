#!/usr/bin/env python3
"""Build a standalone Unity art library from AssetRipper's raw export."""
import collections
import argparse
import json
import re
import shutil
from pathlib import Path

import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT = project_cfg.ROOT
SOURCE = ROOT/'AssetRipperExport/ExportedProject/Assets'
DEST = project_cfg.RECOVERED_ASSETS_ROOT/'Recovered3D'
GUID = re.compile(r'guid: ([a-f0-9]{32})')
HEADER = re.compile(r'^--- !u!(\d+) &(-?\d+).*$', re.M)
# Geometry, skeleton, renderers, animation and colliders. Game scripts, effects,
# lights and cameras are removed from the standalone model prefabs.
KEEP = {1, 4, 23, 33, 65, 95, 111, 135, 136, 137, 224}
BUILTIN = {'0'*32, '0000000000000000e000000000000000', '0000000000000000f000000000000000'}


def clean_prefab(text, scene=False):
    matches = list(HEADER.finditer(text))
    removed = {m[2] for m in matches if (int(m[1]) == 114 if scene else int(m[1]) not in KEEP)}
    chunks = [text[:matches[0].start()]]
    for i, m in enumerate(matches):
        if m[2] in removed:
            continue
        chunk = text[m.start():matches[i+1].start() if i+1 < len(matches) else len(text)]
        chunk = re.sub(r'^  - component: \{fileID: (-?\d+)\}\n',
                       lambda m: '' if m[1] in removed else m[0], chunk, flags=re.M)
        chunk = re.sub(r'^  m_TagString:.*$', '  m_TagString: Untagged', chunk, flags=re.M)
        chunk = re.sub(r'^  m_Layer:.*$', '  m_Layer: 0', chunk, flags=re.M)
        chunks.append(chunk)
    return ''.join(chunks)


def main():
    global SOURCE
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=SOURCE)
    args = parser.parse_args()
    SOURCE = args.source
    paths = [p for p in SOURCE.rglob('*') if p.is_file() and p.suffix != '.meta']
    by_guid, metadata, text_cache = {}, {}, {}
    for p in paths:
        mp = Path(str(p)+'.meta')
        if not mp.exists():
            continue
        meta = mp.read_text()
        match = GUID.search(meta)
        if match:
            by_guid[match[1]] = p
            metadata[p] = meta
    def content(p):
        if p not in text_cache:
            t = p.read_text()
            if p.suffix in ('.prefab', '.unity'):
                t = clean_prefab(t, scene=p.suffix == '.unity')
            elif p.suffix == '.mat':
                t = re.sub(r'^  m_Shader:.*$', '  m_Shader: {fileID: 46, guid: 0000000000000000f000000000000000, type: 0}', t, flags=re.M)
                t = re.sub(r'  m_ValidKeywords:.*?(?=  m_LightmapFlags:)', '  m_ValidKeywords: []\n  m_InvalidKeywords: []\n', t, flags=re.S)
            text_cache[p] = t
        return text_cache[p]
    selected, pending = set(), []
    for p in metadata:
        if p.suffix in ('.anim', '.unity'):
            pending.append(p)
        elif p.suffix == '.asset':
            with p.open() as f:
                head = f.read(100)
            if re.search(r'--- !u!(43|90) ', head):
                pending.append(p)
        elif p.suffix == '.prefab' and not p.stem.lower().startswith(('fx', 'ui_', 'sactx')):
            t = content(p)
            if re.search(r'^--- !u!(23|137) ', t, re.M):
                pending.append(p)
    missing = []
    allowed = {'.asset', '.mat', '.png', '.tga', '.jpg', '.exr', '.anim', '.controller', '.overrideController', '.prefab', '.lighting', '.renderTexture'}
    while pending:
        p = pending.pop()
        if p in selected:
            continue
        selected.add(p)
        if p.suffix in ('.png', '.tga', '.jpg', '.exr'):
            continue
        for guid in set(GUID.findall(content(p))) - BUILTIN:
            dep = by_guid.get(guid)
            if dep is None:
                missing.append({'file':p.name, 'guid':guid})
            elif dep.suffix in allowed:
                pending.append(dep)
    rows, output_names = [], set()
    for p in sorted(selected):
        meta = metadata[p]
        bundle = re.search(r'^  assetBundleName: (.*)$', meta, re.M)
        bundle = bundle[1] if bundle else 'unassigned'
        if p.suffix == '.anim':
            folder = Path('Animations')/bundle
        elif p.suffix == '.unity':
            folder = Path('Scenes')
        elif p.suffix == '.prefab':
            folder = Path('Prefabs')/('Characters' if any(t in p.name for t in ('hero','people','npc','expert','monster','soldier')) else 'Environment')
        elif p.suffix == '.asset':
            head = content(p)[:100]
            folder = Path('Meshes' if '--- !u!43 ' in head else 'Avatars' if '--- !u!90 ' in head else 'SerializedData')
        else:
            folder = Path({'.mat':'Materials','.controller':'Controllers','.overrideController':'Controllers'}.get(p.suffix,'Textures'))
        target = DEST/folder/p.name
        if str(target).casefold() in output_names:
            guid = GUID.search(meta)[1]
            target = target.with_name(target.stem + '__' + guid[:12] + target.suffix)
        if str(target).casefold() in output_names:
            raise ValueError(f'Output collision: {target}')
        output_names.add(str(target).casefold())
        target.parent.mkdir(parents=True, exist_ok=True)
        if p.suffix in ('.png', '.tga', '.jpg', '.exr'):
            shutil.copyfile(p, target)
        else:
            target.write_text(content(p))
        # Do not retain Android bundle assignments in a reusable art library.
        meta = re.sub(r'^  assetBundleName:.*$', '  assetBundleName:', meta, flags=re.M)
        Path(str(target)+'.meta').write_text(meta)
        rows.append({'source':str(p.relative_to(SOURCE)),'path':str(target.relative_to(project_cfg.UNITY_ROOT)), 'bundle':bundle})
    report = {'counts':dict(collections.Counter(Path(r['path']).parts[3] for r in rows)),
              'missing_references':missing, 'assets':rows,
              'material_note':'Built-in Standard replaces proprietary mobile shaders; original shaders remain in AssetRipperExport.'}
    (ROOT/'reports/unity_3d_staging.json').write_text(json.dumps(report,indent=2)+'\n')
    print(report['counts']); print('Unresolved references:',len(missing))


if __name__ == '__main__':
    main()
