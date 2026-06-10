#!/usr/bin/env python3
"""Minimal host-side agent loop for `luotsi inspect`.

The script proves the JSONL control contract without choosing an LLM framework.
It starts `luotsi inspect`, waits for text, taps it if present, captures a
screenshot, then exits. Replace `run_loop` with your own planner or model call.
"""

from __future__ import annotations

import argparse
import json
import os
import queue
import shutil
import subprocess
import sys
import threading
from dataclasses import dataclass
from pathlib import Path
from typing import Any


@dataclass
class InspectProcess:
    process: subprocess.Popen[str]
    output: "queue.Queue[str | None]"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Drive a Luotsi inspect JSONL session.")
    parser.add_argument("--device", required=True, help="ADB serial to pass to luotsi inspect.")
    parser.add_argument("--text", default="Sign in", help="Visible text to wait for and tap.")
    parser.add_argument("--text-match", default="exact", choices=("exact", "contains"), help="Text match mode.")
    parser.add_argument("--content-description", help="Optional content description selector.")
    parser.add_argument("--resource-id", help="Optional Android resource-id selector.")
    parser.add_argument("--class-name", help="Optional Android class selector.")
    parser.add_argument("--region", type=parse_region, help="Optional selector region: left,top,right,bottom.")
    parser.add_argument("--allow-ambiguous", action="store_true", help="Let Luotsi pick the highest-ranked match if multiple elements match.")
    parser.add_argument("--artifacts", help="Artifact base directory for the inspect session.")
    parser.add_argument("--luotsi", default="luotsi", help="Luotsi executable path.")
    parser.add_argument("--timeout-sec", type=int, default=15, help="Wait timeout for visible text.")
    parser.add_argument("--screenshot-label", default="agent-loop", help="Screenshot label.")
    parser.add_argument("--no-tap", action="store_true", help="Only wait for text and capture a screenshot.")
    return parser.parse_args()


def parse_region(value: str) -> dict[str, int]:
    parts = [part.strip() for part in value.split(",")]
    if len(parts) != 4:
        raise argparse.ArgumentTypeError("region must be four integers: left,top,right,bottom")

    try:
        left, top, right, bottom = [int(part) for part in parts]
    except ValueError as exc:
        raise argparse.ArgumentTypeError("region must be four integers: left,top,right,bottom") from exc

    return {"left": left, "top": top, "right": right, "bottom": bottom}


def start_inspect(args: argparse.Namespace) -> InspectProcess:
    if args.artifacts:
        Path(args.artifacts).mkdir(parents=True, exist_ok=True)

    inspect_args = ["inspect", "--device", args.device]
    if args.artifacts:
        inspect_args.extend(["--artifacts", args.artifacts])
    command = build_spawn_command(args.luotsi, inspect_args)

    process = subprocess.Popen(
        command,
        stdin=subprocess.PIPE,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        bufsize=1,
    )
    if process.stdin is None or process.stdout is None:
        raise RuntimeError("Failed to open inspect stdin/stdout pipes.")

    output: "queue.Queue[str | None]" = queue.Queue()
    threading.Thread(target=read_lines, args=(process.stdout, output), daemon=True).start()
    if process.stderr is not None:
        threading.Thread(target=forward_stderr, args=(process.stderr,), daemon=True).start()
    return InspectProcess(process, output)


def build_spawn_command(command: str, inspect_args: list[str]) -> list[str]:
    if os.name != "nt":
        return [command, *inspect_args]

    resolved = shutil.which(command) or command
    if resolved.lower().endswith((".cmd", ".bat")):
        return [
            os.environ.get("COMSPEC", "cmd.exe"),
            "/d",
            "/v:off",
            "/c",
            quote_cmd_line([resolved, *inspect_args]),
        ]
    return [resolved, *inspect_args]


def quote_cmd_line(parts: list[str]) -> str:
    return " ".join(quote_cmd_arg(part) for part in parts)


def quote_cmd_arg(value: str) -> str:
    return '"' + value.replace("%", "%%").replace('"', '""') + '"'


def read_lines(stream: Any, output: "queue.Queue[str | None]") -> None:
    try:
        for line in stream:
            output.put(line)
    finally:
        output.put(None)


def forward_stderr(stream: Any) -> None:
    for line in stream:
        sys.stderr.write(line)


def read_event(session: InspectProcess, timeout_sec: int) -> dict[str, Any]:
    try:
        line = session.output.get(timeout=timeout_sec)
    except queue.Empty as exc:
        raise TimeoutError(f"No inspect event arrived within {timeout_sec} seconds.") from exc

    if line is None:
        raise RuntimeError("Inspect session ended before the expected event arrived.")

    event = json.loads(line)
    print(json.dumps(event, separators=(",", ":")), flush=True)
    return event


