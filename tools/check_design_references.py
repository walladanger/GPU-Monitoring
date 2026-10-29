"""Verify the original design assets without third-party dependencies."""
import hashlib
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1] / "docs" / "design"
manifest = json.loads((root / "reference-manifest.json").read_text())
for name, expected in manifest.items():
    actual = hashlib.sha256((root / name).read_bytes()).hexdigest()
    if actual != expected["sha256"]:
        raise SystemExit(f"FAIL: approved reference changed: {name}")
    print(f"PASS: {name} ({expected['width']} x {expected['height']}) SHA-256")
