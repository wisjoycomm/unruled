"""Sync shared agent rules and root entry files from `project.config` (and optional `.env`)."""

from __future__ import annotations

import argparse
from pathlib import Path
from string import Template
import sys


def parse_env_file(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    if not path.exists():
        return values
    for raw_line in path.read_text(encoding="utf-8").splitlines():
        line = raw_line.strip()
        if not line or line.startswith("#") or "=" not in line:
            continue
        key, val = line.split("=", 1)
        key = key.strip()
        val = val.strip()
        if len(val) >= 2 and val[0] == val[-1] and val[0] in ("'", '"'):
            val = val[1:-1]
        values[key] = val
    return values


def load_config(repo_root: Path) -> dict[str, str]:
    config = parse_env_file(repo_root / "project.config")
    local_env = parse_env_file(repo_root / ".env")
    config.update(local_env)
    return config


def build_targets(repo_root: Path, cfg: dict[str, str]) -> list[tuple[Path, str]]:
    templates_dir = repo_root / ".agents" / "templates"
    entry_tmpl = Template((templates_dir / "ENTRY.md.tmpl").read_text(encoding="utf-8"))
    arch_tmpl = Template((templates_dir / "architecture.md.tmpl").read_text(encoding="utf-8"))
    server_tmpl = Template((templates_dir / "server.md.tmpl").read_text(encoding="utf-8"))
    perf_tmpl = Template((templates_dir / "unity-perf.md.tmpl").read_text(encoding="utf-8"))
    ld_tmpl = Template((templates_dir / "ldplayer-reference.md.tmpl").read_text(encoding="utf-8"))

    claude_models_block = (
        "\n## Models\n"
        f"- **{cfg['CLAUDE_MODEL_PRIMARY']}:** planning, architecture, risky changes "
        "(save/network data shapes, hot-update setup, asmdef changes, big refactors), reviewing large changes.\n"
        f"- **{cfg['CLAUDE_MODEL_SECONDARY']}:** everything else.\n"
        f"- On {cfg['CLAUDE_MODEL_SECONDARY']}, if a task is hard, ambiguous or risky, "
        f"say so and suggest switching to {cfg['CLAUDE_MODEL_PRIMARY']} before starting.\n"
    )

    outputs: list[tuple[Path, str]] = []

    # 1. Root entry files: CLAUDE.md and AGENTS.md
    outputs.append(
        (
            repo_root / "CLAUDE.md",
            entry_tmpl.substitute(
                cfg,
                AGENT_ENTRY_FILE="CLAUDE.md",
                AGENT_MODELS_BLOCK=claude_models_block,
                AGENT_RULES_DIR=cfg["CLAUDE_RULES_DIR"],
                AGENT_SKILLS_DIR=cfg["CLAUDE_SKILLS_DIR"],
            ),
        )
    )
    outputs.append(
        (
            repo_root / "AGENTS.md",
            entry_tmpl.substitute(
                cfg,
                AGENT_ENTRY_FILE="AGENTS.md",
                AGENT_MODELS_BLOCK="",
                AGENT_RULES_DIR=cfg["AGENTS_RULES_DIR"],
                AGENT_SKILLS_DIR=cfg["AGENTS_SKILLS_DIR"],
            ),
        )
    )

    # 2. Per-agent rules directories
    agent_profiles = [
        {
            "rules_dir": cfg["CLAUDE_RULES_DIR"],
            "ld_filename": "ldplayer-reference.md",
            "tool_rules": f"`CLAUDE.md` (KitWright section) and the skill `{cfg['CLAUDE_SKILLS_DIR']}/unity-mcp-workflow`.",
        },
        {
            "rules_dir": cfg["AGENTS_RULES_DIR"],
            "ld_filename": "ldplayer-reference.md",
            "tool_rules": f"`AGENTS.md` (KitWright section) and the skill `{cfg['AGENTS_SKILLS_DIR']}/unity-mcp-workflow`.",
        },
        {
            "rules_dir": cfg["CODEX_RULES_DIR"],
            "ld_filename": "ldplayer-reference.md",
            "tool_rules": f"`AGENTS.md` (KitWright section) and the skill `{cfg['CLAUDE_SKILLS_DIR']}/unity-mcp-workflow`.",
        },
        {
            "rules_dir": cfg["CURSOR_RULES_DIR"],
            "ld_filename": "ldplayer-reference.mdc",
            "tool_rules": "`kitwright-unity-mcp-workflow.mdc`",
        },
    ]

    for profile in agent_profiles:
        rdir = repo_root / profile["rules_dir"]
        outputs.append((rdir / "architecture.md", arch_tmpl.substitute(cfg)))
        outputs.append((rdir / "server.md", server_tmpl.substitute(cfg)))
        outputs.append((rdir / "unity-perf.md", perf_tmpl.substitute(cfg)))
        outputs.append(
            (
                rdir / profile["ld_filename"],
                ld_tmpl.substitute(cfg, AGENT_TOOL_RULES=profile["tool_rules"]),
            )
        )

    return outputs


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="Verify that all generated agent rule files match project.config and templates.",
    )
    args = parser.parse_args()

    repo_root = Path(__file__).resolve().parent.parent
    cfg = load_config(repo_root)
    targets = build_targets(repo_root, cfg)

    if args.check:
        out_of_sync: list[str] = []
        for path, expected in targets:
            if not path.exists():
                out_of_sync.append(f"MISSING: {path.relative_to(repo_root)}")
                continue
            actual = path.read_text(encoding="utf-8")
            if actual != expected:
                out_of_sync.append(f"DIFFERS: {path.relative_to(repo_root)}")
        if out_of_sync:
            for item in out_of_sync:
                print(item, file=sys.stderr)
            return 1
        print(f"All {len(targets)} agent rule files are in sync with project.config.")
        return 0

    for path, content in targets:
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")
    print(f"Synced {len(targets)} agent rule files from project.config.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
