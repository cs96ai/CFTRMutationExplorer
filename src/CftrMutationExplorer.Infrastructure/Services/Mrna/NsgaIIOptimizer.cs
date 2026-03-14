using CftrMutationExplorer.Core.Interfaces;
using CftrMutationExplorer.Core.Models.Mrna;
using System.Diagnostics;

namespace CftrMutationExplorer.Infrastructure.Services.Mrna;

/// <summary>
/// NSGA-II (Non-dominated Sorting Genetic Algorithm II) for multi-objective
/// mRNA codon optimization.
///
/// Reference: Deb et al. (2002), IEEE Transactions on Evolutionary Computation.
///
/// The algorithm maintains a population of mRNA candidates, each differing only
/// in their synonymous codon choices. The protein sequence is invariant — every
/// candidate encodes the exact same CFTR protein.
/// </summary>
public class NsgaIIOptimizer
{
    private readonly ICodonScoringService _scoring;
    private readonly IRnaFoldingService _folding;
    private readonly Random _rng;

    public NsgaIIOptimizer(ICodonScoringService scoring, IRnaFoldingService folding, int? seed = null)
    {
        _scoring = scoring;
        _folding = folding;
        _rng = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public async Task<OptimizationResult> RunAsync(
        string proteinSequence,
        OptimizationConfig config,
        IProgress<OptimizationProgress>? progress = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var history = new List<GenerationStats>();

        int popSize = config.PopulationSize;
        int protLen = proteinSequence.Length;

        // Build lookup: for each position, what synonymous codons are available?
        var synonymousLookup = new string[protLen][];
        for (int i = 0; i < protLen; i++)
        {
            var aa = proteinSequence[i].ToString();
            synonymousLookup[i] = CodonTable.GetSynonymousCodons(aa);
        }

        // Initialize population
        var population = InitializePopulation(popSize, protLen, synonymousLookup, proteinSequence);

        // Score initial population
        _scoring.ScoreBatch(population, proteinSequence, config.Weights);
        ApplyFoldingScores(population, proteinSequence, config.Weights);

        int stagnationCounter = 0;
        double previousBestFitness = double.MinValue;

        for (int gen = 1; gen <= config.MaxGenerations; gen++)
        {
            ct.ThrowIfCancellationRequested();

            // Create offspring via crossover and mutation
            var offspring = new List<MrnaCandidate>(popSize);
            while (offspring.Count < popSize)
            {
                var parent1 = TournamentSelect(population, config.TournamentSize);
                var parent2 = TournamentSelect(population, config.TournamentSize);

                MrnaCandidate child1, child2;
                if (_rng.NextDouble() < config.CrossoverRate)
                    (child1, child2) = UniformCrossover(parent1, parent2, protLen);
                else
                    (child1, child2) = (parent1.Clone(), parent2.Clone());

                Mutate(child1, config.MutationRate, synonymousLookup);
                Mutate(child2, config.MutationRate, synonymousLookup);

                offspring.Add(child1);
                if (offspring.Count < popSize)
                    offspring.Add(child2);
            }

            // Score offspring
            _scoring.ScoreBatch(offspring, proteinSequence, config.Weights);

            // Combine parent + offspring
            var combined = new List<MrnaCandidate>(popSize * 2);
            combined.AddRange(population);
            combined.AddRange(offspring);

            // Non-dominated sorting
            var fronts = NonDominatedSort(combined);

            // Select next generation using NSGA-II: fill by front rank, then crowding distance
            population = SelectNextGeneration(fronts, popSize);

            // Apply folding scores periodically (expensive, so not every generation)
            if (gen % 25 == 0 || gen == config.MaxGenerations)
                ApplyFoldingScores(population, proteinSequence, config.Weights);

            // Record stats
            var stats = ComputeStats(population, gen, sw.Elapsed);
            history.Add(stats);

            // Convergence check
            if (Math.Abs(stats.BestCompositeFitness - previousBestFitness) < 1e-6)
                stagnationCounter++;
            else
                stagnationCounter = 0;
            previousBestFitness = stats.BestCompositeFitness;

            // Report progress
            if (progress != null && (gen % 5 == 0 || gen == 1 || gen == config.MaxGenerations))
            {
                double secsElapsed = sw.Elapsed.TotalSeconds;
                int seqPerSec = secsElapsed > 0 ? (int)(gen * popSize * 2 / secsElapsed) : 0;
                double secsPerGen = gen > 0 ? secsElapsed / gen : 1;
                var eta = TimeSpan.FromSeconds(secsPerGen * (config.MaxGenerations - gen));

                progress.Report(new OptimizationProgress
                {
                    CurrentGeneration = gen,
                    MaxGenerations = config.MaxGenerations,
                    CurrentStats = stats,
                    SequencesPerSecond = seqPerSec,
                    EstimatedTimeRemaining = eta,
                    StatusMessage = stagnationCounter > 0
                        ? $"Gen {gen}/{config.MaxGenerations} — stagnation {stagnationCounter}/{config.StagnationLimit}"
                        : $"Gen {gen}/{config.MaxGenerations} — best fitness {stats.BestCompositeFitness:F4}",
                });
            }

            // Early termination on convergence
            if (stagnationCounter >= config.StagnationLimit)
                break;

            // Yield control periodically for UI responsiveness
            if (gen % 10 == 0)
                await Task.Yield();
        }

        sw.Stop();

        // Final non-dominated sort to extract Pareto front
        var finalFronts = NonDominatedSort(population);
        var paretoFront = finalFronts.Count > 0 ? finalFronts[0] : new List<MrnaCandidate>();

        // Apply folding scores to Pareto front members
        ApplyFoldingScores(paretoFront, proteinSequence, config.Weights);

        var bestOverall = paretoFront.OrderByDescending(c => c.Score.CompositeFitness).FirstOrDefault()
                          ?? population.OrderByDescending(c => c.Score.CompositeFitness).First();

        return new OptimizationResult
        {
            ParetoFront = paretoFront.OrderByDescending(c => c.Score.CompositeFitness).ToList(),
            BestOverall = bestOverall,
            GenerationsRun = history.Count,
            TotalSequencesEvaluated = history.Count * config.PopulationSize * 2,
            ElapsedTime = sw.Elapsed,
            ConvergedEarly = stagnationCounter >= config.StagnationLimit,
            History = history,
            Config = config,
            ProteinSequence = proteinSequence,
        };
    }

    private List<MrnaCandidate> InitializePopulation(
        int popSize, int protLen, string[][] synonymousLookup, string proteinSequence)
    {
        var pop = new List<MrnaCandidate>(popSize);

        // First individual: use most-preferred codons (greedy optimum for CAI)
        var greedy = new MrnaCandidate(protLen);
        // Index 0 is always the most frequent codon (sorted descending in CodonTable.SynonymousCodons)
        pop.Add(greedy);

        // Second individual: back-translated with slight randomization
        var nearOptimal = new MrnaCandidate(protLen);
        for (int i = 0; i < protLen; i++)
        {
            int numSyn = synonymousLookup[i].Length;
            nearOptimal.Codons[i] = numSyn > 1 && _rng.NextDouble() < 0.1
                ? (byte)_rng.Next(numSyn)
                : (byte)0;
        }
        pop.Add(nearOptimal);

        // Rest: random codon choices weighted by human usage frequency
        for (int p = 2; p < popSize; p++)
        {
            var candidate = new MrnaCandidate(protLen);
            for (int i = 0; i < protLen; i++)
            {
                int numSyn = synonymousLookup[i].Length;
                if (numSyn <= 1)
                {
                    candidate.Codons[i] = 0;
                    continue;
                }
                candidate.Codons[i] = (byte)WeightedCodonSelect(synonymousLookup[i]);
            }
            pop.Add(candidate);
        }

        return pop;
    }

    private int WeightedCodonSelect(string[] codons)
    {
        double totalFreq = 0;
        Span<double> freqs = stackalloc double[codons.Length];
        for (int i = 0; i < codons.Length; i++)
        {
            freqs[i] = CodonTable.HumanFrequencyPerThousand.GetValueOrDefault(codons[i], 1.0);
            totalFreq += freqs[i];
        }

        double r = _rng.NextDouble() * totalFreq;
        double cumulative = 0;
        for (int i = 0; i < codons.Length; i++)
        {
            cumulative += freqs[i];
            if (r <= cumulative) return i;
        }
        return codons.Length - 1;
    }

    private MrnaCandidate TournamentSelect(List<MrnaCandidate> population, int tournamentSize)
    {
        MrnaCandidate? best = null;
        for (int i = 0; i < tournamentSize; i++)
        {
            var candidate = population[_rng.Next(population.Count)];
            if (best == null || candidate.Rank < best.Rank ||
                (candidate.Rank == best.Rank && candidate.CrowdingDistance > best.CrowdingDistance))
            {
                best = candidate;
            }
        }
        return best!;
    }

    private (MrnaCandidate, MrnaCandidate) UniformCrossover(
        MrnaCandidate parent1, MrnaCandidate parent2, int length)
    {
        var child1 = new MrnaCandidate(length);
        var child2 = new MrnaCandidate(length);

        for (int i = 0; i < length; i++)
        {
            if (_rng.NextDouble() < 0.5)
            {
                child1.Codons[i] = parent1.Codons[i];
                child2.Codons[i] = parent2.Codons[i];
            }
            else
            {
                child1.Codons[i] = parent2.Codons[i];
                child2.Codons[i] = parent1.Codons[i];
            }
        }

        return (child1, child2);
    }

    private void Mutate(MrnaCandidate candidate, double mutationRate, string[][] synonymousLookup)
    {
        for (int i = 0; i < candidate.Codons.Length; i++)
        {
            if (_rng.NextDouble() < mutationRate)
            {
                int numSyn = synonymousLookup[i].Length;
                if (numSyn > 1)
                    candidate.Codons[i] = (byte)_rng.Next(numSyn);
            }
        }
    }

    /// <summary>
    /// NSGA-II non-dominated sorting. Assigns each individual to a Pareto front (rank 0, 1, 2, ...).
    /// </summary>
    private List<List<MrnaCandidate>> NonDominatedSort(List<MrnaCandidate> population)
    {
        int n = population.Count;
        var dominationCount = new int[n];       // how many solutions dominate this one
        var dominatedSet = new List<int>[n];     // indices this solution dominates
        var fronts = new List<List<MrnaCandidate>>();
        var frontIndices = new List<List<int>>();

        for (int i = 0; i < n; i++)
            dominatedSet[i] = new List<int>();

        var firstFront = new List<int>();

        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (population[i].Dominates(population[j]))
                {
                    dominatedSet[i].Add(j);
                    dominationCount[j]++;
                }
                else if (population[j].Dominates(population[i]))
                {
                    dominatedSet[j].Add(i);
                    dominationCount[i]++;
                }
            }

            if (dominationCount[i] == 0)
            {
                population[i].Rank = 0;
                firstFront.Add(i);
            }
        }

