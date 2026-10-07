"""Generate pixel art with the PixelLab API (pixflux).

Reads PIXELLAB_API_KEY from the environment or the repo-root .env (gitignored).
Each generation costs one trial generation / a few cents, so keep jobs small.

    python tools/pixellab_gen.py jobs.json out_dir

jobs.json: [{"name": "drill", "description": "...", "size": 32, ...extra params}]
Optional "endpoint" (default "create-image-pixflux", e.g. "create-image-bitforge").
Any "*_image" value given as a file path (relative to jobs.json) is sent as base64.
"""
import base64
import json
import os
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

API = "https://api.pixellab.ai/v2"
ROOT = Path(__file__).resolve().parent.parent

# Shared style so a batch reads as one set.
STYLE = {
    "view": "high top-down",
    "outline": "single color black outline",
    "shading": "basic shading",
    "detail": "medium detail",
    "no_background": True,
    "seed": 3037,
}


def api_key() -> str:
    if key := os.environ.get("PIXELLAB_API_KEY"):
        return key
    env = ROOT / ".env"
    if env.exists():
        for line in env.read_text().splitlines():
            if line.startswith("PIXELLAB_API_KEY="):
                return line.split("=", 1)[1].strip()
    sys.exit("PIXELLAB_API_KEY not set (env var or .env)")


def generate(job: dict, key: str, base: Path) -> bytes:
    job = dict(job)
    size = job.pop("size")
    job.pop("name")
    endpoint = job.pop("endpoint", "create-image-pixflux")
    for k, v in job.items():
        if k.endswith("_image") and isinstance(v, str):
            job[k] = {"type": "base64", "base64": base64.b64encode((base / v).read_bytes()).decode()}
    body = {**STYLE, **job, "image_size": {"width": size, "height": size}}
    req = urllib.request.Request(
        f"{API}/{endpoint}",
        data=json.dumps(body).encode(),
        headers={"Authorization": f"Bearer {key}", "Content-Type": "application/json"},
    )
    for attempt in range(6):
        try:
            with urllib.request.urlopen(req, timeout=180) as resp:
                return base64.b64decode(json.load(resp)["image"]["base64"])
        except urllib.error.HTTPError as e:
            if e.code != 429 or attempt == 5:
                sys.exit(f"HTTP {e.code}: {e.read().decode(errors='replace')}")
            wait = int(e.headers.get("Retry-After") or 15 * (attempt + 1))
            print(f"  rate limited, waiting {wait}s", flush=True)
            time.sleep(wait)


def main():
    jobs_path = Path(sys.argv[1])
    jobs = json.loads(jobs_path.read_text(encoding="utf-8"))
    out = Path(sys.argv[2])
    out.mkdir(parents=True, exist_ok=True)
    key = api_key()
    for job in jobs:
        path = out / f"{job['name']}.png"
        if path.exists():
            print(f"skip {path.name} (exists)")
            continue
        print(f"generating {path.name} ...", flush=True)
        path.write_bytes(generate(job, key, jobs_path.parent))


if __name__ == "__main__":
    main()
