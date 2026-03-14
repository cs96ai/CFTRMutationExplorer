using CftrMutationExplorer.Core.Interfaces;

namespace CftrMutationExplorer.Infrastructure.Services.Mrna;

/// <summary>
/// Simplified RNA secondary structure prediction using a Nussinov-style dynamic programming
/// algorithm with stacking energy approximations. For production use, ViennaRNA is recommended.
///
/// This implementation predicts the minimum free energy (MFE) based on base-pairing rules:
///   AU: -2.0 kcal/mol, GC: -3.0 kcal/mol, GU: -1.0 kcal/mol (wobble pair)
///   Stacking bonus: -0.5 kcal/mol for consecutive base pairs
///
/// The algorithm runs in O(n³) time and O(n²) space, suitable for sequences up to ~500 nt.
/// For full-length CDS (~4400 nt), only the 5' region and sliding windows are folded.
/// </summary>
public class RnaFoldingService : IRnaFoldingService
{
    private const double AuEnergy = -2.0;
    private const double GcEnergy = -3.0;
    private const double GuEnergy = -1.0;
    private const double StackBonus = -0.5;
    private const int MinLoopSize = 3;

    public double PredictMfe(string rnaSequence)
    {
        if (rnaSequence.Length < MinLoopSize + 2) return 0;

        int n = rnaSequence.Length;
        var dp = new double[n, n]; // dp[i,j] = MFE for subsequence i..j

        for (int len = MinLoopSize + 2; len <= n; len++)
        {
            for (int i = 0; i <= n - len; i++)
            {
                int j = i + len - 1;

                // Case 1: i is unpaired
                dp[i, j] = dp[i + 1, j];

                // Case 2: j is unpaired
                dp[i, j] = Math.Min(dp[i, j], dp[i, j - 1]);

                // Case 3: i pairs with j
                double pairEnergy = GetPairEnergy(rnaSequence[i], rnaSequence[j]);
                if (pairEnergy < 0 && j - i > MinLoopSize)
                {
                    double inner = (i + 1 <= j - 1) ? dp[i + 1, j - 1] : 0;
                    double stackBonus = 0;
                    if (i + 1 < j - 1 && GetPairEnergy(rnaSequence[i + 1], rnaSequence[j - 1]) < 0)
                        stackBonus = StackBonus;

                    dp[i, j] = Math.Min(dp[i, j], pairEnergy + stackBonus + inner);
                }

                // Case 4: bifurcation — split into two substructures
                for (int k = i + 1; k < j; k++)
                {
                    dp[i, j] = Math.Min(dp[i, j], dp[i, k] + dp[k + 1, j]);
                }
            }
        }

        return dp[0, n - 1];
    }

    public double ScoreFivePrimeFolding(string fullRnaSequence, int windowSize = 100)
    {
        if (fullRnaSequence.Length < 10) return 1.0;

        int len = Math.Min(windowSize, fullRnaSequence.Length);
        var fivePrime = fullRnaSequence[..len];
        double mfe = PredictMfe(fivePrime);

        // Strong structure near start codon hurts translation initiation.
        // MFE is negative; more negative = more structure = worse for translation.
        // Score: 1.0 if MFE > -10, 0.0 if MFE < -40
        if (mfe >= -10) return 1.0;
        if (mfe <= -40) return 0.0;
        return 1.0 - (Math.Abs(mfe) - 10.0) / 30.0;
    }

    public List<double> SlidingWindowMfe(string rnaSequence, int windowSize = 50, int stepSize = 10)
    {
        var results = new List<double>();
        for (int start = 0; start + windowSize <= rnaSequence.Length; start += stepSize)
        {
            var window = rnaSequence.Substring(start, windowSize);
            results.Add(PredictMfe(window));
        }
        return results;
    }

    private static double GetPairEnergy(char a, char b)
    {
        return (a, b) switch
        {
            ('A', 'U') or ('U', 'A') => AuEnergy,
            ('G', 'C') or ('C', 'G') => GcEnergy,
            ('G', 'U') or ('U', 'G') => GuEnergy,
            _ => 0, // no pair
        };
    }
}
