using SecondGuess.Web.Components;
using SecondGuess.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// One shared service: loads matches, runs the what-if engine, and owns the agents (started on first question).
builder.Services.AddSingleton<SecondGuessService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

// Machine-readable feeds for broadcast partners: every overlay card is timed (match clock + timestamp),
// has a display duration and priority, and cites the source events it was built from.
var api = app.MapGroup("/api");
api.MapGet("/matches", (SecondGuessService service) => service.ListMatches());
api.MapGet("/matches/{matchId}/overlays", (string matchId, SecondGuessService service) =>
{
    try { return Results.Ok(service.GetOverlayCards(matchId)); }
    catch (KeyNotFoundException ex) { return Results.NotFound(new { error = ex.Message }); }
});

app.Run();
