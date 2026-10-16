import argparse
from pathlib import Path
import shutil
import subprocess
import sys

p = argparse.ArgumentParser()
p.add_argument("--enable", action="store_true")
a = p.parse_args()
root = Path(__file__).resolve().parents[1]
missing = [x for x in ("git", "gitleaks") if not shutil.which(x)]
if missing:
    sys.exit("Missing prerequisites: " + ", ".join(missing))
cmd = ["git", "-C", str(root), "config", "--local"]
existing = subprocess.run(cmd + ["--get", "core.hooksPath"], capture_output=True, text=True)
if existing.returncode not in (0, 1):
    sys.exit("Cannot inspect Git configuration")
value = existing.stdout.strip()
if value and value != ".githooks":
    sys.exit("Existing hooksPath preserved; review integration manually")
hooks = root / ".git" / "hooks"
if not value and any(x.is_file() and not x.name.endswith(".sample") for x in hooks.glob("*")):
    sys.exit("Existing Git hooks preserved; review integration manually")
if a.enable:
    subprocess.run(cmd + ["core.hooksPath", ".githooks"], check=True)
    print("Local sharing hooks enabled")
else:
    print("Prerequisites available. Preview only; use --enable after reviewing existing hooks.")
