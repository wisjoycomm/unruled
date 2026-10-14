"""Print the root GameObject m_IsActive flag of each Hero UI prefab.

A popup prefab saved with an inactive root never runs Awake when it is instantiated into a
scene, so its button listeners are never attached and every click silently does nothing.
"""
import glob
import io
import os
import re

PREFABS = "UnityAssets/Assets/_EndlessWinter/Prefabs/Heroes/*.prefab"

for path in sorted(glob.glob(PREFABS)):
    text = io.open(path, encoding="utf-8", errors="ignore").read()
    match = re.search(r"^GameObject:\n(?:.*\n)*?  m_IsActive: (\d)", text, re.M)
    print(os.path.basename(path).ljust(30), "rootIsActive =", match.group(1) if match else "?")
