using System.ComponentModel;
using Microsoft.VisualStudio.Shell;

namespace PrinciPal.VsExtension
{
    public class PrinciPalOptionsPage : DialogPage
    {
        [Category("Server")]
        [DisplayName("Port")]
        [Description("Port for the MCP server. All VS instances share this single port. Change here and restart VS if the port is in use.")]
        public int Port { get; set; } = 9229;

        [Category("Server")]
        [DisplayName("Auto-start server")]
        [Description("Automatically start the MCP server when a solution is opened.")]
        public bool AutoStart { get; set; } = true;
    }
}
