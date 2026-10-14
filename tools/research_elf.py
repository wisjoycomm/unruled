#!/usr/bin/env python3
"""Offline ARM64 constructor probe. Never executes Android code on the host.

This is research tooling, not an asset decoder. Unsupported imports/syscalls
stop the emulator rather than guessing their return values.
"""
import struct
import io
import re
from pathlib import Path
from elftools.elf.elffile import ELFFile
from unicorn import Uc, UC_ARCH_ARM64, UC_MODE_ARM, UC_HOOK_CODE, UC_HOOK_MEM_INVALID
from unicorn.arm64_const import *

ROOT = Path(__file__).resolve().parents[1]
STUB = 0x50000000
STACK = 0x70000000
STOP = STUB + 0xF000
TLS = 0x71000000


def main():
    uc = Uc(UC_ARCH_ARM64, UC_MODE_ARM)
    heap = 0x60000000
    uc.mem_map(0, 0x1800000)
    uc.mem_map(STUB, 0x10000)
    uc.mem_map(heap, 0x8000000)
    uc.mem_map(STACK, 0x100000)
    uc.mem_map(TLS, 0x10000)
    uc.reg_write(UC_ARM64_REG_TPIDR_EL0, TLS)
    imports, calls, files = {}, [], {}
    map_lines = []

    def alloc(n):
        nonlocal heap
        p = heap
        heap += (n + 15) // 16 * 16
        if heap > 0x68000000:
            raise RuntimeError("Probe heap exhausted")
        return p

    def put_string(s):
        payload = s.encode()+b'\0'
        address = alloc(len(payload))
        uc.mem_write(address,payload)
        return address

    def string(p):
        result = bytearray()
        for i in range(4096):
            c = uc.mem_read(p+i, 1)[0]
            if not c:
                return bytes(result)
            result.append(c)
        raise ValueError("Unterminated string")

    import project_cfg
    pkg_lib = f"/data/app/{project_cfg.REF_GAME_PACKAGE}/lib/arm64/libunity.so"
    with (ROOT / "extracted/native/libunity.so").open("rb") as f:
        elf = ELFFile(f)
        for segment in elf.iter_segments():
            if segment["p_type"] == "PT_LOAD":
                uc.mem_write(segment["p_vaddr"], segment.data())
                start = segment['p_vaddr'] & ~4095
                end = (segment['p_vaddr'] + segment['p_memsz'] + 4095) & ~4095
                map_lines.append(f"{start:012x}-{end:012x} r-xp {segment['p_offset'] & ~4095:08x} 00:00 1 {pkg_lib}\n")
        dyn = {t.entry.d_tag: t.entry.d_val for t in elf.get_section_by_name(".dynamic").iter_tags()}
        symbols = list(elf.get_section_by_name(".dynsym").iter_symbols())
        for address, size in [(dyn["DT_RELA"], dyn["DT_RELASZ"]), (dyn["DT_JMPREL"], dyn["DT_PLTRELSZ"])]:
            for offset, info, addend in struct.iter_unpack("<QQq", uc.mem_read(address, size)):
                kind, index = info & 0xffffffff, info >> 32
                if kind == 1027:
                    value = addend
                elif kind in (257, 1025, 1026):
                    sym = symbols[index]
                    if sym["st_value"]:
                        value = sym["st_value"] + addend
                    else:
                        value = STUB + index * 16
                        imports[value] = sym.name
                        uc.mem_write(value, bytes.fromhex("c0035fd6"))
                else:
                    raise RuntimeError(f"Unsupported relocation {kind}")
                uc.mem_write(offset, struct.pack("<Q", value))
        inits = struct.unpack("<" + "Q"*(dyn["DT_INIT_ARRAYSZ"]//8), uc.mem_read(dyn["DT_INIT_ARRAY"], dyn["DT_INIT_ARRAYSZ"]))

    def hook(uc, address, size, data):
        if address == STOP:
            uc.emu_stop()
            return
        name = imports.get(address)
        if name is None:
            return
        args = [uc.reg_read(UC_ARM64_REG_X0+i) for i in range(4)]
        a, b, c, d = args
        result = 0
        calls.append((name, args))
        print(name, *(hex(x) for x in args), flush=True)
        if name in ("malloc", "_Znwm", "_Znam"):
            result = alloc(a)
        elif name == "calloc":
            result = alloc(a*b)
        elif name in ("memcpy", "memmove", "__memcpy_chk"):
            uc.mem_write(a, bytes(uc.mem_read(b, c)))
            result = a
        elif name == "memset":
            uc.mem_write(a, bytes([b & 255])*c)
            result = a
        elif name in ("strlen", "__strlen_chk"):
            result = len(string(a))
        elif name in ("strcmp", "strncmp", "memcmp"):
            x, y = (bytes(uc.mem_read(a,c)), bytes(uc.mem_read(b,c))) if name == "memcmp" else (string(a), string(b))
            if name == "strncmp":
                x, y = x[:c], y[:c]
            result = (x > y) - (x < y)
        elif name in ("free", "__cxa_atexit", "__cxa_finalize"):
            pass
        elif name == "fopen":
            path = string(a).decode()
            print("VIRTUAL OPEN", path, flush=True)
            if path in ("/proc/self/maps", "/proc/1234/maps"):
                payload = ''.join(map_lines).encode()
            elif path.endswith('/libunity.so'):
                payload = (ROOT / "extracted/native/libunity.so").read_bytes()
            else:
                raise RuntimeError(f"No virtual file: {path}")
            result = alloc(16)
            files[result] = io.BytesIO(payload)
        elif name == "fgets":
            payload = files[c].readline(b-1)
            if payload:
                uc.mem_write(a, payload+b'\0')
                result = a
        elif name == "fread":
            payload = files[d].read(b*c)
            uc.mem_write(a, payload)
            result = len(payload)//b
        elif name == "fseek":
            signed = b if b < 1<<63 else b-(1<<64)
            files[a].seek(signed,c)
        elif name == "ftell":
            result = files[a].tell()
        elif name == "fclose":
            files.pop(a, None)
        elif name in ("sscanf", "__isoc99_sscanf"):
            source, fmt = string(a).decode(), string(b).decode()
            print("SSCANF", repr(fmt), repr(source[:120]), flush=True)
            if fmt in ("%lx-%lx %4s %*x %*x:%*x %*d%n", "%lx-%lx %4s %lx %*x:%*x %*d%n"):
                match = re.match(r'([0-9a-f]+)-([0-9a-f]+) (\S+) ([0-9a-f]+) [0-9a-f]+:[0-9a-f]+ \d+', source)
                if match:
                    pointers = [uc.reg_read(UC_ARM64_REG_X0+i) for i in range(2,7)]
                    uc.mem_write(pointers[0], struct.pack('<Q',int(match[1],16)))
                    uc.mem_write(pointers[1], struct.pack('<Q',int(match[2],16)))
                    uc.mem_write(pointers[2], match[3].encode()+b'\0')
                    result=3
                    if '%4s %lx' in fmt:
                        uc.mem_write(pointers[3], struct.pack('<Q',int(match[4],16)))
                        result=4
                    uc.mem_write(pointers[result], struct.pack('<I',match.end()))
            elif fmt == "%lx-%lx %s %lx %s %d %s":
                match = re.match(r'([0-9a-f]+)-([0-9a-f]+) (\S+) ([0-9a-f]+) (\S+) (\d+) (.+)', source)
                if match:
                    pointers = [uc.reg_read(UC_ARM64_REG_X0+i) for i in range(2,8)]
                    pointers.append(struct.unpack('<Q',uc.mem_read(uc.reg_read(UC_ARM64_REG_SP),8))[0])
                    for index, (p,v) in enumerate(zip(pointers,match.groups())):
                        if index in (0,1,3):uc.mem_write(p,struct.pack('<Q',int(v,16)))
                        elif index==5:uc.mem_write(p,struct.pack('<I',int(v)))
                        else:uc.mem_write(p,v.encode()+b'\0')
                    result=7
            else:
                raise RuntimeError(f"Unsupported sscanf format: {fmt}")
        elif name == "getpid":
            result = 1234
        elif name == "sysconf" and a in (30,39):
            result = 4096
        elif name == "mprotect":
            pass
        elif name in ("mmap", "mmap64"):
            if d & 0x10:
                raise RuntimeError('Fixed mmap requires explicit modeling')
            heap_padding = (-heap) % 4096
            if heap_padding: alloc(heap_padding)
            result = alloc((b+4095)//4096*4096)
        elif name == "munmap":
            pass
        elif name == "__errno":
            result = TLS + 128
        elif name == "dladdr":
            if a < 0x1800000:
                uc.mem_write(b, struct.pack('<QQQQ',put_string(pkg_lib),0,0,0))
                result=1
        elif name == "dlopen":
            library = string(a).decode() if a else '<self>'
            print('VIRTUAL DLOPEN',library,flush=True)
            if library not in ('libc.so','libdl.so','<self>'):
                raise RuntimeError(f'Unsupported virtual library: {library}')
            result=alloc(16)
        elif name == "dlsym":
            symbol = string(b).decode()
            print('VIRTUAL DLSYM',symbol,flush=True)
            result = next((p for p,n in imports.items() if n==symbol),0)
            if not result:
                result = STUB+0x8000+len(imports)*16
                imports[result] = symbol
                uc.mem_write(result,bytes.fromhex('c0035fd6'))
        elif name == "dlclose":
            pass
        elif name in ('strstr','strchr','strrchr'):
            source = string(a)
            needle = string(b) if name=='strstr' else bytes([b&255])
            index=source.rfind(needle) if name=='strrchr' else source.find(needle)
            result=a+index if index>=0 else 0
        elif name in ('strcpy','strncpy'):
            payload=string(b)+b'\0'
            if name=='strncpy':payload=payload[:c].ljust(c,b'\0')
            uc.mem_write(a,payload)
            result=a
        elif name == "getauxval":
            result = 0
        elif name == "__system_property_get":
            print("PROPERTY", string(a), flush=True)
            uc.mem_write(b, b"\0")
        else:
            if name in ("dlopen", "fopen", "open", "getenv", "dlsym"):
                print("STRING ARG", string(b if name == "dlsym" else a), flush=True)
            raise RuntimeError(f"Unsupported probe import: {name}")
        uc.reg_write(UC_ARM64_REG_X0, result & ((1<<64)-1))

    def invalid(uc, access, address, size, value, data):
        print("INVALID MEMORY", hex(uc.reg_read(UC_ARM64_REG_PC)), hex(address), size, flush=True)
        return False

    uc.hook_add(UC_HOOK_CODE, hook, begin=STUB, end=STUB+0xffff)
    uc.hook_add(UC_HOOK_MEM_INVALID, invalid)
    for address in inits:
        print("CONSTRUCTOR", hex(address), flush=True)
        uc.reg_write(UC_ARM64_REG_SP, STACK+0x80000)
        uc.reg_write(UC_ARM64_REG_LR, STOP)
        try:
            uc.emu_start(address, STOP, count=1000000000)
            if uc.reg_read(UC_ARM64_REG_PC) != STOP:
                raise RuntimeError("Instruction budget reached")
        except Exception as exc:
            print("STOPPED", exc, "PC", hex(uc.reg_read(UC_ARM64_REG_PC)), flush=True)
            break
    (ROOT / 'reports/elf_probe_memory.bin').write_bytes(bytes(uc.mem_read(0,0x1800000)))
    (ROOT / 'reports/elf_probe_heap.bin').write_bytes(bytes(uc.mem_read(0x60000000,heap-0x60000000)))


if __name__ == "__main__":
    main()
