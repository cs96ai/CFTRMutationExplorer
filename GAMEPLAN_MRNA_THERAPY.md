# mRNA Therapy Designer for Cystic Fibrosis — Game Plan

## Current Implementation Status (as of 2026)

The following has been implemented and differs from the original phase numbering below:

| Component | Status | Notes |
|-----------|--------|-------|
| **Stage 1: Codon Optimizer** | ✅ | Python FastAPI service (`scripts/mrna_service`). NSGA-II, 8 scoring functions, checkpoint persistence. WPF tab "Stage 1: Codon Optimizer". |
| **Stage 2: Phase 5 Rescoring** | ✅ | Second-stage rescoring with 12 metrics: RNA structure (ViennaRNACuda/ViennaRNA/Nussinov), GC windows, motif risk, codon diversity. Global folding capped at 600 nt; 3×400 nt windows for long sequences. Weight presets, diversity filter. |
| **Python service** | ✅ | `uvicorn main:app --host 127.0.0.1 --port 8787`. REST + WebSocket. |
| **WPF integration** | ✅ | MrnaApiClient talks to Python. Stage 1 + Stage 2 tabs. Phase 5 shows CDS length and Vienna folding windows in summary. |
| **UTR/Modification/Construct** | ✅ | UTR libraries, modification strategy, full construct assembly in Stage 1. |
| **GPU scoring** | ⚠️ | Optional `scoring_gpu.py`; Stage 1 uses CPU by default. |
| **Phases 5–8 below** | 📋 | Roadmap. "Phase 5" in our code = second-stage rescoring; game plan Phase 5 = UTR design (partially done in Stage 1). |

---

## Mission

Build a computational pipeline that designs optimized mRNA sequences encoding functional
wildtype CFTR protein. The goal: produce an mRNA construct that, when delivered to lung
epithelial cells, would cause them to manufacture working CFTR chloride channels — bypassing
whatever mutation the patient carries.

This is the same approach used by Moderna/BioNTech for COVID vaccines, applied to protein
replacement therapy instead of immunization.

---

## Why mRNA (Not Small Molecules)

| Approach | Targets | Compute Need | Output |
|----------|---------|-------------|--------|
| Small-molecule correctors (Trikafta) | ΔF508 specifically | 3D docking, GPU-heavy | Candidate compounds |
| **mRNA therapy** | **ALL 2,000+ CFTR mutations** | **Sequence math, CPU-friendly** | **Complete mRNA sequence** |

The mRNA approach is **mutation-agnostic** — it delivers a correct copy of the CFTR
instructions regardless of what went wrong in the patient's genome.

---

## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│                    C# / WPF Frontend                                │
│                                                                      │
│  ┌──────────────┐  ┌──────────────┐  ┌───────────────────────────┐  │
│  │ Existing Tabs │  │ mRNA Designer│  │ Optimization Dashboard    │  │
│  │ (3D, Compare, │  │ Tab          │  │ (progress, convergence,   │  │
│  │  Mutation,    │  │              │  │  Pareto front, results)   │  │
│  │  Annotations) │  │              │  │                           │  │
│  └──────────────┘  └──────┬───────┘  └───────────┬───────────────┘  │
│                           │                       │                  │
│                    ┌──────▼───────────────────────▼──────────┐       │
│                    │      mRNA Optimization Engine (C#)      │       │
│                    │  - Codon optimization                   │       │
│                    │  - Scoring functions                    │       │
│                    │  - NSGA-II genetic algorithm            │       │
│                    │  - GPU-accelerated batch fitness        │       │
│                    └──────┬──────────────────────────────────┘       │
│                           │                                          │
│                    ┌──────▼──────────────────────────────────┐       │
│                    │   RNA Structure Prediction (Python)     │       │
│                    │  - ViennaRNA / LinearFold               │       │
│                    │  - Minimum free energy calculation      │       │
│                    │  - Secondary structure visualization    │       │
│                    └─────────────────────────────────────────┘       │
└──────────────────────────────────────────────────────────────────────┘
                            │
               ┌────────────▼────────────────────┐
               │       External Data Sources     │
               │  - NCBI (CFTR CDS sequence)     │
               │  - Kazusa (codon usage tables)  │
               │  - AlphaFold DB (structures)    │
               └─────────────────────────────────┘
```

### Key Decision: C# Engine + Python RNA Folding

**Current implementation**: The optimizer and Phase 5 rescoring run in Python
(`scripts/mrna_service`). The WPF app calls the FastAPI service via HTTP/WebSocket.
RNA folding uses ViennaRNACuda (RNAfold_simple.exe), ViennaRNA (pip), or Nussinov fallback.

*Original plan*: Core optimization in C# with Python for RNA folding. The Python-first
approach was chosen for easier integration with ViennaRNA and faster iteration.

---

## The Science (What We're Actually Computing)

### The Problem

CFTR protein = 1,480 amino acids. The mRNA coding sequence (CDS) is ~4,443 nucleotides.
The genetic code is **degenerate** — most amino acids can be encoded by 2-6 different
codons. For example, Leucine (L) can be:

```
UUA, UUG, CUU, CUC, CUA, CUG  ← 6 choices, same protein
```

The total search space is approximately **3^1480 ≈ 10^706** possible mRNA sequences that
all encode the exact same CFTR protein. We need to find the one(s) that:

1. **Translate efficiently** — use codons that match abundant human tRNAs
2. **Stay stable** — resist degradation by cellular RNases
3. **Avoid immune detection** — minimize motifs that trigger innate immunity
4. **Fold correctly** — the mRNA secondary structure affects translation initiation
5. **Produce functional protein** — codon choices affect co-translational folding

### The Scoring Functions

| Objective | Metric | Target | Weight |
|-----------|--------|--------|--------|
| Translation efficiency | Codon Adaptation Index (CAI) | Maximize (→ 1.0) | High |
| mRNA stability | GC content (windowed) | 45-65% optimal | High |
| Immunogenicity | CpG dinucleotide count | Minimize | High |
| Immunogenicity | Uridine fraction | Minimize | Medium |
| Ribosome flow | Rare codon cluster score | Minimize | Medium |
| Translation initiation | 5' region folding energy | Minimize structure | Medium |
| Codon pair bias | Codon pair score (CPS) | Match human bias | Low |
| Codon diversity | Repeat sequence score | Minimize long repeats | Low |

### The Construct

A complete mRNA therapeutic is not just the coding sequence:

```
5'Cap ── 5'UTR ── START(AUG) ── CDS (4,443 nt) ── STOP ── 3'UTR ── Poly(A) tail
 │         │                      │                          │           │
 m7GpppN   Kozak context     Optimized codons         Stability      120-150 nt
           + low structure    encoding CFTR            elements
```

Each component will be designed:
- **5' Cap**: m7G cap analog (Cap1 structure) — fixed, not optimized
- **5' UTR**: Select from library of high-performing human UTRs or optimize de novo
- **CDS**: The main optimization target (~10^706 search space)
- **3' UTR**: Select from library (e.g., human β-globin 3'UTR variants)
- **Poly(A) tail**: 100-150 adenosines — length optimization
- **Nucleotide modifications**: N1-methylpseudouridine (m1Ψ) replacing all uridines

---

## Phase Breakdown

### Phase 1: Data Foundation & Core Models
**Goal**: Fetch real CFTR sequence data, build domain models, establish codon tables.

#### Deliverables
- [ ] `CftrSequenceProvider` — Fetch CFTR CDS from NCBI (accession NM_000492.4)
- [ ] Embedded fallback CFTR CDS sequence (4,443 nt) for offline use
- [ ] `CodonTable` model — all 64 codons mapped to amino acids
- [ ] `HumanCodonUsage` — frequency table from Kazusa database for Homo sapiens
- [ ] Domain models:
  - `MrnaSequence` — the full construct (cap, UTR, CDS, polyA)
  - `CodonChoice` — a single codon position with its alternatives
  - `OptimizationScore` — multi-objective fitness scores
  - `MrnaCandidate` — a scored sequence in the optimization population
  - `OptimizationConfig` — parameters (pop size, generations, weights, etc.)
  - `OptimizationProgress` — generation stats for UI reporting
- [ ] `IMrnaOptimizationService` interface
- [ ] `ICodonScoringService` interface
- [ ] `IRnaStructureService` interface
- [ ] Unit tests: codon table correctness, amino acid mapping, sequence validation

#### Technical Notes
- CFTR CDS: `NM_000492.4` from NCBI Nucleotide database
- Protein: UniProt P13569 (CFTR_HUMAN), 1,480 amino acids
- Human codon usage: Kazusa Codon Usage Database (Homo sapiens [9606])
- Store the wildtype CDS as an embedded resource for offline reliability

---

### Phase 2: Scoring Engine
**Goal**: Implement all fitness scoring functions that evaluate an mRNA candidate.

#### Deliverables
- [ ] **Codon Adaptation Index (CAI)**
  - For each codon, compute relative adaptiveness `w(c) = freq(c) / max_freq_for_aa`
  - CAI = geometric mean of all w(c) values across the CDS
  - Range: 0 to 1 (1 = every codon is the most frequent for its amino acid)
- [ ] **GC Content Analysis**
  - Overall GC% of CDS
  - Sliding window GC% (50 nt window, 10 nt step)
  - Penalize windows outside 40-65% range
  - Penalize overall GC outside 45-55%
- [ ] **CpG Dinucleotide Score**
  - Count CpG dinucleotides in the CDS
  - Compute observed/expected CpG ratio
  - Penalize ratio > 0.6 (human genome is CpG-depleted; high CpG triggers TLR9)
- [ ] **Uridine Fraction**
  - Count uridines (even with m1Ψ modification, fewer is better)
  - Target: minimize while maintaining protein sequence
- [ ] **Rare Codon Cluster Detection**
  - Identify stretches of ≥3 consecutive rare codons (frequency < 10%)
  - These cause ribosome stalling and reduced protein output
  - Score: count and severity of clusters
- [ ] **Codon Pair Bias (CPS)**
  - Some codon pairs are over/under-represented in human genes
  - Compute codon pair score using human codon pair frequency table
  - Penalize under-represented pairs (correlate with reduced expression)
- [ ] **Repeat Sequence Score**
  - Detect homopolymer runs (≥6 identical nucleotides)
  - Detect dinucleotide repeats (≥8 nt)
  - These cause synthesis issues and potential recombination
- [ ] **Composite Fitness Function**
  - Weighted combination of all scores
  - Configurable weights via `OptimizationConfig`
  - Normalize each score to [0, 1] range before combining
- [ ] Unit tests for each scoring function with known sequences

#### Technical Notes
- All scoring functions must be **stateless and thread-safe** for parallel evaluation
- Design for batch evaluation: score N sequences simultaneously
- CAI reference: Sharp & Li (1987), Nucleic Acids Research
- Codon pair bias reference: Coleman et al. (2008), Science

---

### Phase 3: RNA Secondary Structure Integration
**Goal**: Predict mRNA folding to assess translation initiation and stability.

#### Deliverables
- [ ] Python helper script using ViennaRNA (`RNAfold`)
  - Input: RNA sequence (or subsequence)
  - Output: minimum free energy (MFE), dot-bracket structure, base pair probabilities
- [ ] C# `RnaStructureService` that calls Python subprocess
  - `Task<RnaFoldResult> PredictStructure(string sequence)`
  - `Task<double> CalculateLocalFoldingEnergy(string sequence, int start, int length)`
- [ ] **5' Region Folding Assessment**
  - Predict structure of first 100 nt (5'UTR + start of CDS)
  - Strong structure near AUG = poor ribosome loading
  - Score: penalize MFE < -30 kcal/mol in the 5' leader
- [ ] **Sliding Window MFE Profile**
  - Calculate local MFE along the entire CDS (50 nt windows)
  - Identify regions of extreme stability (potential translation pause sites)
- [ ] Caching layer to avoid redundant folding predictions
- [ ] Unit tests with known RNA structures

#### Technical Notes
- ViennaRNA is the gold standard for RNA secondary structure prediction
- Install: `pip install ViennaRNA` (or conda)
- LinearFold (from Baidu Research) is faster (O(n) vs O(n³)) — consider as alternative
- For the genetic algorithm, we'll only fold the 5' region per candidate (fast)
- Full-length folding is reserved for final top candidates
- Python script location: `scripts/rna_fold.py`
- Communication: JSON via stdin/stdout

---

### Phase 4: Genetic Algorithm (NSGA-II)
**Goal**: Build the multi-objective optimizer that searches for optimal mRNA sequences.

#### Deliverables
- [ ] **NSGA-II Implementation** (Non-dominated Sorting Genetic Algorithm II)
  - Multi-objective: simultaneously optimize CAI, GC%, CpG, uridine, rare codons
  - Pareto dominance ranking
  - Crowding distance for diversity preservation
- [ ] **Initialization**
  - Random: sample codons weighted by human usage frequency
  - Seeded: start from native CFTR CDS as one member of initial population
  - Hybrid: mix of random and seeded individuals
- [ ] **Crossover Operators**
  - Single-point crossover (at codon boundaries)
  - Uniform crossover (each codon position chosen from either parent)
  - Domain-aware crossover (respect exon boundaries / protein domain boundaries)
- [ ] **Mutation Operators**
  - Single codon swap: replace one codon with a synonymous alternative
  - Block mutation: re-randomize a stretch of 5-20 codons
  - Guided mutation: bias toward preferred codons in low-scoring regions
- [ ] **Selection**
  - Tournament selection with Pareto rank + crowding distance
- [ ] **Population Management**
  - Configurable population size (default: 500)
  - Configurable generations (default: 1,000)
  - Elitism: preserve Pareto front across generations
- [ ] **GPU-Accelerated Fitness Evaluation** (RTX 3090)
  - Batch-encode sequences as integer arrays on GPU
  - Parallel CAI, GC%, CpG, uridine scoring via CUDA kernels
  - Expected throughput: ~50,000 sequence evaluations/second
  - Technology: ILGPU (C# GPU library) or TorchSharp
- [ ] **Progress Reporting**
  - Per-generation: best/avg/worst fitness, Pareto front size, diversity metric
  - Emit `OptimizationProgress` events for UI binding
  - Support cancellation via `CancellationToken`
- [ ] **Convergence Detection**
  - Stop if Pareto front hasn't improved for N generations
  - Or if population diversity drops below threshold
- [ ] Unit tests: Pareto ranking, crossover correctness, mutation preserves protein

#### Technical Notes
- NSGA-II reference: Deb et al. (2002), IEEE Transactions on Evolutionary Computation
- The protein sequence must be INVARIANT — crossover and mutation can only swap synonymous codons
- Every candidate must encode the exact same 1,480 amino acid CFTR protein
- GPU scoring: encode A=0, U=1, G=2, C=3 as byte arrays, compute all metrics via parallel reduction
- Population of 500 × 4,443 nt = ~2.2 MB on GPU — trivial for 24 GB 3090

---

### Phase 5: UTR & Modification Design
**Goal**: Design the non-coding regions of the mRNA construct.

#### Deliverables
- [ ] **5' UTR Library**
  - Human α-globin 5'UTR (HBA1) — high translation efficiency
  - Human β-globin 5'UTR (HBB) — well-characterized
  - Optimized Kozak consensus: `GCCACCAUGG` around start codon
  - Custom UTR from literature (e.g., Moderna's proprietary-like designs)
  - Score each UTR by: length, GC%, folding energy, Kozak strength
- [ ] **3' UTR Library**
  - Human α-globin 3'UTR — standard for mRNA therapeutics
  - AES-mtRNR1 tandem 3'UTR — used in BioNTech constructs
  - Human β-globin 3'UTR
  - Score each by: stability elements, AU-rich element content
- [ ] **Poly(A) Tail Optimization**
  - Length range: 100-150 adenosines
  - Segmented poly(A) option: e.g., A(100)-linker-A(70) (Moderna approach)
  - Score: length correlates with stability and translation duration
- [ ] **Nucleotide Modification Strategy**
  - N1-methylpseudouridine (m1Ψ) — complete U→m1Ψ substitution
  - 5-methylcytidine (m5C) — optional, partial replacement
  - Model effect on: immune evasion score, translation efficiency
  - Output: modification map showing every modified position
- [ ] **5' Cap Selection**
  - CleanCap AG (TriLink) — current industry standard
  - ARCA (anti-reverse cap analog) — older approach
  - Cap1 structure: m7GpppAm — provides best translation
- [ ] **Full Construct Assembler**
  - Combine: Cap + 5'UTR + CDS + Stop + 3'UTR + PolyA
  - Validate: no internal stop codons, correct reading frame, length check
  - Calculate: total construct length, total GC%, overall stability estimate
- [ ] Unit tests for UTR scoring, construct assembly, frame validation

---

### Phase 6: WPF UI — mRNA Designer Tab
**Goal**: Build the user interface for the mRNA therapy design pipeline.

#### Deliverables
- [ ] **New tab: "mRNA Therapy Designer"** in existing `TabControl`
- [ ] **Sequence Input Panel**
  - "Fetch from NCBI" button — downloads real CFTR CDS
  - Text display of loaded sequence (scrollable, with position numbers)
  - Sequence stats: length, native CAI, native GC%, amino acid count
  - Ability to paste custom CDS for other CFTR variants
- [ ] **Optimization Configuration Panel**
  - Population size slider (100 - 2,000)
  - Generation count slider (100 - 10,000)
  - Objective weight sliders (CAI, GC%, CpG, uridine, rare codons)
  - UTR selection dropdowns (5' and 3')
  - Poly(A) length input
  - Modification strategy dropdown (m1Ψ, m5C, none)
  - GPU acceleration toggle (auto-detect 3090)
- [ ] **"Start Optimization" Button** (with cancel support)
- [ ] **Real-Time Progress Dashboard**
  - Generation counter (e.g., "Generation 347 / 1,000")
  - Progress bar
  - Live convergence chart (best fitness per generation)
  - Current Pareto front size
  - Estimated time remaining
  - Sequences evaluated per second
- [ ] **Results Panel**
  - DataGrid of top candidates from Pareto front
  - Columns: Rank, CAI, GC%, CpG Count, Uridine%, Stability, Overall Score
  - Click a candidate to see full details
- [ ] **Candidate Detail View**
  - Full scoring breakdown (radar chart or bar chart)
  - Codon usage heatmap (color-coded by frequency along the sequence)
  - Sliding window plots: GC%, CpG density, uridine density
  - Comparison with native CFTR CDS
  - mRNA secondary structure of 5' region (dot-bracket notation)
- [ ] **Full Construct Preview**
  - Visual layout: Cap → 5'UTR → CDS → Stop → 3'UTR → Poly(A)
  - Total length, overall stats
  - Modification map overlay
- [ ] **Export Panel**
  - Export optimized sequence as FASTA
  - Export full construct as GenBank format
  - Export optimization report as Markdown
  - Export scoring data as CSV
  - Copy sequence to clipboard
- [ ] `MrnaDesignerViewModel` — orchestrates all sub-panels
- [ ] `OptimizationProgressViewModel` — binds to live optimization state
- [ ] `CandidateDetailViewModel` — displays selected candidate analysis

---

### Phase 7: Visualization & Analysis
**Goal**: Rich visualizations for understanding optimization results.

#### Deliverables
- [ ] **Codon Usage Heatmap**
  - 1,480 cells (one per amino acid position)
  - Color: green (most frequent codon) → red (rarest codon)
  - Tooltip: codon, frequency, amino acid, position
  - Compare optimized vs native CDS side-by-side
- [ ] **Sliding Window Plots** (custom WPF `Canvas` or lightweight charting)
  - GC content along CDS (50 nt window)
  - CpG density along CDS
  - Uridine density along CDS
  - Local folding energy along CDS
  - Optimal zone overlay (shaded green band)
- [ ] **Pareto Front Scatter Plot**
  - 2D scatter: selectable X/Y axes from objectives (CAI vs CpG, GC vs uridine, etc.)
  - Color by Pareto rank
  - Click points to select candidates
  - Pareto front line highlighted
- [ ] **Convergence Chart**
  - Line plot: best/average fitness per generation
  - Pareto front size over generations
  - Population diversity over generations
- [ ] **mRNA Structure Diagram** (for top candidates)
  - Arc diagram or circle plot of predicted secondary structure
  - Color-coded by base-pair probability
  - Highlight 5' UTR and start codon region
- [ ] **Construct Map**
  - Linear diagram showing all construct regions
  - Clickable regions for detail view
  - Modification annotations overlay

#### Technical Notes
- For charting, consider: OxyPlot (free, WPF-native), LiveCharts2, or custom Canvas rendering
- Heatmap can be rendered as a `WriteableBitmap` for performance
- Structure diagrams can use simple WPF `Path` geometries

---

### Phase 8: Advanced Features & Iteration
**Goal**: Extend the pipeline with advanced optimization strategies and real-world considerations.

#### Deliverables
- [ ] **Simulated Annealing** as alternative optimizer
  - Single-objective mode for quick optimization
  - Useful for refining a single candidate from the Pareto front
- [ ] **Ensemble Optimization**
  - Run multiple independent populations
  - Merge Pareto fronts for better coverage
  - GPU enables parallel populations on 3090
- [ ] **Constraint Handling**
  - Hard constraints: no restriction enzyme sites (BsaI, BsmBI — used in cloning)
  - Hard constraints: no long homopolymer runs (synthesis limit: typically 6-8)
  - Soft constraints: maintain codon diversity per domain
- [ ] **Protein Domain-Aware Optimization**
  - CFTR has 5 domains: TMD1, NBD1, R region, TMD2, NBD2
  - Allow different optimization strategies per domain
  - NBD1 contains the ΔF508 region — can emphasize stability here
- [ ] **LNP Delivery Considerations**
  - mRNA length affects encapsulation efficiency
  - GC content affects LNP loading
  - Surface charge considerations
  - Display LNP compatibility score
- [ ] **Comparison with Known Therapeutics**
  - Load published mRNA sequences (if available) for comparison
  - Score Translate Bio MRT5005 design choices
  - Benchmark our optimization against published approaches
- [ ] **Batch Mode**
  - Optimize multiple CFTR variants in parallel
  - Compare optimized sequences across variants
- [ ] **Save/Resume Optimization**
  - Serialize population state to SQLite
  - Resume interrupted optimizations
  - Version and compare optimization runs

---

## Data Requirements

### Real CFTR Sequence (Phase 1)

```
Gene:     CFTR (ABCC7)
Protein:  UniProt P13569, 1,480 amino acids
mRNA:     NCBI NM_000492.4, CDS = 4,443 nucleotides
Location: Chromosome 7q31.2
```

### Human Codon Usage Table (Phase 1)

Source: Kazusa Codon Usage Database — Homo sapiens [9606]
Contains: frequency per 1,000 codons for all 64 codons
Example:
```
UUU (Phe) 17.6    UCU (Ser) 15.2    UAU (Tyr) 12.2    UGU (Cys) 10.6
UUC (Phe) 20.3    UCC (Ser) 17.7    UAC (Tyr) 15.3    UGC (Cys) 12.6
UUA (Leu)  7.7    UCA (Ser) 12.2    UAA (Stop) 1.0    UGA (Stop) 1.6
UUG (Leu) 12.9    UCG (Ser)  4.4    UAG (Stop) 0.8    UGG (Trp) 13.2
...
```

### Python Environment (Phase 3)

```
python >= 3.10
ViennaRNA >= 2.6
(or) LinearFold (Baidu Research, MIT license)
```

---

## GPU Utilization Plan (RTX 3090)

The 3090 has 24 GB VRAM and 10,496 CUDA cores. Here's how we'll use it:

| Component | GPU Usage | Expected Speedup |
|-----------|----------|-------------------|
| Batch CAI scoring | Parallel reduction over codon arrays | ~100x vs CPU |
| Batch GC% | Count G+C per sequence in parallel | ~100x vs CPU |
| Batch CpG counting | Parallel dinucleotide scan | ~100x vs CPU |
| Population fitness | Score all 500+ candidates simultaneously | ~50x vs CPU |
| Multiple populations | Run 4-8 independent optimizations | Linear scaling |

**Memory estimate**: 2,000 sequences × 4,443 nt × 1 byte = ~8.5 MB. The 3090 can
easily hold 100,000+ sequences simultaneously.

**Technology choice**: ILGPU (C# native CUDA) for direct integration, or TorchSharp
if we want to add ML-based scoring later.

---

## Estimated Timeline

| Phase | Description | Estimated Effort |
|-------|-------------|-----------------|
| 1 | Data Foundation & Core Models | Medium |
| 2 | Scoring Engine | Medium-Large |
| 3 | RNA Structure Integration | Medium |
| 4 | Genetic Algorithm (NSGA-II) | Large |
| 5 | UTR & Modification Design | Medium |
| 6 | WPF UI — mRNA Designer Tab | Large |
| 7 | Visualization & Analysis | Medium-Large |
| 8 | Advanced Features | Large (incremental) |

**MVP (Phases 1-4 + minimal UI)**: A working optimizer that takes the CFTR sequence
and outputs optimized mRNA candidates with multi-objective scores.

**Full Product (Phases 1-7)**: Complete desktop application with visualization,
analysis, and export.

---

## Scientific Disclaimers

This tool is a **computational design aid** for educational and research purposes.

- Optimized sequences have NOT been experimentally validated
- Translation efficiency predictions are based on statistical models, not cell-based assays
- Immunogenicity scoring is heuristic-based, not derived from immune response data
- LNP delivery, in vivo stability, and therapeutic efficacy cannot be predicted computationally
- This is NOT a substitute for wet-lab validation, animal studies, or clinical trials
- Any therapeutic application would require years of preclinical and clinical development

The goal is to apply the same computational techniques used in early-stage mRNA therapeutic
design at pharmaceutical companies, implemented as an open educational tool.

---

## References

1. Karikó, K. et al. (2005). Suppression of RNA recognition by Toll-like receptors. *Immunity*, 23(2), 165-175.
2. Karikó, K. et al. (2008). Incorporation of pseudouridine into mRNA yields superior nonimmunogenic vector. *Molecular Therapy*, 16(11), 1833-1840.
3. Polack, F.P. et al. (2020). Safety and Efficacy of the BNT162b2 mRNA Covid-19 Vaccine. *NEJM*, 383(27), 2603-2615.
4. Sharp, P.M. & Li, W.H. (1987). The codon adaptation index. *Nucleic Acids Research*, 15(3), 1281-1295.
5. Coleman, J.R. et al. (2008). Virus attenuation by genome-scale changes in codon pair bias. *Science*, 320(5884), 1784-1787.
6. Deb, K. et al. (2002). A fast and elitist multiobjective genetic algorithm: NSGA-II. *IEEE Trans. on Evolutionary Computation*, 6(2), 182-197.
7. Robinson, E. et al. (2018). Lipid nanoparticle-delivered chemically modified mRNA restores chloride secretion in cystic fibrosis. *Molecular Therapy*, 26(8), 2034-2046.
8. Translate Bio / Sanofi. MRT5005 Phase 1/2 clinical trial for CF (NCT03375047).
