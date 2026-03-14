using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CftrMutationExplorer.Core.Models.Mrna;
using CftrMutationExplorer.Infrastructure.Services.Mrna;
using OxyPlot;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Windows.Threading;

namespace CftrMutationExplorer.App.ViewModels;

public partial class MrnaDesignerViewModel : ObservableObject
{
    private readonly PythonServiceManager _serviceManager;
    private readonly MrnaApiClient _apiClient;
    private readonly Dispatcher _dispatcher;
    private bool _wasOptimizing;
    private CancellationTokenSource? _completionPollCts;

    [ObservableProperty] private string _proteinSequence = string.Empty;
    [ObservableProperty] private int _proteinLength;
    [ObservableProperty] private int _cdsLengthNt;
    [ObservableProperty] private string _proteinSummary = string.Empty;

    // Config (champion params from hyperparameter search)
    [ObservableProperty] private int _populationSize = 750;
    [ObservableProperty] private double _crossoverRate = 0.75;
    [ObservableProperty] private double _mutationRate = 0.03;
    [ObservableProperty] private double _fitnessThreshold = 0.90;
    [ObservableProperty] private int _checkpointInterval = 100;
    [ObservableProperty] private int _tournamentSize = 2;

    [ObservableProperty] private double _weightCai = 1.0;
    [ObservableProperty] private double _weightGc = 0.9;
    [ObservableProperty] private double _weightCpg = 0.9;
    [ObservableProperty] private double _weightUridine = 0.7;
    [ObservableProperty] private double _weightRareCodons = 0.6;
    [ObservableProperty] private double _weightRepeats = 0.5;
    [ObservableProperty] private double _weightCodonPair = 0.4;

    [ObservableProperty] private int _selectedFiveUtrIndex;
    [ObservableProperty] private int _selectedThreeUtrIndex;
    [ObservableProperty] private int _polyALength = 120;
    [ObservableProperty] private bool _useM1Pseudouridine = true;

    // State
    [ObservableProperty] private bool _isOptimizing;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _isSequenceLoaded;
    [ObservableProperty] private string _statusMessage = "Load the CFTR sequence to begin.";
    [ObservableProperty] private string _progressText = string.Empty;
    [ObservableProperty] private int _currentGeneration;
    [ObservableProperty] private int _sequencesPerSecond;
    [ObservableProperty] private int _paretoFrontSize;
    [ObservableProperty] private double _bestFitness;
    [ObservableProperty] private double _avgFitness;
    [ObservableProperty] private string _serviceStatus = "Not connected";
    [ObservableProperty] private bool _isServiceConnected;
    [ObservableProperty] private bool _thresholdReached;
    [ObservableProperty] private string _gpuInfo = string.Empty;
    [ObservableProperty] private string _runId = string.Empty;

    // Results
    [ObservableProperty] private ObservableCollection<CandidateDisplayItem> _paretoFrontItems = new();
    [ObservableProperty] private CandidateDisplayItem? _selectedCandidate;
    [ObservableProperty] private string _selectedCandidateDetail = string.Empty;

    // Previous runs
    [ObservableProperty] private ObservableCollection<RunSummaryItem> _previousRuns = new();
    [ObservableProperty] private RunSummaryItem? _selectedPreviousRun;

    // Debug log
    [ObservableProperty] private string _debugLog = string.Empty;
    private string _lastLogTimestamp = string.Empty;
    private readonly object _logLock = new();

    // Charts
    [ObservableProperty] private PlotModel _convergencePlot = new();
    [ObservableProperty] private PlotModel _paretoPlot = new();

    // UTR options
    public ObservableCollection<string> FiveUtrOptions { get; } = new()
    {
        "HBA1 (α-globin)", "HBB (β-globin)", "Minimal Kozak", "TEV Leader", "Optimized Synthetic v1"
    };
    public ObservableCollection<string> ThreeUtrOptions { get; } = new()
    {
        "HBA1 (α-globin)", "HBB (β-globin)", "AES-mtRNR1 Tandem", "Minimal poly(A) signal"
    };

