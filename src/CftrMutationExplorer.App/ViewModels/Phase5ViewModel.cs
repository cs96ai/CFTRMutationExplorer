using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Infrastructure.Services.Mrna;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Windows.Threading;

namespace CftrMutationExplorer.App.ViewModels;

public partial class Phase5ViewModel : ObservableObject
{
    private readonly MrnaApiClient _apiClient;
    private readonly Dispatcher _dispatcher;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    // Config
    [ObservableProperty] private string _selectedPreset = "phase5_balanced";
    [ObservableProperty] private int _topN = 50;
    [ObservableProperty] private string? _runId;

    // State
    [ObservableProperty] private bool _isRescoring;
    [ObservableProperty] private bool _hasResults;
    [ObservableProperty] private string _statusMessage = "Configure and run Phase 5 rescoring.";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private int _progressPercent;
    [ObservableProperty] private string _rescoreLog = "";

    // Results
    public ObservableCollection<Phase5CandidateRow> AllCandidates { get; } = new();
    public ObservableCollection<Phase5CandidateRow> DiverseCandidates { get; } = new();
    public ObservableCollection<string> AvailablePresets { get; } = new();

    [ObservableProperty] private Phase5CandidateRow? _selectedCandidate;
    [ObservableProperty] private string _selectedCandidateDetails = "";

    // Charts
    [ObservableProperty] private PlotModel _comparisonChart = new();

    public Phase5ViewModel(MrnaApiClient apiClient)
    {
        _apiClient = apiClient;
        _dispatcher = Dispatcher.CurrentDispatcher;
        // Ensure preset dropdown is never empty (API may not be running yet)
        AvailablePresets.Add("phase5_balanced");
        AvailablePresets.Add("translation_heavy");
        AvailablePresets.Add("immune_stealth_heavy");
        AvailablePresets.Add("manufacturability_heavy");
        AvailablePresets.Add("structure_heavy");
        InitializeChart();
        _ = LoadPresetsAsync();
    }

