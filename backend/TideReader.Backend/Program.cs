using TideReader.Backend;

var app = BackendHost.Build(args, new BackendHostOptions
{
    AllowedOrigins =
    [
        "http://127.0.0.1:5173",
        "http://localhost:5173"
    ]
});

app.Run();
