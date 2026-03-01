using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace PrinciPal.Extension
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
    [ProvideOptionPage(typeof(PrinciPalOptionsPage), "princiPal", "General", 0, 0, true)]
    public sealed class PrinciPalPackage : AsyncPackage
    {
        public const string PackageGuidString = "28d14e0c-5a8f-4b7f-9c12-3e8a6b5d4c9f";

        private DebuggerEventHandler? _debuggerEventHandler;
        private McpServerProcessManager? _processManager;
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
            var configuredPort = options.Port;
            var autoStart = options.AutoStart;

            int port = configuredPort;

            if (autoStart)
            {
                if (McpServerProcessManager.IsPortListening(configuredPort))
                {
                    // Server already running (multi-project startup, or manual)
                    port = configuredPort;
                    _logger.Log($"Existing server detected on port {port}. Skipping auto-start.");
                }
                else
                {
                    // No server running — start one ourselves
                    _processManager = new McpServerProcessManager(_logger.Log);
                    _processManager.Start(configuredPort);
                }

                var mcpUrl = $"http://localhost:{port}/";
                _logger.Log($"MCP config: {{ \"mcpServers\": {{ \"princiPal\": {{ \"url\": \"{mcpUrl}\" }} }} }}");
                dte.StatusBar.Text = $"princiPal MCP: {mcpUrl}";
            }
            else
            {
                _logger.Log($"Auto-start disabled. Start the MCP server manually on port {port}.");
            }

            _debuggerEventHandler = new DebuggerEventHandler(dte, this, port);
            _debuggerEventHandler.Initialize();

            _logger.Log("Package initialized.");
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                _debuggerEventHandler?.Dispose();
                _processManager?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
