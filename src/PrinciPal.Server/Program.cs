using PrinciPal.Server;
using PrinciPal.Server.Extensions;

public partial class Program
{
    public static void Main(string[] args)
    {
        var port = CliArgs.ParsePort(args);

        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddPrinciPalServices(builder.Configuration);

        var app = builder.Build();
        app.MapPrinciPalEndpoints();

        app.Run($"http://localhost:{port}");
    }
}