    public MrnaDesignerViewModel(PythonServiceManager serviceManager, MrnaApiClient apiClient)
    {
        _serviceManager = serviceManager;
        _apiClient = apiClient;
        _dispatcher = Dispatcher.CurrentDispatcher;
        InitializeCharts();

        _apiClient.StatusReceived += OnWsStatusReceived;
        _apiClient.LogReceived += OnWsLogReceived;
        _apiClient.WebSocketConnected += () => _dispatcher.BeginInvoke(() =>
            AppendLog("CLIENT", "WebSocket connected — live streaming active"));
        _apiClient.WebSocketDisconnected += () => _dispatcher.BeginInvoke(() =>
            AppendLog("WARN", "WebSocket disconnected"));
        _apiClient.WebSocketError += msg => _dispatcher.BeginInvoke(() =>
            AppendLog("ERROR", msg));

        AppendLog("CLIENT", "WPF mRNA Designer initialized");
        _ = CheckServiceConnectionAsync();
    }

    private void AppendLog(string level, string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss.fff");
        var line = $"[{timestamp}] [{level}] {message}\n";
        lock (_logLock)
        {
            DebugLog += line;
        }
    }

    [RelayCommand]
    private void LoadCftrSequence()
    {
        ProteinSequence = CftrSequence.ProteinSequence;
        ProteinLength = ProteinSequence.Length;
        CdsLengthNt = ProteinSequence.Length * 3;
        IsSequenceLoaded = true;

        var domain508 = CftrSequence.GetDomain(508);
        ProteinSummary = $"{ProteinLength} amino acids | {CdsLengthNt} nt CDS | " +
                         $"{CftrSequence.Domains.Count} domains | " +
                         $"F508 in {domain508?.Name ?? "?"} | " +
                         $"Search space: ~3^{ProteinLength} ≈ 10^{(int)(ProteinLength * Math.Log10(3))} sequences";

        StatusMessage = "CFTR sequence loaded. Start the Python GPU service and optimize.";
    }

    [RelayCommand]
    private async Task StartService()
    {
        try
        {
            AppendLog("CLIENT", "Starting Python GPU service...");
            StatusMessage = "Starting Python GPU optimization service...";
            ServiceStatus = "Starting...";
            await _serviceManager.StartAsync();
            IsServiceConnected = true;
            ServiceStatus = "Connected (GPU)";
            StatusMessage = "Python service started. Ready to optimize.";
            AppendLog("CLIENT", $"Service connected at {_serviceManager.BaseUrl}");
            await ConnectWebSocket();
            await LoadPreviousRunsAsync();
            await ReconnectIfRunning();
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Failed to start service: {ex.Message}");
            StatusMessage = $"Failed to start service: {ex.Message}";
            ServiceStatus = "Failed";
            IsServiceConnected = false;
        }
    }

