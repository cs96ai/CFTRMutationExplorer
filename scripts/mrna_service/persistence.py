"""
Checkpoint persistence for optimization runs.
Saves/loads JSON files with datetime stamps for crash recovery.
"""

import json
from pathlib import Path


def save_checkpoint(data: dict, filepath: str):
    """Save checkpoint data as JSON. Atomic write via temp file."""
    path = Path(filepath)
    path.parent.mkdir(parents=True, exist_ok=True)

    tmp_path = path.with_suffix(".tmp")
    try:
        with open(tmp_path, "w") as f:
            json.dump(data, f, indent=2, default=str)
        tmp_path.replace(path)
    except Exception:
        if tmp_path.exists():
            tmp_path.unlink()
        raise


def load_checkpoint(filepath: str) -> dict:
    """Load checkpoint data from JSON."""
    with open(filepath, "r") as f:
        return json.load(f)


def list_runs(results_dir: str) -> list:
    """List all saved optimization runs."""
    path = Path(results_dir)
    if not path.exists():
        return []

    runs = []
    for f in sorted(path.glob("optimization_*_FINAL.json"), reverse=True):
        try:
            with open(f, "r") as fh:
                data = json.load(fh)
            runs.append({
                "filename": f.name,
                "filepath": str(f),
                "run_id": data.get("run_id", ""),
                "generation": data.get("generation", 0),
                "best_fitness": data.get("best_fitness", 0),
                "timestamp": data.get("timestamp", ""),
                "threshold_reached": data.get("threshold_reached", False),
                "pareto_front_size": data.get("pareto_front_size", 0),
            })
        except (json.JSONDecodeError, KeyError):
            continue

    # Also check for checkpoint files (non-final)
    for f in sorted(path.glob("optimization_*_gen*.json"), reverse=True):
        try:
            with open(f, "r") as fh:
                data = json.load(fh)
            runs.append({
                "filename": f.name,
                "filepath": str(f),
                "run_id": data.get("run_id", ""),
                "generation": data.get("generation", 0),
                "best_fitness": data.get("best_fitness", 0),
                "timestamp": data.get("timestamp", ""),
                "threshold_reached": data.get("threshold_reached", False),
                "pareto_front_size": data.get("pareto_front_size", 0),
            })
        except (json.JSONDecodeError, KeyError):
            continue

    # Deduplicate by run_id, keep latest
    seen = set()
    unique = []
    for r in runs:
        key = r["run_id"] + "_" + str(r["generation"])
        if key not in seen:
            seen.add(key)
            unique.append(r)
    return unique[:50]
