using Codenames;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameService>();

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapPost("/api/games", async (GameService games) => Results.Ok(new { code = await games.CreateGame() }));
app.MapHub<GameHub>("/hub/game");
app.MapFallbackToFile("index.html");
app.Run();
