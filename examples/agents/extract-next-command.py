#!/usr/bin/env python3
"""Read a Luotsi JSON envelope or JSONL-style log and print the next command."""

from __future__ import annotations

import json
import sys
from typing import Any


def main() -> int:
    try:
        envelope = read_envelope(sys.stdin.read())
        command = extract_next_command(envelope)
    except ValueError as error:
        print(f"extract-next-command: {error}", file=sys.stderr)
        return 1

    if not command:
        print("extract-next-command: no follow-up command found", file=sys.stderr)
        return 1

    print(command)
    return 0


def read_envelope(text: str) -> dict[str, Any]:
    stripped = text.strip()
    if not stripped:
        raise ValueError("stdin did not contain JSON")

    try:
        parsed = json.loads(stripped)
    except json.JSONDecodeError:
        parsed = None

    if is_loose_command_envelope(parsed) or is_run_summary(parsed):
        return parsed

    if isinstance(parsed, list):
        for item in reversed(parsed):
            if is_command_envelope(item) or is_run_summary(item):
                return item

    envelopes: list[dict[str, Any]] = []
    for line in stripped.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            item = json.loads(line)
        except json.JSONDecodeError:
            continue
        if is_command_envelope(item) or is_run_summary(item):
            envelopes.append(item)

    if envelopes:
        return envelopes[-1]

    raise ValueError("stdin did not contain a Luotsi command envelope or run summary")


def is_command_envelope(value: Any) -> bool:
    return isinstance(value, dict) and value.get("schema") == "luotsi-command.v1"


def is_loose_command_envelope(value: Any) -> bool:
    return isinstance(value, dict) and (
        is_command_envelope(value)
        or "ok" in value
        or "data" in value
        or "artifacts" in value
    )


def is_run_summary(value: Any) -> bool:
    return isinstance(value, dict) and value.get("schema") == "luotsi-run-summary.v1"


def extract_next_command(envelope: dict[str, Any]) -> str | None:
    if is_run_summary(envelope):
        command = extract_next_command_from_mapping(envelope)
        if command:
            return command

    data = envelope.get("data")
    if isinstance(data, dict):
        command = extract_next_command_from_mapping(data)
        if command:
            return command

    artifacts = envelope.get("artifacts")
    if isinstance(artifacts, dict):
        artifact_root = clean_command(artifacts.get("artifact_root"))
        if artifact_root:
            return f"luotsi replay packet --artifacts {quote_shell_arg(artifact_root)}"

    return None


def extract_next_command_from_mapping(value: dict[str, Any]) -> str | None:
    next_action = first_present(value, "recommended_next_action", "recommendedNextAction")
    if isinstance(next_action, dict):
        command = clean_command(next_action.get("command"))
        if command:
            return command

    primary_failure = first_present(value, "primary_failure", "primaryFailure")
    if isinstance(primary_failure, dict):
        command = clean_command(first_present(primary_failure, "source_command", "sourceCommand"))
        if command:
            return command

    for name in (
        "recommended_next_steps",
        "recommendedNextSteps",
        "next_actions",
        "nextActions",
        "suggested_commands",
        "suggestedCommands",
        "commands",
        "artifact_commands",
        "artifactCommands",
        "recommended_commands",
        "recommendedCommands",
    ):
        items = value.get(name)
        if isinstance(items, list):
            command = first_command(items, prefer_replay_open=name in (
                "commands",
                "artifact_commands",
                "artifactCommands",
                "recommended_commands",
                "recommendedCommands",
            ))
            if command:
                return command

    return None


def first_present(value: dict[str, Any], *names: str) -> Any:
    for name in names:
        if name in value:
            return value[name]
    return None


def first_command(items: list[Any], *, prefer_replay_open: bool) -> str | None:
    commands = [
        item
        for item in items
        if isinstance(item, dict) and clean_command(item.get("command"))
    ]
    if prefer_replay_open:
        replay = next(
            (
                item
                for item in commands
                if str(item.get("kind", "")).lower() == "replay_open"
                or "replay open" in str(item.get("command", "")).lower()
            ),
            None,
        )
        if replay:
            return clean_command(replay.get("command"))

    return clean_command(commands[0].get("command")) if commands else None


def clean_command(value: Any) -> str | None:
    if not isinstance(value, str):
        return None

    stripped = value.strip()
    return stripped or None


def quote_shell_arg(value: str) -> str:
    safe = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_./:=-" + "\\"
    if value and all(character in safe for character in value):
        return value

    return "'" + value.replace("'", "'\"'\"'") + "'"


if __name__ == "__main__":
    raise SystemExit(main())
