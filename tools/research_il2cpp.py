#!/usr/bin/env python3
"""Offline ARM64 constructor probe for `libil2cpp.so`. Never executes Android code on the host.

Delegates to `arm64_vm.probe_elf` and writes `reports/il2cpp_probe_memory.bin`,
`reports/il2cpp_probe_heap.bin`, and `reports/il2cpp_probe_imports.json`.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import arm64_vm  # noqa: E402


def main():
    arm64_vm.probe_elf(
        lib_name="libil2cpp.so",
        code_size=0x5000000,
        heap_size=0x10000000,
        report_prefix="il2cpp_probe",
        insn_budget=5_000_000_000,
    )


if __name__ == "__main__":
    main()
