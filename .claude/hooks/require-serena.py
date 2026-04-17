#!/usr/bin/env python3
"""
PreToolUse hook: block Read/Grep/Edit on *.cs files in this repo until
mcp__plugin_serena_serena__initial_instructions has been called in the
current session.

Approach A — stateless transcript scan. The session's own transcript is the
source of truth for "has initial_instructions fired this session?" No marker
files to manage, auto-scoped to the current session.
"""

import json
import os
import re
import sys


def main() -> int:
    try:
        payload = json.loads(sys.stdin.read() or "{}")
    except Exception:
        return 0  # fail open on malformed input

    tool_name = payload.get("tool_name", "")
    if tool_name not in ("Read", "Grep", "Edit"):
        return 0

    tool_input = payload.get("tool_input") or {}
    file_path = tool_input.get("file_path") or tool_input.get("path") or ""
    norm = file_path.replace("\\", "/")

    if not re.search(r"\.cs$", norm, re.IGNORECASE):
        return 0
    if "/chaos.client" not in norm.lower():
        return 0  # only guard .cs files inside the Chaos.Client repo

    transcript = payload.get("transcript_path") or ""
    if transcript and os.path.isfile(transcript):
        try:
            with open(transcript, "r", encoding="utf-8", errors="ignore") as f:
                for line in f:
                    if "mcp__plugin_serena_serena__initial_instructions" in line:
                        return 0  # manual was read this session — allow
        except OSError:
            return 0  # fail open if transcript unreadable

    reason = (
        "CLAUDE.md requires Serena semantic tools for C# code in this repo. "
        "Call mcp__plugin_serena_serena__initial_instructions first (reads the "
        "Serena manual, stateless — once per session is enough), then use "
        "find_symbol / get_symbols_overview / find_referencing_symbols / "
        "search_for_pattern / replace_symbol_body / insert_before_symbol / "
        "insert_after_symbol / rename_symbol / safe_delete_symbol instead of "
        "Read/Grep/Edit on .cs files. Read/Grep/Edit remain fine for non-C# "
        "files (markdown, config, etc.)."
    )
    print(json.dumps({
        "hookSpecificOutput": {
            "hookEventName": "PreToolUse",
            "permissionDecision": "deny",
            "permissionDecisionReason": reason,
        }
    }))
    return 0


if __name__ == "__main__":
    sys.exit(main())
