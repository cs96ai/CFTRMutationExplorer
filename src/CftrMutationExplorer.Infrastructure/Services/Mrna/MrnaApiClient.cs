using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CftrMutationExplorer.Infrastructure.Services.Mrna;

/// <summary>
/// HTTP + WebSocket client for the Python mRNA optimization FastAPI service.
/// </summary>
public class MrnaApiClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _wsUrl;
    private ClientWebSocket? _ws;
    private CancellationTokenSource? _wsCts;
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public event Action<ApiStatusResponse>? StatusReceived;
    public event Action<ApiLogEntry>? LogReceived;
    public event Action<string>? WebSocketError;
    public event Action? WebSocketConnected;
    public event Action? WebSocketDisconnected;

    public bool IsWebSocketConnected => _ws?.State == WebSocketState.Open;

    public MrnaApiClient(string baseUrl = "http://127.0.0.1:8787")
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _wsUrl = _baseUrl.Replace("http://", "ws://").Replace("https://", "wss://");
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public async Task ConnectWebSocketAsync()
    {
        await DisconnectWebSocketAsync();

        const int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            var ws = new ClientWebSocket();
            var cts = new CancellationTokenSource();

            try
            {
                await ws.ConnectAsync(new Uri($"{_wsUrl}/ws"), cts.Token);
                _ws = ws;
                _wsCts = cts;
                WebSocketConnected?.Invoke();
                var localWs = ws;
                var localToken = cts.Token;
                _ = Task.Run(() => ReceiveLoop(localWs, localToken));
                return;
            }
            catch (Exception ex)
            {
                ws.Dispose();
                cts.Dispose();
                if (attempt == maxRetries)
                {
                    WebSocketError?.Invoke($"WebSocket connect failed after {maxRetries} attempts: {ex.Message}");
                    return;
                }
                await Task.Delay(500 * attempt);
            }
        }
    }

    public async Task DisconnectWebSocketAsync()
    {
        if (_wsCts != null)
        {
            await _wsCts.CancelAsync();
            _wsCts.Dispose();
            _wsCts = null;
        }
        if (_ws != null)
        {
            if (_ws.State == WebSocketState.Open)
            {
                try { await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); }
                catch { }
            }
            _ws.Dispose();
            _ws = null;
        }
    }

    private async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var sb = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
            {
                sb.Clear();
                WebSocketReceiveResult result;
                do
                {
                    result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        WebSocketDisconnected?.Invoke();
                        return;
                    }
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                ProcessWebSocketMessage(sb.ToString());
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException) { }
        catch (Exception ex)
        {
            WebSocketError?.Invoke($"WebSocket receive error: {ex.Message}");
        }
        finally
        {
            WebSocketDisconnected?.Invoke();
        }
    }

    private void ProcessWebSocketMessage(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.GetProperty("type").GetString();

            switch (type)
            {
                case "status":
                    var status = JsonSerializer.Deserialize<ApiStatusResponse>(json, JsonOpts);
                    if (status != null) StatusReceived?.Invoke(status);
                    break;
                case "log":
                    var log = JsonSerializer.Deserialize<ApiLogEntry>(json, JsonOpts);
                    if (log != null) LogReceived?.Invoke(log);
                    break;
                case "heartbeat":
                    break;
            }
        }
        catch { }
    }

    public async Task<bool> IsServiceRunning()
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/");
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    public async Task<string> StartOptimization(ApiOptimizeRequest request)
    {
        var resp = await _http.PostAsJsonAsync($"{_baseUrl}/optimize/start", request, JsonOpts);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("message").GetString() ?? "";
    }

    public async Task StopOptimization()
    {
        var resp = await _http.PostAsync($"{_baseUrl}/optimize/stop", null);
        resp.EnsureSuccessStatusCode();
    }

    public async Task<ApiStatusResponse?> GetStatus()
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/optimize/status");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ApiStatusResponse>(JsonOpts);
        }
        catch { return null; }
    }

    public async Task<ApiResultsResponse?> GetResults()
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/optimize/results");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ApiResultsResponse>(JsonOpts);
        }
        catch { return null; }
    }

    public async Task<List<ApiRunSummary>> GetHistory()
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/optimize/history");
            if (!resp.IsSuccessStatusCode) return new();
            var result = await resp.Content.ReadFromJsonAsync<ApiHistoryResponse>(JsonOpts);
            return result?.Runs ?? new();
        }
        catch { return new(); }
    }

    public async Task<ApiResultsResponse?> LoadRun(string runId)
    {
        try
        {
            var resp = await _http.GetAsync($"{_baseUrl}/optimize/history/{runId}");
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ApiResultsResponse>(JsonOpts);
        }
        catch { return null; }
    }

    public async Task<string> ResumeRun(string runId, ApiOptimizeRequest request)
    {
        var resp = await _http.PostAsJsonAsync($"{_baseUrl}/optimize/resume/{runId}", request, JsonOpts);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return result.GetProperty("message").GetString() ?? "";
    }

    public async Task<List<ApiLogEntry>> GetLogs(string? since = null, int limit = 200)
    {
        try
        {
            var url = $"{_baseUrl}/logs?limit={limit}";
            if (!string.IsNullOrEmpty(since))
                url += $"&since={Uri.EscapeDataString(since)}";
            var resp = await _http.GetAsync(url);
            if (!resp.IsSuccessStatusCode) return new();
            var result = await resp.Content.ReadFromJsonAsync<ApiLogsResponse>(JsonOpts);
            return result?.Logs ?? new();
        }
        catch { return new(); }
    }

    public void Dispose()
    {
        DisconnectWebSocketAsync().GetAwaiter().GetResult();
        _http.Dispose();
    }
}

