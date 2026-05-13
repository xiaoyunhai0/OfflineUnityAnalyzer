using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

namespace OfflineUnityAnalyzer.ViewerServer;

public static class ViewerServerHost
{
    public static WebApplication Build(ViewerServerOptions options)
    {
        var builder = WebApplication.CreateBuilder();
        var port = options.Port <= 0 ? 0 : options.Port;
        builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

        var app = builder.Build();

        app.MapGet("/api/health", () => Results.Ok(new
        {
            status = "ok",
            outputRoot = Path.GetFullPath(options.OutputRoot)
        }));

        app.MapGet("/api/summary", async () =>
        {
            var summaryPath = Path.Combine(options.OutputRoot, "summary.json");
            if (!File.Exists(summaryPath))
            {
                return Results.NotFound(new { error = "summary.json not found" });
            }

            var json = await File.ReadAllTextAsync(summaryPath);
            return Results.Text(json, "application/json");
        });

        return app;
    }
}
