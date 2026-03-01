using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

namespace PrinciPal.Extension
{
    public sealed class McpServerProcessManager : IDisposable
    {
        private readonly Action<string> _log;
        private readonly object _lock = new object();
        private Process? _process;
        private int _port;
        private int _restartCount;
        private bool _disposed;

        private const int MaxRestarts = 5;
        private const string ServerExeRelativePath = "Server\\PrinciPal.McpServer.exe";
        private const string DevMarkerRelativePath = "Server\\.devproject";

        public int Port
        {
            get { lock (_lock) { return _port; } }
        }

        public McpServerProcessManager(Action<string> log)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
        }

        public void Start(int port)
        {
            lock (_lock)
            {
                if (_disposed) return;
                _port = port;
                _restartCount = 0;
                StartProcess();
            }
        }

        private void StartProcess()
        {
            var parentPid = Process.GetCurrentProcess().Id;
            var startInfo = ResolveStartInfo(parentPid);
            if (startInfo == null) return;

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.OutputDataReceived += (s, e) => { if (e.Data != null) _log(e.Data); };
            _process.ErrorDataReceived += (s, e) => { if (e.Data != null) _log($"[stderr] {e.Data}"); };
            _process.Exited += OnProcessExited;

            try
            {
                _process.Start();
                _process.BeginOutputReadLine();
                _process.BeginErrorReadLine();
                _log($"MCP server started (PID {_process.Id}) on http://localhost:{_port}/");
            }
            catch (Exception ex)
            {
                _log($"Failed to start MCP server: {ex.Message}");
            }
        }

        private ProcessStartInfo? ResolveStartInfo(int parentPid)
        {
            var extensionDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            var exePath = Path.Combine(extensionDir, ServerExeRelativePath);
            var devMarkerPath = Path.Combine(extensionDir, DevMarkerRelativePath);
            var args = $"--port {_port} --parent-pid {parentPid}";

            // Release: bundled self-contained exe
            if (File.Exists(exePath))
            {
                _log($"Using bundled server: {exePath}");
                return new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
            }

            // Debug: dev marker contains the .csproj path, use dotnet run
            if (File.Exists(devMarkerPath))
            {
                var projectPath = File.ReadAllText(devMarkerPath).Trim();
                if (File.Exists(projectPath))
                {
                    _log($"Dev mode: using dotnet run --project {projectPath}");
                    return new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = $"run --project \"{projectPath}\" --no-build -- {args}",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                    };
                }
            }

            _log($"Server not found. Checked:\n  - {exePath}\n  - {devMarkerPath}");
            return null;
        }

        private void OnProcessExited(object sender, EventArgs e)
        {
            lock (_lock)
            {
                if (_disposed) return;

                var exitCode = -1;
                try { exitCode = _process?.ExitCode ?? -1; } catch { }

                if (exitCode == 0)
                {
                    _log("MCP server exited normally.");
                    return;
                }

                _restartCount++;
                if (_restartCount > MaxRestarts)
                {
                    _log($"MCP server crashed {_restartCount} times. Giving up.");
                    return;
                }

                _log($"MCP server crashed (exit code {exitCode}). Restarting (attempt {_restartCount}/{MaxRestarts})...");
                StartProcess();
            }
        }

        public static bool IsPortListening(int port)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    client.Connect(IPAddress.Loopback, port);
                    return true;
                }
            }
            catch (SocketException)
            {
                return false;
            }
        }

        public static int FindAvailablePort(int startPort)
        {
            for (int port = startPort; port < startPort + 100; port++)
            {
                try
                {
                    var listener = new TcpListener(IPAddress.Loopback, port);
                    listener.Start();
                    listener.Stop();
                    return port;
                }
                catch (SocketException)
                {
                    // Port in use, try next
                }
            }
            return startPort; // fallback
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;

                if (_process != null && !_process.HasExited)
                {
                    try
                    {
                        _process.Kill();
                        _process.WaitForExit(5000);
                        _log("MCP server stopped.");
                    }
                    catch (Exception ex)
                    {
                        _log($"Error stopping MCP server: {ex.Message}");
                    }
                }

                _process?.Dispose();
                _process = null;
            }
        }
    }
}