    [RelayCommand]
    private async Task StopService()
    {
        try
        {
            AppendLog("CLIENT", "Stopping Python GPU service...");
            StatusMessage = "Stopping service...";
            ServiceStatus = "Stopping...";
            IsOptimizing = false;
            _wasOptimizing = false;

            await _apiClient.DisconnectWebSocketAsync();

            await Task.Run(() => _serviceManager.ForceStop());

            IsServiceConnected = false;
            ServiceStatus = "Stopped";
            StatusMessage = "Python service stopped.";
            AppendLog("CLIENT", "Service stopped successfully");
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Stop service failed: {ex.Message}");
            StatusMessage = $"Error stopping: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task RestartService()
    {
        try
        {
            AppendLog("CLIENT", "Restarting Python GPU service...");
            StatusMessage = "Restarting service...";
            ServiceStatus = "Restarting...";
            IsOptimizing = false;
            _wasOptimizing = false;

            await _apiClient.DisconnectWebSocketAsync();

            await _serviceManager.RestartAsync();

            IsServiceConnected = true;
            ServiceStatus = "Connected (GPU)";
            StatusMessage = "Python service restarted. Ready to optimize.";
            AppendLog("CLIENT", $"Service restarted at {_serviceManager.BaseUrl}");
            await ConnectWebSocket();
            await LoadPreviousRunsAsync();
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Restart failed: {ex.Message}");
            StatusMessage = $"Restart failed: {ex.Message}";
            ServiceStatus = "Failed";
            IsServiceConnected = false;
        }
    }

    [RelayCommand]
    private async Task StartOptimization()
    {
        if (!IsSequenceLoaded || IsOptimizing) return;

        if (!IsServiceConnected)
        {
            await StartService();
            if (!IsServiceConnected) return;
        }

        IsOptimizing = true;
        HasResult = false;
        ThresholdReached = false;
        ParetoFrontItems.Clear();
        InitializeCharts();

        var request = new ApiOptimizeRequest
        {
            PopulationSize = PopulationSize,
            CrossoverRate = CrossoverRate,
            MutationRate = MutationRate,
            FitnessThreshold = FitnessThreshold,
            CheckpointInterval = CheckpointInterval,
            TournamentSize = TournamentSize,
            Weights = new List<double>
            {
                WeightCai, WeightGc, WeightCpg, WeightUridine,
                WeightRareCodons, WeightRepeats, WeightCodonPair
            },
        };

        try
        {
            AppendLog("CLIENT", $"Sending start request: pop={PopulationSize}, threshold={FitnessThreshold}");
            var msg = await _apiClient.StartOptimization(request);
            AppendLog("CLIENT", $"Start response: {msg}");
            StatusMessage = $"Optimization started: {msg}";
            _wasOptimizing = true;
            if (!_apiClient.IsWebSocketConnected)
                await ConnectWebSocket();
            StartCompletionPolling();
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            AppendLog("WARN", "Service returned 400 — optimization may already be running, reconnecting...");
            _wasOptimizing = true;
            if (!_apiClient.IsWebSocketConnected)
                await ConnectWebSocket();
            StartCompletionPolling();
            StatusMessage = "Reconnected to existing optimization run.";
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Start optimization failed: {ex.Message}");
            StatusMessage = $"Error starting optimization: {ex.Message}";
            IsOptimizing = false;
        }
    }

    [RelayCommand]
    private async Task StopOptimization()
    {
        try
        {
            AppendLog("CLIENT", "Sending stop signal...");
            await _apiClient.StopOptimization();
            AppendLog("CLIENT", "Stop signal acknowledged by service");
            StatusMessage = "Stop signal sent. Saving checkpoint...";
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Stop failed: {ex.Message}");
            StatusMessage = $"Error stopping: {ex.Message}";
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        lock (_logLock) { DebugLog = string.Empty; }
        _lastLogTimestamp = string.Empty;
    }

    [RelayCommand]
    private async Task LoadPreviousRuns()
    {
        await LoadPreviousRunsAsync();
    }

    [RelayCommand]
    private async Task LoadSelectedRun()
    {
        if (SelectedPreviousRun == null) return;

        if (!IsServiceConnected)
        {
            await StartService();
            if (!IsServiceConnected) return;
        }

        try
        {
            StatusMessage = $"Loading run {SelectedPreviousRun.RunId}...";
            var results = await _apiClient.LoadRun(SelectedPreviousRun.RunId);
            if (results != null)
            {
                DisplayApiResults(results);
                HasResult = true;
                StatusMessage = $"Loaded run {SelectedPreviousRun.RunId}: Gen {results.Generation}, Best={results.BestFitness:F4}";
            }
            else
            {
                StatusMessage = "Failed to load run.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading run: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ResumeSelectedRun()
    {
        if (SelectedPreviousRun == null || IsOptimizing) return;

        if (!IsServiceConnected)
        {
            await StartService();
            if (!IsServiceConnected) return;
        }

        IsOptimizing = true;
        HasResult = false;
        ThresholdReached = false;
        InitializeCharts();

        var request = new ApiOptimizeRequest
        {
            PopulationSize = PopulationSize,
            CrossoverRate = CrossoverRate,
            MutationRate = MutationRate,
            FitnessThreshold = FitnessThreshold,
            CheckpointInterval = CheckpointInterval,
            TournamentSize = TournamentSize,
            Weights = new List<double>
            {
                WeightCai, WeightGc, WeightCpg, WeightUridine,
                WeightRareCodons, WeightRepeats, WeightCodonPair
            },
        };

        try
        {
            var msg = await _apiClient.ResumeRun(SelectedPreviousRun.RunId, request);
            StatusMessage = $"Resumed: {msg}";
            _wasOptimizing = true;
            if (!_apiClient.IsWebSocketConnected)
                await ConnectWebSocket();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error resuming: {ex.Message}";
            IsOptimizing = false;
        }
    }

    private async Task ConnectWebSocket()
    {
        AppendLog("CLIENT", "Connecting WebSocket for live streaming...");
        await _apiClient.ConnectWebSocketAsync();
    }

    private void OnWsStatusReceived(ApiStatusResponse status)
    {
        _dispatcher.BeginInvoke(() =>
        {
            CurrentGeneration = status.Generation;
            BestFitness = status.BestFitness;
            AvgFitness = status.AvgFitness;
            ParetoFrontSize = status.ParetoFrontSize;
            SequencesPerSecond = status.SeqsPerSec;
            ThresholdReached = status.ThresholdReached;
            RunId = status.RunId;
            ProgressText = status.Status;

            if (status.Status.Contains("CRASHED") || status.Status.Contains("Error"))
            {
                AppendLog("ERROR", $"Service reported error: {status.Status}");
            }

            var bestSeries = ConvergencePlot.Series.OfType<LineSeries>().FirstOrDefault();
            var avgSeries = ConvergencePlot.Series.OfType<LineSeries>().Skip(1).FirstOrDefault();
            if (bestSeries != null && status.Generation > 0)
            {
                if (bestSeries.Points.Count == 0 || bestSeries.Points.Last().X < status.Generation)
                {
                    bestSeries.Points.Add(new DataPoint(status.Generation, status.BestFitness));
                    avgSeries?.Points.Add(new DataPoint(status.Generation, status.AvgFitness));
                    SafeInvalidatePlot(ConvergencePlot);
                }
            }

            if (!status.Running && _wasOptimizing)
            {
                _completionPollCts?.Cancel();
                AppendLog("CLIENT", $"Optimization completed at gen {status.Generation}" +
                    (status.ThresholdReached ? " — THRESHOLD REACHED" : " — stopped by user"));
                IsOptimizing = false;
                _wasOptimizing = false;
                _ = FetchFinalResults(status);
            }
            else if (status.Running)
            {
                _wasOptimizing = true;
                IsOptimizing = true;
                StatusMessage = $"Gen {status.Generation} | Best: {status.BestFitness:F4} | Avg: {status.AvgFitness:F4} | {status.SeqsPerSec} seq/s" +
                                (status.ThresholdReached ? " | THRESHOLD MET" : "");
            }
        });
    }

    private void OnWsLogReceived(ApiLogEntry entry)
    {
        _dispatcher.BeginInvoke(() =>
        {
            AppendLog($"SVC:{entry.Level}", entry.Message);
            _lastLogTimestamp = entry.Timestamp;
        });
    }

    private void StartCompletionPolling()
    {
        _completionPollCts?.Cancel();
        _completionPollCts = new CancellationTokenSource();
        _ = PollForCompletion(_completionPollCts.Token);
    }

    private async Task PollForCompletion(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(3000, ct);
                var status = await _apiClient.GetStatus();
                if (status == null) continue;

                if (!status.Running)
                {
                    var s = status;
                    await _dispatcher.InvokeAsync(() =>
                    {
                        if (HasResult) return;

                        AppendLog("CLIENT", $"Completion detected via poll at gen {s.Generation}" +
                            (s.ThresholdReached ? " — THRESHOLD REACHED" : ""));
                        ThresholdReached = s.ThresholdReached;
                        CurrentGeneration = s.Generation;
                        BestFitness = s.BestFitness;
                        IsOptimizing = false;
                        _wasOptimizing = false;
                        _ = FetchFinalResults(s);
                    });
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _dispatcher.BeginInvoke(() =>
                AppendLog("WARN", $"Completion poll error: {ex.Message}"));
        }
    }

    private async Task FetchFinalResults(ApiStatusResponse status)
    {
        try
        {
            AppendLog("CLIENT", "Fetching final results from service...");
            var results = await _apiClient.GetResults();
            if (results != null && results.ParetoFront.Count > 0)
            {
                DisplayApiResults(results);
                HasResult = true;
                AppendLog("CLIENT", $"Final results loaded: {results.ParetoFront.Count} Pareto candidates");
            }
            else
            {
                AppendLog("WARN", $"Results response empty (ParetoFront={results?.ParetoFront.Count ?? 0})");
            }

            await LoadPreviousRunsAsync();

            StatusMessage = ThresholdReached
                ? $"THRESHOLD REACHED at gen {status.Generation}! Best={status.BestFitness:F4}"
                : $"Stopped at gen {status.Generation}. Best={status.BestFitness:F4}";
        }
        catch (Exception ex)
        {
            AppendLog("ERROR", $"Fetching final results failed: {ex.Message}");
        }
    }

    private void DisplayApiResults(ApiResultsResponse results)
    {
        ParetoFrontItems.Clear();
        for (int i = 0; i < results.ParetoFront.Count; i++)
        {
            var c = results.ParetoFront[i];
            ParetoFrontItems.Add(new CandidateDisplayItem
            {
                Index = i,
                Rank = i + 1,
                Cai = c.Cai,
                GcScore = c.GcScore,
                CpgScore = c.CpgScore,
                UridineScore = c.UridineScore,
                RareCodonScore = c.RareCodonScore,
                RepeatScore = c.RepeatScore,
                CodonPairScore = c.CodonPairScore,
                CompositeFitness = c.Composite,
            });
        }

        if (ParetoFrontItems.Count > 0)
            SelectedCandidate = ParetoFrontItems[0];

        UpdateParetoPlotFromApi(results);

        if (results.History.Count > 0)
        {
            var bestSeries = ConvergencePlot.Series.OfType<LineSeries>().FirstOrDefault();
            var avgSeries = ConvergencePlot.Series.OfType<LineSeries>().Skip(1).FirstOrDefault();
            if (bestSeries != null)
            {
                bestSeries.Points.Clear();
                avgSeries?.Points.Clear();
                foreach (var h in results.History)
                {
                    bestSeries.Points.Add(new DataPoint(h.Generation, h.BestFitness));
                    avgSeries?.Points.Add(new DataPoint(h.Generation, h.AvgFitness));
                }
                SafeInvalidatePlot(ConvergencePlot);
            }
        }
    }

    partial void OnSelectedCandidateChanged(CandidateDisplayItem? value)
    {
        if (value == null)
        {
            SelectedCandidateDetail = string.Empty;
            var hl = ParetoPlot.Series.OfType<ScatterSeries>()
                .FirstOrDefault(s => s.Title == "Selected");
            if (hl != null) { hl.Points.Clear(); SafeInvalidatePlot(ParetoPlot); }
            return;
        }

        SelectedCandidateDetail =
            $"Candidate #{value.Rank}\n" +
            $"━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            $"  CAI:              {value.Cai:F4}  (codon adaptation)\n" +
            $"  GC Content:       {value.GcScore:F4}  (target 45-55%)\n" +
            $"  CpG Depletion:    {value.CpgScore:F4}  (immune evasion)\n" +
            $"  Uridine Score:    {value.UridineScore:F4}  (low immunogenicity)\n" +
            $"  Rare Codon:       {value.RareCodonScore:F4}  (ribosome flow)\n" +
            $"  Repeat Score:     {value.RepeatScore:F4}  (synthesis quality)\n" +
            $"  Codon Pair:       {value.CodonPairScore:F4}  (junction quality)\n" +
            $"  ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━\n" +
            $"  COMPOSITE:        {value.CompositeFitness:F4}";

        HighlightSelectedOnChart(value);
    }

    private async Task CheckServiceConnectionAsync()
    {
        AppendLog("CLIENT", "Checking if Python service is already running...");
        var connected = await _apiClient.IsServiceRunning();
        IsServiceConnected = connected;
        ServiceStatus = connected ? "Connected" : "Not connected";
        AppendLog("CLIENT", connected ? "Service already running — connected" : "Service not running");
        if (connected)
        {
            await ConnectWebSocket();
            await LoadPreviousRunsAsync();
            await ReconnectIfRunning();
        }
    }

    private async Task ReconnectIfRunning()
    {
        try
        {
            var status = await _apiClient.GetStatus();
            if (status is { Running: true })
            {
                AppendLog("CLIENT", $"Reconnecting to in-progress optimization: run={status.RunId}, gen={status.Generation}");
                IsOptimizing = true;
                _wasOptimizing = true;
                CurrentGeneration = status.Generation;
                BestFitness = status.BestFitness;
                AvgFitness = status.AvgFitness;
                ParetoFrontSize = status.ParetoFrontSize;
                SequencesPerSecond = status.SeqsPerSec;
                RunId = status.RunId;
                ProgressText = status.Status;
                StatusMessage = $"Reconnected to running optimization: Gen {status.Generation} | Best: {status.BestFitness:F4}";
            }
        }
        catch (Exception ex)
        {
            AppendLog("WARN", $"Could not check for running optimization: {ex.Message}");
        }
    }

    private async Task LoadPreviousRunsAsync()
    {
        try
        {
            var runs = await _apiClient.GetHistory();
            PreviousRuns.Clear();
            foreach (var r in runs)
            {
                PreviousRuns.Add(new RunSummaryItem
                {
                    RunId = r.RunId,
                    Generation = r.Generation,
                    BestFitness = r.BestFitness,
                    Timestamp = r.Timestamp,
                    ThresholdReached = r.ThresholdReached,
                    ParetoFrontSize = r.ParetoFrontSize,
                });
            }
        }
        catch { }
    }

    private void InitializeCharts()
    {
        var convergence = new PlotModel
        {
            Title = "Optimization Convergence (GPU)",
            TitleColor = OxyColor.FromRgb(180, 220, 255),
            PlotAreaBorderColor = OxyColor.FromRgb(60, 65, 75),
            Background = OxyColor.FromRgb(30, 33, 40),
            TextColor = OxyColor.FromRgb(180, 185, 195),
        };
        convergence.Axes.Add(new LinearAxis
        {
            Key = "gen_x",
            Position = AxisPosition.Bottom,
            Title = "Generation",
            TitleColor = OxyColor.FromRgb(140, 145, 155),
            TicklineColor = OxyColor.FromRgb(60, 65, 75),
            TextColor = OxyColor.FromRgb(140, 145, 155),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(50, 55, 65),
        });
        convergence.Axes.Add(new LinearAxis
        {
            Key = "fit_y",
            Position = AxisPosition.Left,
            Title = "Composite Fitness",
            TitleColor = OxyColor.FromRgb(140, 145, 155),
            TicklineColor = OxyColor.FromRgb(60, 65, 75),
            TextColor = OxyColor.FromRgb(140, 145, 155),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(50, 55, 65),
            Minimum = 0,
            Maximum = 1,
        });
        convergence.Series.Add(new LineSeries
        {
            Title = "Best",
            Color = OxyColor.FromRgb(100, 200, 100),
            StrokeThickness = 2,
            XAxisKey = "gen_x",
            YAxisKey = "fit_y",
        });
        convergence.Series.Add(new LineSeries
        {
            Title = "Average",
            Color = OxyColor.FromRgb(100, 150, 255),
            StrokeThickness = 1,
            LineStyle = LineStyle.Dash,
            XAxisKey = "gen_x",
            YAxisKey = "fit_y",
        });
        ConvergencePlot = convergence;

        var pareto = new PlotModel
        {
            Title = "Pareto Front (CAI vs CpG Depletion)",
            TitleColor = OxyColor.FromRgb(180, 220, 255),
            PlotAreaBorderColor = OxyColor.FromRgb(60, 65, 75),
            Background = OxyColor.FromRgb(30, 33, 40),
            TextColor = OxyColor.FromRgb(180, 185, 195),
        };
        pareto.Axes.Add(new LinearAxis
        {
            Key = "cai_x",
            Position = AxisPosition.Bottom,
            Title = "CAI",
            TitleColor = OxyColor.FromRgb(140, 145, 155),
            TextColor = OxyColor.FromRgb(140, 145, 155),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(50, 55, 65),
        });
        pareto.Axes.Add(new LinearAxis
        {
            Key = "cpg_y",
            Position = AxisPosition.Left,
            Title = "CpG Depletion Score",
            TitleColor = OxyColor.FromRgb(140, 145, 155),
            TextColor = OxyColor.FromRgb(140, 145, 155),
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromRgb(50, 55, 65),
        });
        ParetoPlot = pareto;
    }

    private void SafeInvalidatePlot(PlotModel? plot)
    {
        if (plot == null) return;
        try { plot.InvalidatePlot(true); }
        catch { }
    }

    private void UpdateParetoPlotFromApi(ApiResultsResponse results)
    {
        ParetoPlot.Series.Clear();
        var scatter = new ScatterSeries
        {
            Title = "Candidates",
            MarkerType = MarkerType.Circle,
            MarkerSize = 5,
            MarkerFill = OxyColor.FromRgb(255, 180, 60),
            MarkerStroke = OxyColor.FromRgb(200, 140, 40),
            MarkerStrokeThickness = 0.5,
            XAxisKey = "cai_x",
            YAxisKey = "cpg_y",
            TrackerFormatString = "#{Tag}\nCAI: {2:F4}\nCpG: {4:F4}",
        };

        for (int i = 0; i < results.ParetoFront.Count; i++)
        {
            var c = results.ParetoFront[i];
            scatter.Points.Add(new ScatterPoint(c.Cai, c.CpgScore, tag: i + 1));
        }

        var highlight = new ScatterSeries
        {
            Title = "Selected",
            MarkerType = MarkerType.Circle,
            MarkerSize = 14,
            MarkerFill = OxyColor.FromArgb(60, 255, 50, 50),
            MarkerStroke = OxyColor.FromRgb(255, 60, 60),
            MarkerStrokeThickness = 2.5,
            XAxisKey = "cai_x",
            YAxisKey = "cpg_y",
            RenderInLegend = false,
        };

        ParetoPlot.Series.Add(scatter);
        ParetoPlot.Series.Add(highlight);
        SafeInvalidatePlot(ParetoPlot);
    }

    private void HighlightSelectedOnChart(CandidateDisplayItem candidate)
    {
        var highlight = ParetoPlot.Series.OfType<ScatterSeries>()
            .FirstOrDefault(s => s.Title == "Selected");
        if (highlight == null) return;

        highlight.Points.Clear();
        highlight.Points.Add(new ScatterPoint(candidate.Cai, candidate.CpgScore));
        SafeInvalidatePlot(ParetoPlot);
    }

    public void Shutdown()
    {
        _apiClient.StatusReceived -= OnWsStatusReceived;
        _apiClient.LogReceived -= OnWsLogReceived;
        _serviceManager.Dispose();
        _apiClient.Dispose();
    }
}

public class CandidateDisplayItem
{
    public int Index { get; set; }
    public int Rank { get; set; }
    public double Cai { get; set; }
    public double GcScore { get; set; }
    public double CpgScore { get; set; }
    public double UridineScore { get; set; }
    public double RareCodonScore { get; set; }
    public double RepeatScore { get; set; }
    public double CodonPairScore { get; set; }
    public double CompositeFitness { get; set; }

    public string CaiDisplay => Cai.ToString("F3");
    public string GcDisplay => GcScore.ToString("F3");
    public string CpgDisplay => CpgScore.ToString("F3");
    public string UridineDisplay => UridineScore.ToString("F3");
    public string CompositeDisplay => CompositeFitness.ToString("F4");
}

public class RunSummaryItem
{
    public string RunId { get; set; } = "";
    public int Generation { get; set; }
    public double BestFitness { get; set; }
    public string Timestamp { get; set; } = "";
    public bool ThresholdReached { get; set; }
    public int ParetoFrontSize { get; set; }

    public string Display => $"{Timestamp} | Gen {Generation} | Best: {BestFitness:F4}" +
                             (ThresholdReached ? " ★" : "");
}