        frontIndices.Add(firstFront);
        fronts.Add(firstFront.Select(i => population[i]).ToList());

        int rank = 0;
        while (frontIndices[rank].Count > 0)
        {
            var nextFront = new List<int>();
            foreach (int i in frontIndices[rank])
            {
                foreach (int j in dominatedSet[i])
                {
                    dominationCount[j]--;
                    if (dominationCount[j] == 0)
                    {
                        population[j].Rank = rank + 1;
                        nextFront.Add(j);
                    }
                }
            }

            if (nextFront.Count == 0) break;

            rank++;
            frontIndices.Add(nextFront);
            fronts.Add(nextFront.Select(i => population[i]).ToList());
        }

        return fronts;
    }

    /// <summary>
    /// Assign crowding distance to individuals within a front.
    /// </summary>
    private void AssignCrowdingDistance(List<MrnaCandidate> front)
    {
        int n = front.Count;
        if (n <= 2)
        {
            foreach (var c in front)
                c.CrowdingDistance = double.MaxValue;
            return;
        }

        foreach (var c in front)
            c.CrowdingDistance = 0;

        int numObjectives = front[0].Score.ToObjectiveArray().Length;

        for (int m = 0; m < numObjectives; m++)
        {
            var sorted = front.OrderBy(c => c.Score.ToObjectiveArray()[m]).ToList();
            sorted[0].CrowdingDistance = double.MaxValue;
            sorted[n - 1].CrowdingDistance = double.MaxValue;

            double objMin = sorted[0].Score.ToObjectiveArray()[m];
            double objMax = sorted[n - 1].Score.ToObjectiveArray()[m];
            double range = objMax - objMin;
            if (range < 1e-10) continue;

            for (int i = 1; i < n - 1; i++)
            {
                double dist = (sorted[i + 1].Score.ToObjectiveArray()[m] -
                              sorted[i - 1].Score.ToObjectiveArray()[m]) / range;
                sorted[i].CrowdingDistance += dist;
            }
        }
    }

    private List<MrnaCandidate> SelectNextGeneration(List<List<MrnaCandidate>> fronts, int popSize)
    {
        var nextGen = new List<MrnaCandidate>(popSize);

        foreach (var front in fronts)
        {
            AssignCrowdingDistance(front);

            if (nextGen.Count + front.Count <= popSize)
            {
                nextGen.AddRange(front);
            }
            else
            {
                int remaining = popSize - nextGen.Count;
                var sorted = front.OrderByDescending(c => c.CrowdingDistance).Take(remaining);
                nextGen.AddRange(sorted);
                break;
            }
        }

        return nextGen;
    }

    private void ApplyFoldingScores(IList<MrnaCandidate> candidates, string proteinSequence, ObjectiveWeights weights)
    {
        Parallel.ForEach(candidates, candidate =>
        {
            var rna = candidate.ToRnaSequence(proteinSequence);
            candidate.Score.FoldingScore = _folding.ScoreFivePrimeFolding(rna, 80);

            // Recompute composite with folding score
            var w = weights.ToArray();
            var obj = candidate.Score.ToObjectiveArray();
            double weightedSum = 0, totalWeight = 0;
            for (int i = 0; i < obj.Length; i++)
            {
                weightedSum += obj[i] * w[i];
                totalWeight += w[i];
            }
            candidate.Score.CompositeFitness = totalWeight > 0 ? weightedSum / totalWeight : 0;
        });
    }

    private GenerationStats ComputeStats(List<MrnaCandidate> population, int generation, TimeSpan elapsed)
    {
        var fitnesses = population.Select(c => c.Score.CompositeFitness).ToList();
        var paretoFront = population.Where(c => c.Rank == 0).ToList();

        double diversity = ComputeDiversity(population);

        return new GenerationStats
        {
            Generation = generation,
            BestCompositeFitness = fitnesses.Max(),
            AverageCompositeFitness = fitnesses.Average(),
            WorstCompositeFitness = fitnesses.Min(),
            ParetoFrontSize = paretoFront.Count,
            PopulationDiversity = diversity,
            BestCai = population.Max(c => c.Score.Cai),
            BestGcScore = population.Max(c => c.Score.GcContentScore),
            BestCpgScore = population.Max(c => c.Score.CpgScore),
            BestUridineScore = population.Max(c => c.Score.UridineScore),
            Elapsed = elapsed,
        };
    }

    private double ComputeDiversity(List<MrnaCandidate> population)
    {
        if (population.Count < 2) return 0;

        // Sample diversity: average Hamming distance between random pairs
        int sampleSize = Math.Min(50, population.Count);
        double totalDist = 0;
        int comparisons = 0;

        for (int i = 0; i < sampleSize; i++)
        {
            var a = population[_rng.Next(population.Count)];
            var b = population[_rng.Next(population.Count)];
            int diff = 0;
            for (int j = 0; j < a.Codons.Length; j++)
                if (a.Codons[j] != b.Codons[j]) diff++;
            totalDist += (double)diff / a.Codons.Length;
            comparisons++;
        }

        return comparisons > 0 ? totalDist / comparisons : 0;
    }
}
