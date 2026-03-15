# CFTR Mutation Explorer — Build Progress

## Overview

A WPF desktop application for biotech researchers studying cystic fibrosis.  
Researchers load protein structures, compare mutated variants, inspect structural changes, and identify potential drug-binding pockets.

## Phases

| Phase | Description | Status |
|-------|-------------|--------|
| 1 | Solution structure, domain models, PDB parser, basic file loading UI | ✅ Complete |
| 2 | 3D viewport with HelixToolkit, view modes, camera controls | ✅ Complete |
| 3 | Comparison features, mutation region inspection | ✅ Complete |
| 4 | Annotation system with SQLite persistence | ✅ Complete |
| 5 | Export, binding pocket heuristic, polish | ✅ Complete |
| 6 | mRNA Therapy Designer — Stage 1 (NSGA-II codon optimizer), Stage 2 (Phase 5 rescoring), Python FastAPI service | ✅ Complete |

---

## Phase 1 — Foundation ✅

### Deliverables
- [x] Solution with 4 projects (App, Core, Infrastructure, Tests)
- [x] Domain model classes (Atom, Residue, Chain, ProteinStructure, Annotation, etc.)
- [x] IPdbParser + PdbParser implementation with async + progress + cancellation
- [x] Unit tests for parsing (10 tests)
- [x] Main window shell with toolbar and file loading
- [x] Sample PDB files (CFTR_Normal.pdb, CFTR_F508del.pdb)
- [x] Async loading with progress indicator

---

## Phase 2 — 3D Visualization ✅

### Deliverables
- [x] HelixToolkit.Wpf integration
- [x] ViewportViewModel with scene building
- [x] Ball-and-stick backbone rendering (N, CA, C, O atoms)
- [x] View modes: Reference Only, Mutant Only, Overlay, Highlight Mutation
- [x] Color schemes: By Chain, By Residue Type, By B-Factor, Single Color
- [x] Residue 508 highlighting (red) + neighborhood highlighting (yellow)
- [x] Camera controls (Reset, Zoom Extents)
- [x] Atom size slider
- [x] Viewport info bar

---

## Phase 3 — Comparison & Mutation Analysis ✅

### Deliverables
- [x] IStructureComparisonService with full implementation
- [x] Simplified RMSD calculation (alpha-carbon pairing)
- [x] Missing residue detection
- [x] Residue-by-residue comparison table
- [x] ComparisonSummaryViewModel with metrics grid
- [x] MutationAnalysisViewModel with ΔF508 analysis
- [x] Mutation neighborhood inspection (reference vs mutant)
- [x] Folding impact assessment (contextual for F508del)
- [x] Side-by-side neighbor tables
- [x] Comparison tests (5 tests)

---

## Phase 4 — Annotations & Persistence ✅

### Deliverables
- [x] SQLite database schema (Annotations, AnalysisSessions, RecentFiles)
- [x] DatabaseInitializer with auto-create
- [x] SqliteAnnotationRepository (CRUD + filter by residue)
- [x] SqliteSessionPersistenceService
- [x] AnnotationListViewModel with add/delete/edit
- [x] Annotations tab in UI with form and list
- [x] Annotation persistence tests (5 tests)

---

## Phase 5 — Export, Pockets & Polish ✅

### Deliverables
- [x] ReportExportService — Markdown comparison reports
- [x] CSV annotation export
- [x] Screenshot export (PNG)
- [x] BindingPocketService — heuristic spatial clustering
- [x] BindingPocketViewModel with configurable parameters
- [x] Export tab in UI
- [x] Candidate Pockets tab in UI
- [x] Scientific dark theme (ScientificTheme.xaml)
- [x] Value converters (BoolToVisibility, NullToCollapsed, etc.)
- [x] Empty states, loading states, error states in UI
- [x] Status bar with persistent feedback
- [x] README with architecture, how-to-run, disclaimers
- [x] Professional scientific UX with docked panels

---

## Phase 6 — mRNA Therapy Designer ✅

### Stage 1: Codon Optimizer (Python FastAPI service)
- [x] Python service at `scripts/mrna_service` (FastAPI, uvicorn, port 8787)
- [x] CFTR protein sequence data (UniProt P13569, 1480 amino acids)
- [x] Complete codon table (64 codons, standard genetic code)
- [x] Human codon usage frequencies (Kazusa DB, Homo sapiens)
- [x] 8 scoring functions: CAI, GC%, CpG depletion, uridine reduction, rare codon avoidance, repeat avoidance, codon pair bias
- [x] NSGA-II multi-objective genetic algorithm (non-dominated sorting, crowding distance, Pareto front)
- [x] Population initialization (greedy optimal + frequency-weighted random)
- [x] Crossover (uniform at codon boundaries) and mutation (synonymous codon swap)
- [x] Convergence detection (stagnation limit)
- [x] Checkpoint persistence, Stage 1 top 10 log (`stage1_top10_candidates.json`)
- [x] 5' and 3' UTR libraries, nucleotide modification strategy, full construct assembly
- [x] FASTA export and Markdown report generation

### Stage 2: Phase 5 Advanced Rescoring
- [x] Phase 5 pipeline: 12 metrics (RNA structure, GC windows, motif risk, codon diversity)
- [x] Folding backends: ViennaRNACuda (RNAfold_simple.exe) → ViennaRNA (pip) → Nussinov fallback
- [x] Global folding capped at 600 nt; 3×400 nt windows for long sequences (avoids O(n³) blow-up)
- [x] Saturation dampening, diversity filter (Hamming-distance), weight presets
- [x] Phase 5 rescoring API (`/phase5/rescore`, `/phase5/progress`, `/phase5/results`)
- [x] Phase 5 tests (`tests/test_phase5.py`)

