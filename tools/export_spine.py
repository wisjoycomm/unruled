#!/usr/bin/env python3
"""Recover Spine import files and atlas page images with original associations."""
import json
import re
import shutil
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
SOURCE=ROOT/'AllAssetsExport/ExportedProject/Assets'
GUID=re.compile(r'guid: ([a-f0-9]{32})')


def main():
    by_guid={}
    for p in SOURCE.rglob('*.meta'):
        m=GUID.search(p.read_text())
        if m:by_guid[m[1]]=Path(str(p)[:-5])
    rows=[]
    for p in sorted((SOURCE/'MonoBehaviour').glob('*.asset')):
        text=p.read_text()
        if '\n  skeletonJSON:' not in text or '\n  atlasAssets:' not in text:continue
        row={'source':str(p.relative_to(ROOT))}
        try:
            skeleton=by_guid[re.search(r'skeletonJSON:.*guid: ([a-f0-9]{32})',text)[1]]
            raw=skeleton.read_bytes()
            version=re.search(rb'[34]\.[0-9]+\.[0-9]+',raw[:200])
            target=ROOT/'ResourcesExport/Spine'/p.stem
            target.mkdir(parents=True,exist_ok=True)
            ext='.json' if raw.lstrip().startswith(b'{') else '.skel.bytes'
            (target/(p.stem.replace('_SkeletonData','')+ext)).write_bytes(raw)
            atlas_section=text.split('  atlasAssets:',1)[1].split('\n  scale:',1)[0]
            for atlas_guid in GUID.findall(atlas_section):
                atlas_asset=by_guid[atlas_guid];atlas_text=atlas_asset.read_text()
                atlas=by_guid[re.search(r'atlasFile:.*guid: ([a-f0-9]{32})',atlas_text)[1]]
                atlas_data=atlas.read_text(encoding='utf-8-sig')
                # Atlas page headers are followed by page metadata (not indented
                # region metadata). Preserve the atlas text exactly.
                lines=atlas_data.splitlines();pages=[]
                for i,line in enumerate(lines[:-1]):
                    if line and not line[0].isspace() and ':' not in line and (i==0 or not lines[i-1].strip()):
                        if any(lines[i+1].startswith(k) for k in ('size:','format:','filter:','pma:')):pages.append(line)
                materials=GUID.findall(atlas_text.split('  materials:',1)[1])
                if len(materials)!=len(pages):raise ValueError(f'Atlas pages {len(pages)} != materials {len(materials)}')
                for page,guid in zip(pages,materials):
                    if Path(page).name!=page or page in ('.','..'):raise ValueError('Unsafe atlas page path')
                    material=by_guid[guid].read_text()
                    texture_guid=re.search(r'_MainTex:\s+\n?\s*m_Texture:.*guid: ([a-f0-9]{32})',material)
                    if texture_guid is None:raise ValueError('Atlas material has no main texture')
                    texture=by_guid[texture_guid[1]]
                    shutil.copyfile(texture,target/page)
                (target/(atlas_asset.stem+'.atlas.txt')).write_text(atlas_data)
            row.update(status='exported',output=str(target.relative_to(ROOT)),spine_version=version[0].decode() if version else 'unknown')
        except Exception as exc:row.update(status='failed',error=str(exc))
        rows.append(row)
    (ROOT/'reports/spine_export.json').write_text(json.dumps(rows,indent=2)+'\n')
    print('Spine exports',sum(r['status']=='exported' for r in rows),'failed',sum(r['status']=='failed' for r in rows))


if __name__=='__main__':main()
