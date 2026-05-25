var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls(builder.Configuration["Urls"] ?? "http://localhost:3001");

var app = builder.Build();

// Локальная разработка: тот же config.js, что и для GitHub Pages (папка docs — источник, см. sync-portal.ps1)
var apiBaseUrl = (builder.Configuration["ApiBaseUrl"] ?? "http://localhost:3000").TrimEnd('/');

app.MapGet("/js/config.js", () =>
{
    var apiJson = System.Text.Json.JsonSerializer.Serialize(apiBaseUrl);
    var js = $"window.PORTAL_CONFIG = {{ apiBase: {apiJson} }};";
    return Results.Content(js, "application/javascript; charset=utf-8");
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapFallbackToFile("index.html");
app.Run();