// API DTOs

public class ApiOptimizeRequest
{
    [JsonPropertyName("population_size")]
    public int PopulationSize { get; set; } = 750;

    [JsonPropertyName("crossover_rate")]
    public double CrossoverRate { get; set; } = 0.75;

    [JsonPropertyName("mutation_rate")]
    public double MutationRate { get; set; } = 0.03;

    [JsonPropertyName("fitness_threshold")]
    public double FitnessThreshold { get; set; } = 0.90;

    [JsonPropertyName("checkpoint_interval")]
    public int CheckpointInterval { get; set; } = 100;

    [JsonPropertyName("tournament_size")]
    public int TournamentSize { get; set; } = 2;

    [JsonPropertyName("weights")]
    public List<double> Weights { get; set; } = new() { 1.0, 0.9, 0.9, 0.7, 0.6, 0.5, 0.4 };
}

public class ApiStatusResponse
{
    [JsonPropertyName("running")]
    public bool Running { get; set; }

    [JsonPropertyName("generation")]
    public int Generation { get; set; }

    [JsonPropertyName("best_fitness")]
    public double BestFitness { get; set; }

    [JsonPropertyName("avg_fitness")]
    public double AvgFitness { get; set; }

    [JsonPropertyName("pareto_front_size")]
    public int ParetoFrontSize { get; set; }

    [JsonPropertyName("seqs_per_sec")]
    public int SeqsPerSec { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("threshold_reached")]
    public bool ThresholdReached { get; set; }

    [JsonPropertyName("started_at")]
    public string? StartedAt { get; set; }
}

public class ApiResultsResponse
{
    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("generation")]
    public int Generation { get; set; }

    [JsonPropertyName("best_fitness")]
    public double BestFitness { get; set; }

    [JsonPropertyName("pareto_front")]
    public List<ApiCandidate> ParetoFront { get; set; } = new();

    [JsonPropertyName("best_candidate")]
    public ApiBestCandidate? BestCandidate { get; set; }

    [JsonPropertyName("history")]
    public List<ApiGenStats> History { get; set; } = new();
}

public class ApiCandidate
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("cai")]
    public double Cai { get; set; }

    [JsonPropertyName("gc_score")]
    public double GcScore { get; set; }

    [JsonPropertyName("cpg_score")]
    public double CpgScore { get; set; }

    [JsonPropertyName("uridine_score")]
    public double UridineScore { get; set; }

    [JsonPropertyName("rare_codon_score")]
    public double RareCodonScore { get; set; }

    [JsonPropertyName("repeat_score")]
    public double RepeatScore { get; set; }

    [JsonPropertyName("codon_pair_score")]
    public double CodonPairScore { get; set; }

    [JsonPropertyName("composite")]
    public double Composite { get; set; }
}

public class ApiBestCandidate
{
    [JsonPropertyName("composite")]
    public double Composite { get; set; }

    [JsonPropertyName("cai")]
    public double Cai { get; set; }

    [JsonPropertyName("gc_score")]
    public double GcScore { get; set; }

    [JsonPropertyName("cpg_score")]
    public double CpgScore { get; set; }

    [JsonPropertyName("uridine_score")]
    public double UridineScore { get; set; }

    [JsonPropertyName("rare_codon_score")]
    public double RareCodonScore { get; set; }

    [JsonPropertyName("repeat_score")]
    public double RepeatScore { get; set; }

    [JsonPropertyName("codon_pair_score")]
    public double CodonPairScore { get; set; }

    [JsonPropertyName("rna_sequence_first_120")]
    public string RnaSequenceFirst120 { get; set; } = "";

    [JsonPropertyName("rna_length")]
    public int RnaLength { get; set; }
}

public class ApiGenStats
{
    [JsonPropertyName("generation")]
    public int Generation { get; set; }

    [JsonPropertyName("best_fitness")]
    public double BestFitness { get; set; }

    [JsonPropertyName("avg_fitness")]
    public double AvgFitness { get; set; }

    [JsonPropertyName("pareto_front_size")]
    public int ParetoFrontSize { get; set; }

    [JsonPropertyName("elapsed_seconds")]
    public double ElapsedSeconds { get; set; }

    [JsonPropertyName("seqs_per_sec")]
    public int SeqsPerSec { get; set; }
}

public class ApiRunSummary
{
    [JsonPropertyName("filename")]
    public string Filename { get; set; } = "";

    [JsonPropertyName("run_id")]
    public string RunId { get; set; } = "";

    [JsonPropertyName("generation")]
    public int Generation { get; set; }

    [JsonPropertyName("best_fitness")]
    public double BestFitness { get; set; }

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("threshold_reached")]
    public bool ThresholdReached { get; set; }

    [JsonPropertyName("pareto_front_size")]
    public int ParetoFrontSize { get; set; }
}

public class ApiHistoryResponse
{
    [JsonPropertyName("runs")]
    public List<ApiRunSummary> Runs { get; set; } = new();
}

public class ApiLogEntry
{
    [JsonPropertyName("timestamp")]
    public string Timestamp { get; set; } = "";

    [JsonPropertyName("level")]
    public string Level { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";
}

public class ApiLogsResponse
{
    [JsonPropertyName("logs")]
    public List<ApiLogEntry> Logs { get; set; } = new();

    [JsonPropertyName("total")]
    public int Total { get; set; }
}