### WPF UI
- [x] "mRNA Therapy Pipeline" tab with sub-tabs: Stage 1 (Codon Optimizer), Stage 2 (Phase 5 Rescoring)
- [x] Stage 1: Configuration panel, progress, Pareto front, results table, candidate detail
- [x] Stage 2: Preset/Top N/Run ID config, Run Rescore, Log tab, All Candidates/Diverse Top-K DataGrids, comparison chart
- [x] Summary shows CDS length and Vienna folding windows (e.g. "4440 nt → 3×400 nt + 50+100 nt + 8×120 nt")
- [x] MrnaApiClient for optimization and Phase 5 endpoints

---

## Test Summary

| Category | Tests |
|----------|-------|
| PDB Parsing | 10 |
| Structure Comparison | 5 |
| Annotation Persistence | 5 |
| Codon Table | 9 |
| CFTR Sequence | 4 |
| Codon Scoring | 6 |
| RNA Folding | 4 |
| mRNA Candidates | 3 |
| Construct Design | 2 |
| UTR Library | 3 |
| **Total** | **54** |

All tests passing ✅

---

## File Structure

```
CftrMutationExplorer.slnx
├── src/
│   ├── CftrMutationExplorer.Core/
│   │   ├── Models/
│   │   │   ├── Atom.cs, Residue.cs, Chain.cs, ProteinStructure.cs
│   │   │   ├── Annotation.cs, AnalysisSession.cs
│   │   │   ├── StructureComparisonResult.cs, BindingPocketCandidate.cs
│   │   │   └── Mrna/
│   │   │       ├── CodonTable.cs           # 64 codons + human usage frequencies
│   │   │       ├── CftrSequence.cs         # CFTR protein (1480 aa) + domains + mutations
│   │   │       ├── MrnaCandidate.cs        # Candidate solution + multi-objective scores
│   │   │       ├── OptimizationConfig.cs   # GA parameters + objective weights
│   │   │       ├── OptimizationResult.cs   # Result + generation history + progress
│   │   │       ├── ConstructDesign.cs      # Full mRNA construct (cap/UTR/CDS/polyA)
│   │   │       └── UtrLibrary.cs           # 5'/3' UTR sequence library
│   │   └── Interfaces/
│   │       ├── IPdbParser.cs, IStructureComparisonService.cs
│   │       ├── IAnnotationRepository.cs, ISessionPersistenceService.cs
│   │       ├── IReportExportService.cs, IBindingPocketService.cs
│   │       ├── ICodonScoringService.cs     # 8 scoring functions
│   │       ├── IRnaFoldingService.cs       # RNA structure prediction
│   │       └── IMrnaOptimizationService.cs # Pipeline orchestration
│   ├── CftrMutationExplorer.Infrastructure/
│   │   ├── Parsing/PdbParser.cs
│   │   ├── Persistence/DatabaseInitializer.cs, Sqlite*.cs
│   │   └── Services/
│   │       ├── StructureComparisonService.cs, ReportExportService.cs, BindingPocketService.cs
│   │       └── Mrna/
│   │           ├── MrnaApiClient.cs              # HTTP client for Python mRNA service (Stage 1 + Phase 5)
│   │           ├── CodonScoringService.cs        # CAI, GC%, CpG, uridine, etc.
│   │           ├── RnaFoldingService.cs          # Nussinov-style DP (legacy; mRNA uses Python folding_backend)
│   │           ├── NsgaIIOptimizer.cs            # NSGA-II genetic algorithm
│   │           └── MrnaOptimizationService.cs    # Pipeline orchestrator
│   └── CftrMutationExplorer.App/
│       ├── App.xaml.cs (DI registration)
│       ├── MainWindow.xaml (+ mRNA Therapy tab)
│       ├── ViewModels/
│       │   ├── MainWindowViewModel.cs + existing VMs
│       │   ├── MrnaDesignerViewModel.cs    # Stage 1 UI orchestration
│       │   └── Phase5ViewModel.cs          # Stage 2 rescoring UI
│       ├── Views/
│       │   ├── ProteinViewport.xaml/.cs
│       │   ├── MrnaDesignerView.xaml/.cs   # Stage 1: config + progress + results
│       │   └── Phase5ResultsView.xaml/.cs # Stage 2: rescoring config + log + candidates
│       ├── Converters/BooleanConverters.cs
│       └── Themes/ScientificTheme.xaml
├── tests/
│   └── CftrMutationExplorer.Tests/
│       ├── PdbParserTests.cs, StructureComparisonTests.cs, AnnotationRepositoryTests.cs
│       └── MrnaOptimizationTests.cs        # 33 tests for mRNA pipeline
├── data/CFTR_Normal.pdb, CFTR_F508del.pdb
├── scripts/mrna_service/           # Python FastAPI service (Stage 1 + Phase 5)
│   ├── main.py                    # REST API, WebSocket
│   ├── optimizer.py               # NSGA-II codon optimizer
│   ├── folding_backend.py         # ViennaRNACuda, ViennaRNA, Nussinov
│   ├── phase5_scoring.py          # Phase 5 rescoring orchestrator
│   ├── phase5_metrics.py          # Structure, GC, motif, codon metrics
│   ├── phase5_config.py           # Presets, thresholds
│   ├── phase5_report.py           # JSON report generation
│   ├── motif_rules.py             # Homopolymer, repeat, forbidden motifs
│   └── tests/test_phase5.py
├── GAMEPLAN_MRNA_THERAPY.md
├── README.md
└── PROGRESS.md
```
