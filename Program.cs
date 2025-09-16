using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

var haBaseUrl = Environment.GetEnvironmentVariable("HA_BASE_URL") ?? "http://localhost:8123";
var haToken   = Environment.GetEnvironmentVariable("HA_TOKEN")     ?? ""; 
var haEntity  = Environment.GetEnvironmentVariable("HA_SWITCH_ENTITY") ?? "switch.pump_plug";
var camUrl    = Environment.GetEnvironmentVariable("CAMERA_MJPEG_URL") ?? "http://localhost:8081/?action=stream";

// HttpClient for Home Assistant
builder.Services.AddHttpClient("homeassistant", client =>
{
    client.BaseAddress = new Uri(haBaseUrl);
    if (!string.IsNullOrWhiteSpace(haToken))
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", haToken);
    }
});

var app = builder.Build();

// Static files (wwwroot) and default index.html
app.UseDefaultFiles();
app.UseStaticFiles();

var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

// Health check
app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

// Return small config to the UI
app.MapGet("/api/config", () => Results.Ok(new
{
    entityId = haEntity,
    cameraMjpegUrl = camUrl,
    haBase = haBaseUrl
}));

// Get current state for the entity
app.MapGet("/api/state", async (IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("homeassistant");
    var res = await http.GetAsync($"/api/states/{haEntity}");
    if (!res.IsSuccessStatusCode)
    {
        return Results.Problem($"Failed to get state from Home Assistant: {(int)res.StatusCode} {res.ReasonPhrase}");
    }

    using var stream = await res.Content.ReadAsStreamAsync();
    using var doc = await JsonDocument.ParseAsync(stream);
    var root = doc.RootElement;

    var state = root.GetProperty("state").GetString();
    var attrs = root.GetProperty("attributes");
    var name = attrs.TryGetProperty("friendly_name", out var n) ? n.GetString() : haEntity;

    return Results.Ok(new { entityId = haEntity, name, state });
});

// Toggle the switch
app.MapPost("/api/toggle", async (IHttpClientFactory factory) =>
{
    var http = factory.CreateClient("homeassistant");
    var payload = new { entity_id = haEntity };
    var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    var res = await http.PostAsync("/api/services/switch/toggle", content);
    if (!res.IsSuccessStatusCode)
    {
        return Results.Problem($"Failed to toggle switch: {(int)res.StatusCode} {res.ReasonPhrase}");
    }

    return Results.Ok(new { ok = true });
});

app.Run();
