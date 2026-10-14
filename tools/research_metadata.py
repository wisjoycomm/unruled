#!/usr/bin/env python3
"""Offline emulation of this build's HTPX metadata reader."""
import struct
import sys
from pathlib import Path
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE
from unicorn.arm64_const import *
from decrypt_bundles import Decoder, ROOT

sys.path.insert(0, str(Path(__file__).resolve().parent))
import project_cfg  # noqa: E402


def main():
    d = Decoder.__new__(Decoder)
    d.uc = uc = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
    for address, size in [(0,0x5000000),(0x50000000,0x10000),(0x60000000,0x10000000),
                          (0x70000000,0x100000),(0x71000000,0x10000),(0x72000000,0x1000000)]:
        uc.mem_map(address,size)
    uc.mem_write(0,(project_cfg.REPORTS_DIR/'il2cpp_probe_memory.bin').read_bytes())
    uc.mem_write(0x60000000,(project_cfg.REPORTS_DIR/'il2cpp_probe_heap.bin').read_bytes())
    uc.reg_write(UC_ARM64_REG_TPIDR_EL0,0x71000000)
    d.heap = 0x67000000
    uc.hook_add(UC_HOOK_CODE,d.unhandled,begin=0x50000000,end=0x5000ffff)
    payload=(project_cfg.EXTRACTED_DIR/'base/assets/bin/Data/Managed/Metadata/global-metadata.dat').read_bytes()
    uc.mem_write(0x72000000,payload)
    uc.reg_write(UC_ARM64_REG_X0,0x72000000)
    uc.reg_write(UC_ARM64_REG_X27,struct.unpack('<Q',uc.mem_read(0x37c7260,8))[0])
    uc.reg_write(UC_ARM64_REG_SP,0x70080000)
    uc.emu_start(0x37730d8,0x3773750,count=2000000000)
    if uc.reg_read(UC_ARM64_REG_PC)!=0x3773750:
        raise RuntimeError(f'Budget exceeded at {uc.reg_read(UC_ARM64_REG_PC):#x}')
    decoded=bytes(uc.mem_read(0x72000000,len(payload)))
    # Original magic/version are replaced by HTPX+file length. Keep the decoded
    # body for string research without inventing a restored metadata version.
    (ROOT/'reports/metadata_decoded.bin').write_bytes(decoded)
    print('Decoded metadata body',len(decoded),decoded[8:40].hex())


if __name__=='__main__':main()
