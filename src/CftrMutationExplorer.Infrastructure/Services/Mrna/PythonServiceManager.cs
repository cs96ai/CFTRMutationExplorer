using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace CftrMutationExplorer.Infrastructure.Services.Mrna;

/// <summary>
/// Manages the Python FastAPI service lifecycle.
/// Can start, stop, and restart the service — including killing orphaned
/// processes from previous sessions that are still bound to the port.
/// </summary>
public class PythonServiceManager : IDisposable
{
    private Process? _process;
    private readonly string _scriptDir;
    private readonly int _port;
    private bool _disposed;

    public bool IsRunning => _process != null && !_process.HasExited;
    public string BaseUrl => $"http://127.0.0.1:{_port}";
    public int Port => _port;

    public PythonServiceManager(int port = 8787)
    {
        _port = port;
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _scriptDir = FindServiceDirectory(baseDir);
    }

    public async Task StartAsync()
    {
        if (IsRunning) return;

        if (string.IsNullOrEmpty(_scriptDir) || !Directory.Exists(_scriptDir))
            throw new InvalidOperationException(
                "Python service directory not found. Expected at scripts/mrna_service/");

        var pythonExe = FindPython();
        if (string.IsNullOrEmpty(pythonExe))
            throw new InvalidOperationException(
                "Python not found. Install Python 3.10+ and ensure it's on PATH.");

        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = pythonExe,
                Arguments = $"-m uvicorn main:app --host 127.0.0.1 --port {_port}",
                WorkingDirectory = _scriptDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
            EnableRaisingEvents = true,
        };

        _process.Start();

        var client = new MrnaApiClient(BaseUrl);
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(1000);
            if (await client.IsServiceRunning())
                return;
        }

        throw new TimeoutException("Python service failed to start within 30 seconds.");
    }

    public void Stop()
    {
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
            catch { }
        }
        _process?.Dispose();
        _process = null;
    }

    /// <summary>
    /// Kills any process listening on the configured port, even if it wasn't
    /// spawned by this manager (e.g. orphaned from a previous session).
    /// </summary>
    public void KillOrphanedService()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "netstat",
                Arguments = "-ano",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using var netstat = Process.Start(psi);
            if (netstat == null) return;

            var output = netstat.StandardOutput.ReadToEnd();
            netstat.WaitForExit(5000);

            var portStr = $":{_port}";
            foreach (var line in output.Split('\n'))
            {
                if (!line.Contains(portStr) || !line.Contains("LISTENING")) continue;

                var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 5 && int.TryParse(parts[^1], out var pid) && pid > 0)
                {
                    try
                    {
                        using var proc = Process.GetProcessById(pid);
                        proc.Kill(entireProcessTree: true);
                        proc.WaitForExit(3000);
                    }
                    catch { }
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Full stop: kills our tracked process AND any orphaned process on the port.
    /// </summary>
    public void ForceStop()
    {
        Stop();
        KillOrphanedService();
    }

    /// <summary>
    /// Force-stops everything on the port, then starts a fresh service.
    /// </summary>
    public async Task RestartAsync()
    {
        ForceStop();
        await Task.Delay(1000);
        await StartAsync();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    private static string FindPython()
    {
        var candidates = new[] { "python", "python3", "py" };
        foreach (var name in candidates)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo
                {
                    FileName = name,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                });
                if (p != null)
                {
                    p.WaitForExit(5000);
                    if (p.ExitCode == 0) return name;
                    p.Dispose();
                }
            }
            catch { }
        }
        return string.Empty;
    }

    private static string FindServiceDirectory(string baseDir)
    {
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "scripts", "mrna_service");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "main.py")))
                return candidate;
            dir = dir.Parent;
        }
        return string.Empty;
    }
}
