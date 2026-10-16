"""Fail-closed staged/history checks. Never prints file content or scanner output."""
import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile


class GuardError(Exception):
    pass


def git(root, *args):
    p = subprocess.run(["git", "-C", str(root), "--work-tree", str(root), *args], capture_output=True)
    if p.returncode:
        raise GuardError("Git inspection failed; publication blocked")
    return p.stdout


def forbidden(name, policy):
    n = name.replace("\\", "/").casefold()
    parts = PurePosixPath(n).parts
    return (any(n == x.casefold() or n.startswith(x.casefold() + "/") for x in policy["private_roots"])
            or any(x in {s.casefold() for s in policy["private_names"]} for x in parts)
            or any(x.startswith(".env") and x not in (".env.example", ".env.sample") for x in parts)
            or PurePosixPath(n).suffix in policy["private_extensions"]
            or any(x in ("bin", "obj", "__pycache__", ".vs") for x in parts))


PATTERNS = {
    "private-key": re.compile(r"-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----"),
    # POSIX home literals are split via concatenation so this scanner's own source
    # does not self-match; detection coverage is unchanged.
    "user-home-path": re.compile(
        r"(?:[A-Za-z]:[/\\]Users[/\\][^/\\\s]+|"
        + "/" "Users" "/" + r"[^/\s]+|"
        + "/" "home" "/" + r"[^/\s]+)",
        re.I,
    ),
    "local-runtime-id": re.compile(r"\b(?:term|run|task|dispatch)_[a-f0-9]{8,}\b", re.I),
}


def check_text(data, label):
    if b"\0" in data:
        return  # Binary assets require human review; do not claim complete privacy scanning.
    text = data.decode("utf-8", errors="replace")
    for kind, pattern in PATTERNS.items():
        match = pattern.search(text)
        if match:
            line = text.count("\n", 0, match.start()) + 1
            raise GuardError(f"{label}:{line}: {kind} (content withheld)")


def scan_snapshot(root, tree=None):
    # Read policy from the working copy so a staged policy weakening cannot silently pass.
    policy = json.loads((root / ".repo-sharing.json").read_text(encoding="utf-8-sig"))
    scanner = shutil.which("gitleaks")
    if not scanner:
        raise GuardError("gitleaks is required and missing; publication blocked")
    if tree:
        records = git(root, "ls-tree", "-r", "-z", tree).split(b"\0")
    else:
        records = git(root, "ls-files", "--stage", "-z").split(b"\0")
    with tempfile.TemporaryDirectory(prefix="share-guard-") as tmp:
        if tree:
            message = git(root, "show", "-s", "--format=%B", tree)
            check_text(message, "commit-message")
            Path(tmp, "__commit_message.txt").write_bytes(message)
        for record in records:
            if not record:
                continue
            meta, rawname = record.split(b"\t", 1)
            name = rawname.decode("utf-8")
            fields = meta.split()
            mode, oid = fields[0], fields[2] if tree else fields[1]
            if not tree and fields[2] != b"0":
                raise GuardError("Unmerged index; publication blocked")
            if mode not in (b"100644", b"100755"):
                raise GuardError(f"{name}: symlink/submodule requires separate review")
            if forbidden(name, policy):
                raise GuardError(f"{name}: local/private path must not be published")
            safe = PurePosixPath(name)
            if safe.is_absolute() or ".." in safe.parts or ":" in name:
                raise GuardError("Unsafe repository path")
            data = git(root, "cat-file", "blob", oid.decode("ascii"))
            check_text(data, name)
            dest = Path(tmp).joinpath(*safe.parts)
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(data)
        result = subprocess.run([scanner, "dir", tmp, "--no-banner", "--redact", "--exit-code", "1"],
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if result.returncode:
            raise GuardError("Secret scan failed or found a secret; publication blocked (values withheld)")


def outgoing_commits(root, updates):
    commits = set()
    for line in updates.splitlines():
        fields = line.split()
        if len(fields) != 4:
            raise GuardError("Invalid pre-push input")
        _, new, _, old = fields
        if not all(re.fullmatch(r"[0-9a-f]{40}|[0-9a-f]{64}", x) for x in (new, old)):
            raise GuardError("Invalid object identifier")
        if set(new) == {"0"}:
            raise GuardError("Remote deletion requires separate explicit review")
        if set(old) != {"0"}:
            fast_forward = subprocess.run(["git", "-C", str(root), "merge-base", "--is-ancestor", old, new],
                                          stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            if fast_forward.returncode:
                raise GuardError("Non-fast-forward or uninspectable remote history; separate review required")
        # For a new ref inspect all reachable history, even if some exists remotely.
        rev = new if set(old) == {"0"} else f"{old}..{new}"
        commits.update(git(root, "rev-list", rev).decode().splitlines())
    return sorted(commits)


def main():
    p = argparse.ArgumentParser()
    p.add_argument("mode", choices=["staged", "pre-push", "history"])
    p.add_argument("--root", default=".")
    args = p.parse_args()
    root = Path(args.root).resolve()
    try:
        if args.mode == "staged":
            scan_snapshot(root)
        else:
            commits = outgoing_commits(root, sys.stdin.read()) if args.mode == "pre-push" else git(root, "rev-list", "HEAD").decode().splitlines()
            for commit in commits:
                scan_snapshot(root, commit)
        print("Sharing checks passed")
    except (GuardError, OSError, ValueError, KeyError) as e:
        print(f"BLOCKED: {e}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
