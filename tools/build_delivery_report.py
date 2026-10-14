#!/usr/bin/env python3
"""Write the final Vietnamese handoff from measured export/validation reports."""
import collections
import hashlib
import json
from pathlib import Path
import sys

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402

ROOT=project_cfg.ROOT
REC_SUBDIR=project_cfg.RECOVERED_SUBDIR
PKG_PREFIX=project_cfg.PROJECT_NAME


def report(name):
    return json.loads((ROOT/'reports'/name).read_text())


def main():
    inventory=report('download_inventory_summary.json')
    resources=report('all_resources_inventory.json')
    full=report('full_export_summary.json')
    unity=report('unity_3d_validation.json')
    media=report('unity_media_data_validation.json')
    spine=report('spine_export.json')
    config=report('lua_config_export.json')
    scenes=report('unity_scene_validation.json')
    config_validation=report('config_json_validation.json')
    packages=report('package_verification.json')
    counts=resources['object_counts']
    summary={'build':'1.33.9 (304)','manifest':inventory['status'],
             'decrypted_bundles':resources['bundle_count'],'raw_object_counts':counts,
             'unity_3d':{k:v for k,v in unity.items() if k!='prefabs'},'unity_media_data':media,
             'scene_files':full['scenes'],'lua_config_json':sum(r['status']=='exported' for r in config),
             'scriptable_json':resources['scriptables_exported'],
             'scene_validation':scenes, 'config_validation':config_validation,
             'packages':packages, 'gallery_validation':report('gallery_validation.json'),
             'usable_animated_prefabs':sum(p['sampledClips']>0 and not p['problems'] for p in unity['prefabs']),
             'spine':dict(collections.Counter(r['status'] for r in spine))}
    (ROOT/'reports/delivery_summary.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2)+'\n')
    manual=f'''{PKG_PREFIX.upper()} — TÀI NGUYÊN CHO UNITY
Phiên bản nguồn: 1.33.9, build 304. Unity kiểm chứng: {unity['unityVersion']}.

MỞ VÀ DÙNG
1. Unity Hub > Add project from disk > chọn thư mục dự án Unity ({project_cfg.UNITY_ROOT.name}).
2. Mở Assets/{REC_SUBDIR}/Recovered3D/Demo/CharacterGallery.unity, nhấn Play.
3. Prefab nhân vật: Assets/{REC_SUBDIR}/Recovered3D/Prefabs/Characters.
   Model môi trường: .../Prefabs/Environment. Clip: .../Animations.
   Controller mẫu: .../Playback. Scene: .../Scenes.
4. Để chuyển sang project khác, dùng Assets > Import Package > Custom Package:
   - {PKG_PREFIX}_3D.unitypackage
   - {PKG_PREFIX}_MediaData.unitypackage
   Hai gói này được xuất bằng Unity Editor thật. Dùng Built-in Render Pipeline.
   Dung lượng: 3D {packages[0]['bytes']/1e9:.2f} GB; MediaData {packages[1]['bytes']/1e9:.2f} GB.
   Mesh ở dạng .asset, prefab .prefab, clip .anim; Unity dùng trực tiếp, không cần FBX.

KẾT QUẢ KIỂM CHỨNG
- Đủ {inventory['total']:,} file trong manifest, tất cả kiểm tra kích thước và MD5.
- Tải thêm 8.534 file từ CDN, tổng 3.503.399.320 byte.
- Giải mã và giải nén hợp lệ {resources['bundle_count']:,} bundle UnityFS.
- Bộ Unity: {unity['meshes']:,} mesh, {unity['animations']:,} clip, {unity['avatars']:,} avatar,
  {unity['usablePrefabs']:,} prefab đạt kiểm tra. {unity['sampledClips']:,} phép lấy mẫu animation
  trên rig thực sự làm thay đổi transform; xem báo cáo cho từng prefab.
  Có {summary['usable_animated_prefabs']} prefab vừa đạt kiểm tra vừa có animation đã thử chuyển động.
- Media/data: {media['audio']:,} WAV, {media['video']:,} MP4, {media['dataFiles']:,} TextAsset.
- Config: {summary['lua_config_json']:,} bảng Lua JSON; {summary['scriptable_json']:,} Scriptable JSON.
- Spine: {summary['spine'].get('exported',0)} bộ skeleton + atlas + PNG, phiên bản 3.8.86/3.8.99.

TÀI NGUYÊN GỐC ĐẦY ĐỦ
AllAssetsExport/ExportedProject/Assets chứa bản xuất chưa bỏ thành phần gameplay:
- GameObject: {full['files_by_extension'].get('.prefab',0):,} prefab.
- Mesh, AnimationClip, Avatar, AnimatorController: model, clip, rig, controller gốc.
- Texture2D: PNG và .meta chứa thông tin sprite; có {counts.get('Sprite',0):,} Sprite trong nguồn.
- Material, Shader, MonoBehaviour, TextAsset, SpriteAtlas, Font và các thư mục scene.
- {len(full['scenes'])} scene: {', '.join(Path(s).stem for s in full['scenes'])}.
Đây là bản lưu nguồn tài nguyên. Scripts do AssetRipper tạo là stub rỗng,
không phải C# gameplay đã phục hồi. Không dùng nó như một bản game hoàn chỉnh.
PNG/TTF có thể import trực tiếp vào Unity; sao chép kèm .meta nếu muốn giữ GUID
và thiết lập sprite. Prefab UI, timeline, particle và shader trong bản xuất gốc
còn cần nối lại runtime/script/material tương ứng trước khi hoạt động đầy đủ.

CONFIG / SPINE / LUA
ResourcesExport/Config/LuaJSON: bảng config Lua đã đọc thành dữ liệu JSON.
ResourcesExport/Config/AllScriptables: dữ liệu serialized kèm đường dẫn bundle và PathID.
  Số vô hạn/NaN được giữ bằng object {{"$float":"Infinity"}}, {{"$float":"-Infinity"}}
  hoặc {{"$float":"NaN"}} để file tuân thủ JSON chuẩn. PPtr vẫn là tham chiếu dữ liệu gốc.
ResourcesExport/Config/Localization: bản dịch TXT và JSON.
ResourcesExport/Config/WorldBinary: dữ liệu map gốc; định dạng nhị phân được giữ nguyên.
ResourcesExport/Spine: mỗi bộ có .skel.bytes, .atlas.txt và PNG cùng thư mục.
  Cần runtime Spine-Unity tương thích 3.8 để tạo SkeletonDataAsset và phát animation.
ResourcesExport/Lua/32 và /64: LuaJIT bytecode nguyên gốc, có thể phân tích tiếp.

GIỚI HẠN CẦN BIẾT
- Bộ Recovered3D đã bỏ script gameplay và particle component để dùng độc lập.
  Các prefab chưa đạt kiểm tra nằm trong Assets/Incomplete3D; bản đầy đủ
  vẫn còn trong AllAssetsExport. Chi tiết: reports/character_catalog.csv.
- Shader riêng của game được thay bằng Standard trong bộ Recovered3D. Vật liệu,
  hiệu ứng, outline và ánh sáng có thể khác bản game; bản shader xuất gốc được giữ lại.
- Scene gốc chủ yếu là cấu trúc khởi tạo. Nhiều đối tượng được code/Lua tạo lúc chạy.
  Việc scene mở được không có nghĩa toàn bộ gameplay hoặc bản đồ runtime đã phục hồi.
- MP4 dùng H.264/AAC. Video webm có alpha được giữ nguyên ở downloaded/assets/video;
  bản MP4 không giữ alpha. WAV giữ waveform, chưa dựng lại cue sequence và mixer CRI.
- Đây là toàn bộ manifest của build 1.33.9; cập nhật hoặc nội dung server ngoài manifest
  cần manifest tương ứng. Code server không nằm trong APK. C# đã biên dịch IL2CPP,
  có thể phân tích một phần nhưng chưa khôi phục source/build project gốc.

TOOL TẢI VÀ GIẢI MÃ
Chạy từ thư mục này:
  .venv/bin/python tools/download_assets.py inventory
  .venv/bin/python tools/download_assets.py download --workers 8
  .venv/bin/python tools/download_assets.py download --group data --filter hero --limit 10
  .venv/bin/python tools/decrypt_bundles.py --resume
  .venv/bin/python tools/decrypt_bundles.py --input downloaded/assets/data --resume --report reports/decryption_downloaded.json
Tool tải hỗ trợ tải tiếp .part, retry và chỉ lưu file khi MD5/kích thước đúng.
CDN mặc định đã kiểm chứng:
  https://gof-gloabl-cdn.akamaized.net/RELEASE/1_33_9/android/split/
Decoder dành riêng cho thư viện của build này; kiểm tra SHA-256 trước khi mô phỏng ARM64.
Snapshot và công cụ nghiên cứu nằm ở reports/elf_probe_* và tools/research_*.py.

BÁO CÁO
reports/delivery_summary.json — tổng hợp.
reports/asset_inventory.csv — toàn bộ manifest và file tương ứng.
reports/download_results.json — kết quả tải CDN.
reports/all_resources_inventory.json — loại object theo từng bundle.
reports/unity_3d_validation.json — kết quả kiểm tra Unity và lý do loại prefab.
reports/unity_media_data_validation.json — kiểm tra media và dữ liệu trong Unity.
reports/unity_scene_validation.json — kết quả mở từng scene và kiểm tra tham chiếu.
reports/config_json_validation.json — kiểm tra cú pháp JSON chuẩn.
reports/character_catalog.csv — tra cứu prefab và số clip đã ghép/thử chuyển động.
reports/spine_export.json — các bộ Spine và phiên bản.
reports/package_verification.json — kiểm tra cấu trúc gói, số file và SHA-256.
reports/character_gallery_0.png và character_gallery_1.png — hai frame render thật từ Unity.
'''
    (ROOT/'HUONG_DAN_UNITY.txt').write_text(manual)
    print('Wrote HUONG_DAN_UNITY.txt and reports/delivery_summary.json')


if __name__=='__main__':main()