    private void InitializeChart()
    {
        var model = new PlotModel
        {
            Title = "Legacy vs Phase 5 Composite",
            Background = OxyColor.FromRgb(30, 30, 40),
            PlotAreaBorderColor = OxyColor.FromRgb(80, 80, 100),
            TextColor = OxyColor.FromRgb(200, 200, 210),
            TitleColor = OxyColor.FromRgb(200, 200, 210),
        };

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Title = "Legacy Composite",
            Key = "x",
            Minimum = 0, Maximum = 1,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(60, 60, 80),
            AxislineColor = OxyColor.FromRgb(100, 100, 120),
            TextColor = OxyColor.FromRgb(180, 180, 200),
            TitleColor = OxyColor.FromRgb(180, 180, 200),
        });

        model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Title = "Phase 5 Composite",
            Key = "y",
            Minimum = 0, Maximum = 1,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(60, 60, 80),
            AxislineColor = OxyColor.FromRgb(100, 100, 120),
            TextColor = OxyColor.FromRgb(180, 180, 200),
            TitleColor = OxyColor.FromRgb(180, 180, 200),
        });

        var scatter = new ScatterSeries
        {
            Title = "Candidates",
            MarkerType = MarkerType.Circle,
            MarkerSize = 5,
            MarkerFill = OxyColor.FromRgb(100, 149, 237),
            MarkerStroke = OxyColor.FromRgb(65, 105, 225),
            MarkerStrokeThickness = 1,
            XAxisKey = "x",
            YAxisKey = "y",
            TrackerFormatString = "#{Tag}\nLegacy: {2:0.0000}\nPhase5: {4:0.0000}",
        };
        model.Series.Add(scatter);

        // Diagonal reference line (y = x)
        var diag = new LineSeries
        {
            Title = "No Change Line",
            Color = OxyColor.FromArgb(100, 255, 255, 255),
            StrokeThickness = 1,
            LineStyle = LineStyle.Dash,
            XAxisKey = "x",
            YAxisKey = "y",
        };
        diag.Points.Add(new DataPoint(0, 0));
        diag.Points.Add(new DataPoint(1, 1));
        model.Series.Add(diag);

        // Selected highlight
        var highlight = new ScatterSeries
        {
            Title = "Selected",
            MarkerType = MarkerType.Circle,
            MarkerSize = 10,
            MarkerFill = OxyColor.FromArgb(80, 255, 0, 0),
            MarkerStroke = OxyColor.FromRgb(255, 50, 50),
            MarkerStrokeThickness = 2,
            XAxisKey = "x",
            YAxisKey = "y",
        };
        model.Series.Add(highlight);

        ComparisonChart = model;
    }

    private async Task LoadPresetsAsync()
    {
        try
        {
            var resp = await _apiClient.GetPhase5Presets();
            if (resp?.Presets != null && resp.Presets.Count > 0)
            {
                await _dispatcher.InvokeAsync(() =>
                {
                    AvailablePresets.Clear();
                    foreach (var name in resp.Presets.Keys)
                        AvailablePresets.Add(name);
                    if (!AvailablePresets.Contains(SelectedPreset))
                        SelectedPreset = AvailablePresets.FirstOrDefault() ?? "phase5_balanced";
                });
            }
        }
        catch
        {
            // Keep defaults already set in constructor; API unavailable
        }
    }

    [RelayCommand]
    private async Task RescoreCandidates()
    {
        if (IsRescoring) return;

        IsRescoring = true;
        StatusMessage = "Starting Phase 5 rescoring...";
        ProgressText = "";
        ProgressPercent = 0;
        HasResults = false;
        RescoreLog = "";

        try
        {
            var request = new ApiPhase5RescoreRequest
            {
                RunId = string.IsNullOrWhiteSpace(RunId) ? null : RunId,
                TopN = TopN,
                Preset = SelectedPreset,
            };

            var reqLog = $"Request: run_id={RunId ?? "(latest)"}, top_n={TopN}, preset={SelectedPreset}";
            RescoreLog = reqLog;
            await _apiClient.StartPhase5Rescore(request);
            StatusMessage = "Phase 5 rescoring in progress...";

            // Poll for progress
            while (true)
            {
                await Task.Delay(800);
                var progress = await _apiClient.GetPhase5Progress();
                if (progress == null) continue;

                await _dispatcher.InvokeAsync(() =>
                {
                    if (progress.Total > 0)
                    {
                        ProgressPercent = (int)(100.0 * progress.Done / progress.Total);
                        ProgressText = $"Rescoring... {progress.Done}/{progress.Total}";
                    }
                    if (progress.Log != null && progress.Log.Count > 0)
                    {
                        var apiLog = string.Join(Environment.NewLine, progress.Log);
                        RescoreLog = reqLog + Environment.NewLine + apiLog;
                    }
                });

                if (!progress.Running)
                {
                    if (!string.IsNullOrEmpty(progress.Error))
                    {
                        StatusMessage = $"Phase 5 rescore failed: {progress.Error}";
                        return;
                    }
                    break;
                }
            }

            // Fetch final results
            var result = await _apiClient.GetPhase5Results();
            if (result == null)
            {
                StatusMessage = "Phase 5 rescoring returned no results.";
                return;
            }

            await _dispatcher.InvokeAsync(() => ProcessResults(result.Value));
            StatusMessage = $"Phase 5 rescoring complete. {AllCandidates.Count} candidates rescored.";
            ProgressText = "";
            ProgressPercent = 100;
            HasResults = true;
        }
        catch (HttpRequestException ex)
        {
            StatusMessage = $"Phase 5 rescore failed: {ex.Message}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsRescoring = false;
            ProgressText = "";
        }
    }

    private void ProcessResults(JsonElement root)
    {
        AllCandidates.Clear();
        DiverseCandidates.Clear();

        if (root.TryGetProperty("candidates_all", out var allJson))
        {
            foreach (var item in allJson.EnumerateArray())
            {
                var row = ParseCandidateRow(item);
                if (row != null) AllCandidates.Add(row);
            }
        }

        if (root.TryGetProperty("candidates_diverse_topk", out var divJson))
        {
            foreach (var item in divJson.EnumerateArray())
            {
                var row = ParseCandidateRow(item);
                if (row != null) DiverseCandidates.Add(row);
            }
        }

        if (root.TryGetProperty("summary", out var summaryJson))
        {
            var lines = new System.Text.StringBuilder();
            if (summaryJson.TryGetProperty("sequence_length_nt", out var sl))
                lines.AppendLine($"CDS length: {sl.GetInt32()} nt");
            if (summaryJson.TryGetProperty("folding_summary", out var fs))
                lines.AppendLine($"Vienna folding: {fs.GetString()}");
            if (summaryJson.TryGetProperty("total_candidates", out var tc))
                lines.AppendLine($"Total candidates: {tc.GetInt32()}");
            if (summaryJson.TryGetProperty("diverse_candidates", out var dc))
                lines.AppendLine($"Diverse top-K: {dc.GetInt32()}");
            if (summaryJson.TryGetProperty("top_candidate_id", out var tid))
                lines.AppendLine($"Top candidate: {tid.GetString()}");
            if (summaryJson.TryGetProperty("moved_up_count", out var mu))
                lines.AppendLine($"Moved up: {mu.GetInt32()}");
            if (summaryJson.TryGetProperty("moved_down_count", out var md))
                lines.AppendLine($"Moved down: {md.GetInt32()}");
            if (summaryJson.TryGetProperty("avg_composite_phase5", out var avg))
                lines.AppendLine($"Avg Phase5 composite: {avg.GetDouble():F4}");
            SummaryText = lines.ToString();
        }

        UpdateChart();
    }

    private Phase5CandidateRow? ParseCandidateRow(JsonElement item)
    {
        try
        {
            return new Phase5CandidateRow
            {
                Id = item.GetProperty("id").GetString() ?? "",
                RankPhase5 = item.GetProperty("rank_phase5").GetInt32(),
                RankLegacy = item.GetProperty("rank_legacy").GetInt32(),
                RankChange = item.GetProperty("rank_change").GetInt32(),
                CompositeLegacy = item.GetProperty("composite_legacy").GetDouble(),
                CompositePhase5 = item.GetProperty("composite_phase5").GetDouble(),
                Cai = GetMetric(item, "legacy_metrics", "cai"),
                GcScore = GetMetric(item, "legacy_metrics", "gc_score"),
                CpgScore = GetMetric(item, "legacy_metrics", "cpg_score"),
                Structure5Prime = GetMetric(item, "phase5_metrics", "structure_5prime"),
                GcWindow30 = GetMetric(item, "phase5_metrics", "gc_window_30"),
                CodonDiversity = GetMetric(item, "phase5_metrics", "codon_diversity"),
                MotifRisk = GetMetric(item, "phase5_metrics", "motif_risk"),
                Homopolymer = GetMetric(item, "phase5_metrics", "homopolymer"),
                Explanation = item.GetProperty("explanation").GetString() ?? "",
                Warnings = item.TryGetProperty("warnings", out var w)
                    ? string.Join("; ", w.EnumerateArray().Select(x => x.GetString()))
                    : "",
                AllLegacyMetrics = ExtractMetricsDict(item, "legacy_metrics"),
                AllPhase5Metrics = ExtractMetricsDict(item, "phase5_metrics"),
            };
        }
        catch { return null; }
    }

    private static double GetMetric(JsonElement item, string group, string key)
    {
        if (item.TryGetProperty(group, out var g) && g.TryGetProperty(key, out var v))
            return v.GetDouble();
        return 0;
    }

    private static Dictionary<string, double> ExtractMetricsDict(JsonElement item, string group)
    {
        var dict = new Dictionary<string, double>();
        if (item.TryGetProperty(group, out var g) && g.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in g.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.Number)
                    dict[prop.Name] = prop.Value.GetDouble();
            }
        }
        return dict;
    }

    private void UpdateChart()
    {
        var scatter = ComparisonChart.Series.OfType<ScatterSeries>().FirstOrDefault(s => s.Title == "Candidates");
        if (scatter == null) return;
        scatter.Points.Clear();

        foreach (var c in AllCandidates)
        {
            scatter.Points.Add(new ScatterPoint(c.CompositeLegacy, c.CompositePhase5, tag: c.Id));
        }

        var highlight = ComparisonChart.Series.OfType<ScatterSeries>().FirstOrDefault(s => s.Title == "Selected");
        if (highlight != null) highlight.Points.Clear();

        try { ComparisonChart.InvalidatePlot(true); } catch { }
    }

    partial void OnSelectedCandidateChanged(Phase5CandidateRow? value)
    {
        if (value == null)
        {
            SelectedCandidateDetails = "";
            var hl = ComparisonChart.Series.OfType<ScatterSeries>().FirstOrDefault(s => s.Title == "Selected");
            if (hl != null) { hl.Points.Clear(); try { ComparisonChart.InvalidatePlot(true); } catch { } }
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Candidate: {value.Id}");
        sb.AppendLine($"Phase 5 Rank: #{value.RankPhase5} (was #{value.RankLegacy}, {(value.RankChange > 0 ? "+" : "")}{value.RankChange})");
        sb.AppendLine();
        sb.AppendLine("=== Legacy Metrics ===");
        foreach (var kv in value.AllLegacyMetrics)
            sb.AppendLine($"  {kv.Key}: {kv.Value:F4}");
        sb.AppendLine($"  Composite: {value.CompositeLegacy:F4}");
        sb.AppendLine();
        sb.AppendLine("=== Phase 5 Metrics ===");
        foreach (var kv in value.AllPhase5Metrics)
            sb.AppendLine($"  {kv.Key}: {kv.Value:F4}");
        sb.AppendLine($"  Composite: {value.CompositePhase5:F4}");
        sb.AppendLine();
        sb.AppendLine("=== Explanation ===");
        sb.AppendLine(value.Explanation);
        if (!string.IsNullOrEmpty(value.Warnings))
        {
            sb.AppendLine();
            sb.AppendLine($"Warnings: {value.Warnings}");
        }
        SelectedCandidateDetails = sb.ToString();

        // Highlight on chart
        var highlight = ComparisonChart.Series.OfType<ScatterSeries>().FirstOrDefault(s => s.Title == "Selected");
        if (highlight != null)
        {
            highlight.Points.Clear();
            highlight.Points.Add(new ScatterPoint(value.CompositeLegacy, value.CompositePhase5));
            try { ComparisonChart.InvalidatePlot(true); } catch { }
        }
    }
}

public class Phase5CandidateRow
{
    public string Id { get; set; } = "";
    public int RankPhase5 { get; set; }
    public int RankLegacy { get; set; }
    public int RankChange { get; set; }
    public double CompositeLegacy { get; set; }
    public double CompositePhase5 { get; set; }
    public double Cai { get; set; }
    public double GcScore { get; set; }
    public double CpgScore { get; set; }
    public double Structure5Prime { get; set; }
    public double GcWindow30 { get; set; }
    public double CodonDiversity { get; set; }
    public double MotifRisk { get; set; }
    public double Homopolymer { get; set; }
    public string Explanation { get; set; } = "";
    public string Warnings { get; set; } = "";
    public Dictionary<string, double> AllLegacyMetrics { get; set; } = new();
    public Dictionary<string, double> AllPhase5Metrics { get; set; } = new();
}
