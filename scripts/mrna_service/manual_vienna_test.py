"""Manually run Stage 1 top 10 candidates through ViennaRNA folding."""
import json
from pathlib import Path

from folding_backend import ViennaRNACudaBackend

# Load the Stage 1 top 10
with open("results/stage1_top10_candidates.json") as f:
    data = json.load(f)

candidates = data["candidates"]
print(f"Loaded {len(candidates)} candidates from run {data['run_id']}")
print()

backend = ViennaRNACudaBackend()
print(f"Using {backend.name}")
print()

for c in candidates:
    seq = c["coding_sequence"][:100]  # First 100 nt (5' region)
    r = backend.fold(seq)
    print(f"{c['id']} (first 100 nt):")
    print(f"  Structure: {r.structure[:80]}...")
    print(f"  MFE: {r.mfe:.2f} kcal/mol")
    print()