def send_command(session: InspectProcess, command_id: str, command: str, **payload: Any) -> None:
    if session.process.stdin is None:
        raise RuntimeError("Inspect stdin is closed.")

    request = {"id": command_id, "command": command, **payload}
    session.process.stdin.write(json.dumps(request, separators=(",", ":")) + "\n")
    session.process.stdin.flush()


def selector_payload(args: argparse.Namespace) -> dict[str, Any]:
    payload: dict[str, Any] = {
        "text": args.text,
        "text_match": args.text_match,
    }
    if args.content_description:
        payload["content_description"] = args.content_description
    if args.resource_id:
        payload["resource_id"] = args.resource_id
    if args.class_name:
        payload["class_name"] = args.class_name
    if args.region:
        payload["region"] = args.region
    if args.allow_ambiguous:
        payload["allow_ambiguous"] = True
    return payload


def wait_for_command_result(
    session: InspectProcess,
    command_id: str,
    timeout_sec: int,
    *,
    wait_for_settle: bool = False,
) -> dict[str, Any]:
    result: dict[str, Any] | None = None
    while True:
        event = read_event(session, timeout_sec)
        if event.get("type") in {"session_error", "protocol_error"}:
            raise RuntimeError(f"Inspect session failed: {json.dumps(event, separators=(',', ':'))}")
        if event.get("type") == "command_result" and event.get("id") == command_id:
            result = event
            if not event.get("ok") or not wait_for_settle:
                return event
        if wait_for_settle and event.get("type") == "screen_delta" and event.get("id") == command_id and result:
            return result


def wait_for_initial_state(session: InspectProcess, timeout_sec: int) -> None:
    while True:
        event = read_event(session, timeout_sec)
        if event.get("type") == "screen_snapshot":
            return
        if event.get("type") in {"session_error", "protocol_error"}:
            raise RuntimeError(f"Inspect session failed: {json.dumps(event, separators=(',', ':'))}")


def run_loop(args: argparse.Namespace) -> int:
    session = start_inspect(args)
    try:
        wait_for_initial_state(session, args.timeout_sec)

        send_command(session, "1", "wait_visible", **selector_payload(args), timeout_sec=args.timeout_sec)
        visible = wait_for_command_result(session, "1", args.timeout_sec + 5)
        if not visible.get("ok"):
            send_command(session, "2", "capture_artifacts", label="agent-loop-wait-failed")
            wait_for_command_result(session, "2", args.timeout_sec)
            return 2

        if args.no_tap:
            send_command(session, "2", "screenshot", label=args.screenshot_label)
            screenshot = wait_for_command_result(session, "2", args.timeout_sec)
            if not screenshot.get("ok"):
                return 4
            return 0

        send_command(session, "2", "tap_text", **selector_payload(args), timeout_sec=5)
        tapped = wait_for_command_result(session, "2", args.timeout_sec, wait_for_settle=True)
        if not tapped.get("ok"):
            return 3

        send_command(session, "3", "screenshot", label=args.screenshot_label)
        screenshot = wait_for_command_result(session, "3", args.timeout_sec)
        if not screenshot.get("ok"):
            return 4
        return 0
    finally:
        stop_inspect(session)


def stop_inspect(session: InspectProcess) -> None:
    if session.process.poll() is None:
        try:
            send_command(session, "exit", "exit")
        except (BrokenPipeError, RuntimeError, OSError):
            pass

    try:
        exit_code = session.process.wait(timeout=5)
    except subprocess.TimeoutExpired as exc:
        session.process.terminate()
        raise RuntimeError("Inspect process did not exit within 5 seconds.") from exc

    if exit_code != 0:
        raise RuntimeError(f"Inspect process exited with code {exit_code}.")


def write_artifact_handoff(args: argparse.Namespace) -> None:
    if not args.artifacts:
        return

    print(f"inspect-agent-loop artifacts: {args.artifacts}", file=sys.stderr)
    print(
        f"inspect-agent-loop next: luotsi replay open --last --artifacts {quote_shell_arg(args.artifacts)} --dry-run",
        file=sys.stderr,
    )


def quote_shell_arg(value: str) -> str:
    safe = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_./:=-"
    if value and all(character in safe for character in value):
        return value

    return "'" + value.replace("'", "'\"'\"'") + "'"


def main() -> int:
    args: argparse.Namespace | None = None
    try:
        args = parse_args()
        exit_code = run_loop(args)
        write_artifact_handoff(args)
        return exit_code
    except Exception as exc:  # noqa: BLE001 - example script should surface concise failures.
        print(f"inspect-agent-loop failed: {exc}", file=sys.stderr)
        if args is not None:
            write_artifact_handoff(args)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
