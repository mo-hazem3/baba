using Baba.Api;

// Standalone host for development, and the starting point for the cloud edition.
// The desktop app does not use this file: it builds the same host through BabaApi.Create.
var options = new BabaApiOptions
{
    Port = int.TryParse(Environment.GetEnvironmentVariable("BABA_PORT"), out var port) ? port : 5054,
    AccessToken = Environment.GetEnvironmentVariable("BABA_TOKEN"),
    WebRootPath = Environment.GetEnvironmentVariable("BABA_WEBROOT"),
    AdditionalAllowedOrigins = ["http://localhost:5173", "http://127.0.0.1:5173"],
};

BabaApi.Create(options, args: args).Run();
