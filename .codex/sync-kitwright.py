"""Refresh Codex's local KitWright connection from this Unity project's config."""

import json
from pathlib import Path
import re
import tomllib
from urllib.parse import urlparse


def main():
    project = Path(__file__).resolve().parent.parent
    source = json.loads((project / ".mcp.json").read_text(encoding="utf-8-sig"))
    url = source["mcpServers"]["kitwright"]["url"]
    endpoint = urlparse(url)
    if endpoint.scheme not in ("http", "https") or endpoint.hostname not in (
        "127.0.0.1", "localhost", "::1"
    ):
        raise ValueError("Expected this project's local KitWright HTTP endpoint")

    destination = project / ".codex" / "config.toml"
    existing = destination.read_text(encoding="utf-8") if destination.exists() else (
        "# Project-local Codex configuration. Ignored by Git because the URL contains a token.\n"
        "# Refresh after Unity restarts: python .codex/sync-kitwright.py\n"
    )
    tomllib.loads(existing)
    section = (
        "[mcp_servers.kitwright]\n"
        f"url = {json.dumps(url)}\n"
        "startup_timeout_sec = 20\n"
        "tool_timeout_sec = 120\n\n"
    )
    pattern = r"(?m)^\[mcp_servers\.kitwright\][^\n]*\n[\s\S]*?(?=^\[|\Z)"
    if re.search(pattern, existing):
        updated = re.sub(pattern, lambda _: section, existing, count=1)
    else:
        updated = existing.rstrip() + "\n\n" + section
    parsed = tomllib.loads(updated)
    if parsed["mcp_servers"]["kitwright"]["url"] != url:
        raise ValueError("KitWright configuration validation failed")
    destination.write_text(updated, encoding="utf-8")
    print("Codex KitWright connection refreshed; endpoint and token withheld.")


if __name__ == "__main__":
    main()
