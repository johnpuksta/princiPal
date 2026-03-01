using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace PrinciPal.Extension
{
    public class PrinciPalOptionsPage : DialogPage
    {
        [Category("Server")]
        [DisplayName("Port")]
        [Description("TCP port for the MCP server (default 9229).")]
        public int Port { get; set; } = 9229;

        [Category("Server")]
        [DisplayName("Auto-start server")]
        [Description("Automatically start the MCP server when a solution is opened.")]
        public bool AutoStart { get; set; } = true;
    }
}
