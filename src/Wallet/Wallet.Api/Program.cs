namespace MiniWallet.Wallet.Api;

public partial class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        app.Run();
    }
}

