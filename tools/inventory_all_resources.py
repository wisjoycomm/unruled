#!/usr/bin/env python3
"""Inventory every recovered bundle and export all scriptable configuration."""
import collections
import json
import re
import shutil
from pathlib import Path
import sys
import UnityPy
from config_json import dumps

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT=project_cfg.ROOT


def main():
    rows=[]; totals=collections.Counter(); config_count=0; failures=[]
    out=project_cfg.RESOURCES_CONFIG_DIR/'AllScriptables'
    out.mkdir(parents=True,exist_ok=True)
    paths=sorted(project_cfg.DECRYPTED_DIR.glob('*.unity3d'))
    for index,path in enumerate(paths):
        try:
            env=UnityPy.load(str(path)); counts=collections.Counter()
            for obj in env.objects:
                counts[obj.type.name]+=1
                if obj.type.name=='MonoBehaviour':
                    raw=obj.get_raw_data()
                    if raw[:12]!=bytes(12):continue
                    try:
                        data=obj.read_typetree()
                        name=re.sub(r'[^\w.-]+','_',data.get('m_Name','unnamed'))[:120] or 'unnamed'
                        target=out/path.stem/(name+'__'+str(obj.path_id)+'.json')
                        target.parent.mkdir(parents=True,exist_ok=True)
                        target.write_text(dumps(data,ensure_ascii=False,indent=2)+'\n')
                        config_count+=1
                    except Exception as exc:
                        failures.append({'bundle':path.name,'path_id':obj.path_id,'error':str(exc)})
                elif obj.type.name=='TextAsset' and path.name.startswith('config_language_'):
                    d=obj.read();name=re.sub(r'[^\w.-]+','_',d.m_Name)
                    target=project_cfg.RESOURCES_CONFIG_DIR/'Localization'/name
                    target.parent.mkdir(parents=True,exist_ok=True)
                    target.with_suffix('.txt').write_text(d.m_Script,encoding='utf8')
                    values={};last=None
                    for line in d.m_Script.lstrip('\ufeff').splitlines():
                        match=re.match(r'^([^=\s]+)=(.*)$',line)
                        if match:last=match[1];values[last]=match[2]
                        elif last is not None:values[last]+='\n'+line
                    target.with_suffix('.json').write_text(json.dumps(values,ensure_ascii=False,indent=2)+'\n')
            totals.update(counts);rows.append({'file':path.name,'counts':dict(counts)})
        except Exception as exc:
            failures.append({'bundle':path.name,'error':str(exc)})
        if index%500==0:print(f'{index+1}/{len(paths)}',flush=True)
    report={'bundle_count':len(paths),'object_counts':dict(totals),'scriptables_exported':config_count,'failures':failures,'bundles':rows}
    (project_cfg.REPORTS_DIR/'all_resources_inventory.json').write_text(json.dumps(report,indent=2)+'\n')
    for name in ['AllScriptables','Localization']:
        shutil.copytree(project_cfg.RESOURCES_CONFIG_DIR/name,project_cfg.RECOVERED_ASSETS_ROOT/'Data'/name,dirs_exist_ok=True)
    print('Counts',dict(totals));print('Scriptables',config_count,'failures',len(failures))


if __name__=='__main__':main()
