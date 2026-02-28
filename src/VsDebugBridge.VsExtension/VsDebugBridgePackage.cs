using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Task = System.Threading.Tasks.Task;

namespace VsDebugBridge.VsExtension
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [Guid(PackageGuidString)]
    [ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExistsAndFullyLoaded_string, PackageAutoLoadFlags.BackgroundLoad)]
    public sealed class VsDebugBridgePackage : AsyncPackage
    {
        public const string PackageGuidString = "28d14e0c-5a8f-4b7f-9c12-3e8a6b5d4c9f";

        private DebuggerEventHandler? _debuggerEventHandler;

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await base.InitializeAsync(cancellationToken, progress);

            // Switch to the main thread for DTE access
            await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            var dte = await GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE80.DTE2;
            if (dte == null) return;

            _debuggerEventHandler = new DebuggerEventHandler(dte, this);
            _debuggerEventHandler.Initialize();

            System.Diagnostics.Debug.WriteLine("VsDebugBridge: Package initialized successfully.");
        }

        protected override void Dispose(bool disposing)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (disposing)
            {
                _debuggerEventHandler?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
