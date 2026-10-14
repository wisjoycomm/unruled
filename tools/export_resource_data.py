#!/usr/bin/env python3
"""Export readable configuration, Lua bytecode and raw map data with provenance."""
import collections
import hashlib
import json
import re
import shutil
import subprocess
from pathlib import Path
import sys
import UnityPy
from config_json import dumps

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT=project_cfg.ROOT
OUT=project_cfg.RESOURCES_EXPORT_DIR


def safe(name):
    return re.sub(r'[^\w.\-@]+','_',name).strip('._') or 'unnamed'


def main():
    rows=[]
    dirs = {
        d.name
        for base in (project_cfg.RAW_GAME_ASSETS_DIR, project_cfg.DOWNLOADED_ASSETS_DIR)
        if base.exists()
        for d in base.iterdir()
        if d.is_dir() and d.name != 'data'
    }
    for kind in sorted(dirs):
        files={p.name:p for base in (project_cfg.RAW_GAME_ASSETS_DIR, project_cfg.DOWNLOADED_ASSETS_DIR) if (base/kind).exists() for p in (base/kind).glob('*') if p.is_file()}
        for p in files.values():
            folder=OUT/'Config'/('DeviceJSON' if kind=='config' else 'WorldBinary')
            if kind not in ('config','world'):folder=folder/kind
            target=folder/p.name
            if kind=='config':
                value=json.loads(p.read_bytes())
                target=target.with_suffix('.json')
                target.parent.mkdir(parents=True,exist_ok=True)
                target.write_text(dumps(value,indent=2,ensure_ascii=False)+'\n')
            else:
                target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(p,target)
            rows.append({'source':str(p.relative_to(ROOT)),'output':str(target.relative_to(ROOT)), 'status':'exported'})
    paths=list(project_cfg.DECRYPTED_DIR.glob('config_*.unity3d'))
    paths+=list(project_cfg.DECRYPTED_DIR.glob('luapackage_*.unity3d'))
    for path in sorted(paths):
        env=UnityPy.load(str(path))
        arch='64' if path.name.startswith('luapackage_64') else '32'
        for obj in env.objects:
            row={'source':path.name,'path_id':obj.path_id,'type':obj.type.name}
            if obj.type.name=='TextAsset' and path.name.startswith('luapackage'):
                data=obj.read();payload=data.m_Script.encode('utf8','surrogateescape')
                target=OUT/'Lua'/arch/(safe(data.m_Name)+'.bytes')
                target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(payload)
                row.update(name=data.m_Name,status='exported',output=str(target.relative_to(ROOT)))
                rows.append(row)
            elif obj.type.name=='MonoBehaviour':
                try:
                    data=obj.read_typetree()
                    if data.get('m_GameObject',{}).get('m_PathID',0):continue
                    target=OUT/'Config'/'ScriptableJSON'/(safe(data.get('m_Name','config'))+'__'+str(obj.path_id)+'.json')
                    target.parent.mkdir(parents=True,exist_ok=True)
                    target.write_text(dumps(data,indent=2,ensure_ascii=False)+'\n')
                    row.update(status='exported',output=str(target.relative_to(ROOT)))
                except Exception as exc:row.update(status='failed',error=str(exc))
                rows.append(row)
    lua=OUT/'Lua/64'
    names=[p.stem for p in lua.glob('game_config_*.bytes') if p.stem!='game_config_ConfigMeta']
    filelist=ROOT/'reports/lua_config_modules.txt';filelist.write_text('\n'.join(sorted(names))+'\n')
    destination=OUT/'Config/LuaJSON';destination.mkdir(parents=True,exist_ok=True)
    subprocess.run([str(ROOT/'tools/LuaJIT-2.1/src/luajit'),str(ROOT/'tools/export_config_json.lua'),
                    str(lua),str(filelist),str(destination),str(ROOT/'reports/lua_config_export.json')],check=True)
    lua_rows=json.loads((ROOT/'reports/lua_config_export.json').read_text())
    for row in lua_rows:
        if row['status']=='exported':json.loads((destination/(row['name']+'.json')).read_text())
    report={'exports':rows,'lua_configs':dict(collections.Counter(r['status'] for r in lua_rows)),
            'lua_bytecode':len(list((OUT/'Lua').rglob('*.bytes'))),'scriptable_json':len(list((OUT/'Config/ScriptableJSON').glob('*.json')))}
    (ROOT/'reports/resource_data_export.json').write_text(json.dumps(report,indent=2)+'\n')
    print({k:v for k,v in report.items() if k!='exports'})


if __name__=='__main__':main()
