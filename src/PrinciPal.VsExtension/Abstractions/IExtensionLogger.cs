namespace PrinciPal.VsExtension.Abstractions
{
    /// <summary>
    /// Logs messages to the extension's output pane.
    /// </summary>
    public interface IExtensionLogger
    {
        void Log(string message);
    }
}
