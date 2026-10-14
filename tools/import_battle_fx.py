#!/usr/bin/env python3
"""Copy Exploration battle VFX prefabs (hero attack / projectile / hit / XP skill, soldier arrows and spears)
from AssetRipper's export into the Recovered3D art library, with every dependency and original GUIDs.

Assets already in the project (same GUID) are reused, not copied. Game scripts are stripped from the prefabs.
Material shaders are left as exported (stubs, missing in the project); the Unity side remaps them with
EndlessWinter/Exploration/Import Battle VFX (ExploreBattleFxImporter).
"""
import argparse
import collections
import json
import re
import shutil
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'AllAssetsExport/ExportedProject/Assets'
PROJECT = ROOT / 'UnityAssets/Assets'
DEST = PROJECT / 'WhiteoutSurvival/Recovered3D'
GUID = re.compile(r'guid: ([a-f0-9]{32})')
HEADER = re.compile(r'^--- !u!(\d+) &(-?\d+).*$', re.M)
BUILTIN = {'0' * 32, '0000000000000000e000000000000000', '0000000000000000f000000000000000'}
# GameObject, Transform, MeshRenderer, MeshFilter, TrailRenderer, Animation, ParticleSystem(+Renderer),
# LineRenderer, SkinnedMeshRenderer, Animator.
KEEP = {1, 4, 23, 33, 95, 96, 111, 120, 137, 198, 199}
# Hero battle models deployable in Exploration (HeroDefinition.battleModel) and the escort soldiers.
HEROES = ['beastmaster', 'builder', 'blaster', 'chef', 'cop', 'explorer', 'jasser', 'hunter', 'ling_shuang',
          'lumberjack', 'machinist', 'molly', 'nicolas', 'ranger', 'seo_yoon', 'smith', 'walis']
SOLDIERS = ['fx_soldier_archer_attack', 'fx_soilder_pikeman_attack_dandao']
FOLDERS = {'.mat': 'Materials', '.png': 'Textures', '.tga': 'Textures', '.anim': 'Animations/unassigned',
           '.prefab': 'Prefabs/Fx', '.controller': 'Controllers'}
COPY = {'.mat', '.png', '.tga', '.anim', '.asset', '.prefab', '.controller'}


def clean_prefab(text):
    matches = list(HEADER.finditer(text))
    removed = {m[2] for m in matches if int(m[1]) not in KEEP}
    chunks = [text[:matches[0].start()]]
    for i, m in enumerate(matches):
        if m[2] in removed:
            continue
        chunk = text[m.start():matches[i + 1].start() if i + 1 < len(matches) else len(text)]
        chunk = re.sub(r'^  - component: \{fileID: (-?\d+)\}\n',
                       lambda c: '' if c[1] in removed else c[0], chunk, flags=re.M)
        chunk = re.sub(r'^  m_TagString:.*$', '  m_TagString: Untagged', chunk, flags=re.M)
        chunk = re.sub(r'^  m_Layer:.*$', '  m_Layer: 0', chunk, flags=re.M)
        chunks.append(chunk)
    return ''.join(chunks)


def index_guids(root, folders):
    """guid -> asset path, via ripgrep over the .meta files (the export holds ~80k assets)."""
    out = subprocess.run(['rg', '--no-heading', '-o', '-m', '1', '--glob', '*.meta', r'^guid: [0-9a-f]{32}', *folders],
                         cwd=root, capture_output=True, text=True, check=True).stdout
    result = {}
    for line in out.splitlines():
        path, _, guid = line.rpartition(':guid: ')
        result[guid.strip()] = root / path[:-5]
    return result


def select_prefabs():
    names = []
    for prefab in sorted((SOURCE / 'GameObject').glob('fx_hero_*.prefab')):
        stem = prefab.stem
        if '_lod' in stem:
            continue
        if any(stem.startswith('fx_hero_' + h + '_') for h in HEROES):
            names.append(prefab)
    names += [SOURCE / 'GameObject' / (n + '.prefab') for n in SOLDIERS]
    return names


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()

    export = index_guids(SOURCE, ['Material', 'Mesh', 'Texture2D', 'Shader', 'AnimationClip', 'AnimatorController'])
    project = index_guids(PROJECT, ['.'])
    pending = select_prefabs()
    selected, missing, shaders = {}, [], collections.Counter()
    while pending:
        path = pending.pop()
        if path in selected:
            continue
        text = path.read_text(encoding='utf-8', errors='ignore')
        if path.suffix == '.prefab':
            text = clean_prefab(text)
        selected[path] = text
        if path.suffix in ('.png', '.tga'):
            continue
        for guid in set(GUID.findall(text)) - BUILTIN:
            if guid in project:
                continue
            dep = export.get(guid)
            if dep is None:
                missing.append({'file': path.name, 'guid': guid})
            elif dep.suffix == '.shader':
                shaders[dep.stem] += 1
            elif dep.suffix in COPY:
                pending.append(dep)

    rows = []
    for path, text in sorted(selected.items()):
        if path.suffix == '.asset':
            head = text[:120]
            folder = 'Meshes' if '--- !u!43 ' in head else 'SerializedData'
        else:
            folder = FOLDERS[path.suffix]
        target = DEST / folder / path.name
        if target.exists():
            target = target.with_name(target.stem + '__' + GUID.search(path.with_suffix(path.suffix + '.meta').read_text())[1][:12] + target.suffix)
        if target.exists():
            raise ValueError(f'Output collision: {target}')
        rows.append({'source': str(path.relative_to(SOURCE)), 'path': str(target.relative_to(ROOT / 'UnityAssets'))})
        if args.dry_run:
            continue
        target.parent.mkdir(parents=True, exist_ok=True)
        if path.suffix in ('.png', '.tga'):
            shutil.copyfile(path, target)
        else:
            target.write_text(text, encoding='utf-8')
        meta = Path(str(path) + '.meta').read_text(encoding='utf-8', errors='ignore')
        meta = re.sub(r'^  assetBundleName:.*$', '  assetBundleName:', meta, flags=re.M)
        Path(str(target) + '.meta').write_text(meta, encoding='utf-8')

    counts = collections.Counter(Path(r['path']).parts[3] for r in rows)
    report = {'counts': dict(counts), 'stub_shaders': dict(shaders), 'missing_references': missing, 'assets': rows}
    if not args.dry_run:
        (ROOT / 'reports/battle_fx_import.json').write_text(json.dumps(report, indent=2) + '\n')
    print('prefabs', sum(1 for r in rows if r['path'].endswith('.prefab')), dict(counts))
    print('stub shaders', dict(shaders))
    print('unresolved', len(missing))


if __name__ == '__main__':
    main()
