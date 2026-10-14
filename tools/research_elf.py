#!/usr/bin/env python3
"""Offline ARM64 constructor probe for `libunity.so`. Never executes Android code on the host.

Delegates to `arm64_vm.probe_elf` and writes `reports/elf_probe_memory.bin`,
`reports/elf_probe_heap.bin`, and `reports/elf_probe_imports.json`.
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import arm64_vm  # noqa: E402


def main():
    arm64_vm.probe_elf(
        lib_name="libunity.so",
        code_size=0x1800000,
        heap_size=0x08000000,
        report_prefix="elf_probe",
        insn_budget=1_000_000_000,
    )


if __name__ == "__main__":
    main()
