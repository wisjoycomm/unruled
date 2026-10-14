#!/usr/bin/env python3
"""Convert all downloaded video into H.264/AAC MP4 for Unity VideoPlayer."""
import concurrent.futures
import json
import subprocess
from pathlib import Path

import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT=project_cfg.ROOT
OUTPUT=project_cfg.RECOVERED_ASSETS_ROOT/'Video'


def convert(path):
    output=OUTPUT/(path.stem+'.mp4')
    row={'source':str(path.relative_to(ROOT)),'output':str(output.relative_to(ROOT))}
    try:
        probe=subprocess.run(['ffprobe','-v','error','-show_streams','-of','json',str(path)],capture_output=True,text=True,check=True)
        source=json.loads(probe.stdout)
        row['source_codecs']=[s.get('codec_name') for s in source['streams']]
        partial=output.with_suffix('.part.mp4')
        command=['ffmpeg','-nostdin','-hide_banner','-loglevel','error','-y','-i',str(path),
                 '-map','0:v:0','-map','0:a:0?','-c:v','libx264','-preset','fast','-crf','20',
                 '-pix_fmt','yuv420p','-c:a','aac','-b:a','160k','-movflags','+faststart',str(partial)]
        subprocess.run(command,capture_output=True,text=True,check=True)
        check=subprocess.run(['ffprobe','-v','error','-show_streams','-of','json',str(partial)],capture_output=True,text=True,check=True)
        streams=json.loads(check.stdout)['streams']
        video=next(s for s in streams if s['codec_type']=='video')
        if video['codec_name']!='h264' or int(video.get('nb_frames',0))<1:
            raise ValueError('Invalid H.264 output')
        partial.replace(output)
        row.update(status='exported',width=video['width'],height=video['height'],frames=int(video['nb_frames']))
    except Exception as exc:
        row.update(status='failed',error=getattr(exc,'stderr',None) or str(exc))
    return row


def main():
    OUTPUT.mkdir(parents=True,exist_ok=True)
    paths=sorted((ROOT/'downloaded/assets/video').glob('*'))
    paths=[p for p in paths if p.suffix in ('.mp4','.webm','.usm')]
    with concurrent.futures.ThreadPoolExecutor(max_workers=2) as pool:
        rows=[]
        for row in pool.map(convert,paths):
            rows.append(row);print(f"{len(rows)}/{len(paths)} {row['status']} {Path(row['source']).name}",flush=True)
    (ROOT/'reports/video_export.json').write_text(json.dumps(rows,indent=2)+'\n')


if __name__=='__main__':main()
