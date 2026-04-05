#!/usr/bin/env python3

from __future__ import annotations

import json
import subprocess
import sys
from pathlib import Path


def repo_root() -> Path:
    return Path(__file__).resolve().parents[2]


def load_rules() -> tuple[list[str], set[str], str]:
    rules_path = Path(__file__).with_name("secret_policy_rules.json")

    try:
        data = json.loads(rules_path.read_text())
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"Invalid secret policy rules file: {rules_path}: {exc}") from exc
    except OSError as exc:
        raise RuntimeError(f"Unable to read secret policy rules file: {rules_path}: {exc}") from exc

    tracked_file_globs = data.get("trackedFileGlobs")
    jwt_placeholders = data.get("jwtPlaceholders")
    intake_invite_prefix = data.get("intakeInvitePrefix")

    if not isinstance(tracked_file_globs, list) or not tracked_file_globs or not all(isinstance(item, str) for item in tracked_file_globs):
        raise RuntimeError(f"secret policy rules file is missing trackedFileGlobs: {rules_path}")

    if not isinstance(jwt_placeholders, list) or not all(isinstance(item, str) for item in jwt_placeholders):
        raise RuntimeError(f"secret policy rules file is missing a valid jwtPlaceholders list: {rules_path}")

    if not isinstance(intake_invite_prefix, str) or not intake_invite_prefix:
        raise RuntimeError(f"secret policy rules file is missing intakeInvitePrefix: {rules_path}")

    return tracked_file_globs, set(jwt_placeholders), intake_invite_prefix


def tracked_appsettings_files(root: Path, tracked_file_globs: list[str]) -> list[Path]:
    try:
        result = subprocess.run(
            ["git", "ls-files", "--", *tracked_file_globs],
            cwd=root,
            capture_output=True,
            text=True,
            check=True,
        )
    except FileNotFoundError as exc:
        raise RuntimeError("git is required to enumerate tracked appsettings files for the secret policy scan.") from exc
    except subprocess.CalledProcessError as exc:
        output = "\n".join(part.strip() for part in [exc.stdout, exc.stderr] if part and part.strip())
        details = output or f"exit code {exc.returncode}"
        raise RuntimeError(f"Unable to enumerate tracked appsettings files with git ls-files: {details}") from exc

    return [root / line for line in result.stdout.splitlines() if line.strip()]


def validate_file(path: Path, root: Path, jwt_placeholders: set[str], intake_invite_prefix: str) -> list[str]:
    errors: list[str] = []
    rel = path.relative_to(root)

    try:
        data = json.loads(path.read_text())
    except json.JSONDecodeError as exc:
        return [f"PARSE ERROR: {rel} is not valid JSON: {exc}"]
    except OSError as exc:
        return [f"READ ERROR: {rel}: {exc}"]

    jwt_key = data.get("Jwt", {}).get("SigningKey", None)
    if jwt_key is not None and jwt_key not in jwt_placeholders:
        errors.append(
            f"FAIL: {rel} contains a non-placeholder Jwt:SigningKey. "
            "Use a REPLACE_ placeholder or empty string in tracked files."
        )

    intake_key = data.get("IntakeInvite", {}).get("SigningKey", None)
    if intake_key is not None and intake_key != "" and not intake_key.startswith(intake_invite_prefix):
        errors.append(
            f"FAIL: {rel} contains a non-placeholder IntakeInvite:SigningKey. "
            "Use a REPLACE_ placeholder or empty string in tracked files."
        )

    return errors


def main() -> int:
    root = repo_root()
    try:
        tracked_file_globs, jwt_placeholders, intake_invite_prefix = load_rules()
    except RuntimeError as exc:
        print(str(exc))
        return 1

    try:
        files = [Path(arg).resolve() for arg in sys.argv[1:]] if len(sys.argv) > 1 else tracked_appsettings_files(root, tracked_file_globs)
    except RuntimeError as exc:
        print(str(exc))
        return 1

    if not files:
        print("No tracked appsettings*.json files found.")
        return 0

    failures: list[str] = []
    for path in files:
        failures.extend(validate_file(path, root, jwt_placeholders, intake_invite_prefix))

    if failures:
        for failure in failures:
            print(failure)
        print("Secret policy scan FAILED.")
        return 1

    print(f"Secret policy scan passed for {len(files)} tracked appsettings file(s).")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
