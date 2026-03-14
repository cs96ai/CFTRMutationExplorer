namespace CftrMutationExplorer.Core.Interfaces;

/// <summary>
/// Predicts RNA secondary structure and minimum free energy (MFE).
/// </summary>
public interface IRnaFoldingService
{
    /// <summary>
    /// Predict the minimum free energy of an RNA sequence's secondary structure.
    /// More negative = more stable structure.
    /// </summary>
    double PredictMfe(string rnaSequence);

    /// <summary>
    /// Score the 5' region folding for translation initiation.
    /// Returns a score in [0, 1] where 1 = low structure (good for translation).
    /// </summary>
    double ScoreFivePrimeFolding(string fullRnaSequence, int windowSize = 100);

    /// <summary>
    /// Calculate local folding energy along the sequence using a sliding window.
    /// Returns MFE values at each window position.
    /// </summary>
    List<double> SlidingWindowMfe(string rnaSequence, int windowSize = 50, int stepSize = 10);
}
