using System;
using System.Diagnostics;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using PrinciPal.VsExtension.Abstractions;

namespace PrinciPal.VsExtension
{
    public sealed class OutputLogger : IExtensionLogger
    {
        private IVsOutputWindowPane? _pane;

        private static readonly Guid PaneGuid = new Guid("A1B2C3D4-1234-5678-9ABC-DEF012345678");

        public void EnsurePane()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var outputWindow = Package.GetGlobalService(typeof(SVsOutputWindow)) as IVsOutputWindow;
            if (outputWindow == null) return;

            var guid = PaneGuid;
            outputWindow.GetPane(ref guid, out _pane);
            if (_pane == null)
            {
                outputWindow.CreatePane(ref guid, "princiPal", fInitVisible: 1, fClearWithSolution: 0);
                outputWindow.GetPane(ref guid, out _pane);
            }
        }

        public void Log(string message)
        {
            var timestamped = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";

            if (_pane != null)
            {
#pragma warning disable VSTHRD010 // OutputStringThreadSafe is designed for cross-thread use
                _pane.OutputStringThreadSafe(timestamped);
#pragma warning restore VSTHRD010
            }
            else
            {
                Debug.WriteLine($"PrinciPal: {message}");
            }
        }
    }
}
