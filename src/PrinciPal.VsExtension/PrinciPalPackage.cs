using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using PrinciPal.VsExtension.Adapters;
using PrinciPal.VsExtension.Services;
using Task = System.Threading.Tasks.Task;

namespace PrinciPal.VsExtension
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(PrinciPalOptionsPage), "princiPal", "General", 0, 0, true)]
    public sealed class PrinciPalPackage : AsyncPackage
    {
        public const string PackageGuidString = "28d14e0c-5a8f-4b7f-9c12-3e8a6b5d4c9f";

        private DebuggerEventHandler? _debuggerEventHandler;
        private HttpDebugStatePublisher? _publisher;
        private ServerProcessManager? _processManager;
        private OutputLogger? _logger;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            // Switch to the main thread for DTE access and Output window
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            _logger = new OutputLogger();
            _logger.EnsurePane();

            var dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            if (dte == null)
            {
                _logger.Log("Failed to get DTE service.");
                return;
            }

            // Read options
            var options = (PrinciPalOptionsPage)GetDialogPage(typeof(PrinciPalOptionsPage));
            var port = options.Port;
            var autoStart = options.AutoStart;

            // Compute session ID (unique hash) and friendly name from solution path
            var solutionPath = dte.Solution?.FullName;
            string sessionId;
            string sessionName;
            if (!string.IsNullOrEmpty(solutionPath))
            {
                sessionName = Path.GetFileNameWithoutExtension(solutionPath);
                var bytes = Encoding.UTF8.GetBytes(solutionPath!.ToLowerInvariant());
                byte[] hash;
                using (var sha = new SHA256Managed())
                {
                    hash = sha.ComputeHash(bytes);
                }
                sessionId = BitConverter.ToString(hash, 0, 4).Replace("-", "").ToLowerInvariant();
            }
            else
            {
                sessionId = $"vs-{Process.GetCurrentProcess().Id}";
                sessionName = sessionId;
            }

            if (autoStart)
            {
                if (ServerProcessManager.IsPrinciPalServerRunning(port))
                {
                    _logger.Log($"Existing princiPal server detected on port {port}. Reusing it.");
                }
                else
                {
                    _processManager = new ServerProcessManager(_logger.Log);
                    _processManager.Start(port);
                }

                var mcpUrl = $"http://localhost:{port}/";
                _logger.Log($"MCP config: {{ \"mcpServers\": {{ \"princiPal\": {{ \"url\": \"{mcpUrl}\" }} }} }}");
                dte.StatusBar.Text = $"princiPal MCP: {mcpUrl}";
            }
            else
            {
                _logger.Log($"Auto-start disabled. Start the MCP server manually on port {port}.");
            }

            var reader = new VsDebuggerAdapter(dte);
            _publisher = new HttpDebugStatePublisher(port, sessionId, sessionName, solutionPath ?? "");
            var coordinator = new DebugEventCoordinator(reader, _publisher, _logger);

            _debuggerEventHandler = new DebuggerEventHandler(dte, this, coordinator, _logger);
            _debuggerEventHandler.Initialize();

            _logger.Log($"Session: {sessionName} [{sessionId}]");
            _logger.Log("Package initialized.");
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                // Best-effort deregister session from MCP server
                if (_debuggerEventHandler != null)
                {
                    try
                    {
                        _debuggerEventHandler.DeregisterSessionAsync().Wait(3000);
                    }
                    catch { /* best-effort */ }
                }

                _debuggerEventHandler?.Dispose();
                _publisher?.Dispose();
                _processManager?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
