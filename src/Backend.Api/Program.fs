module FixItHere.Backend.Program

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open FixItHere.Backend.Db
open FixItHere.Backend.Services
open FixItHere.Backend.Hub

let builder = WebApplication.CreateBuilder()
builder.Services.AddDbContext<AppDb>(fun opts ->
    opts.UseSqlite("Data Source=fixithere-demo.db") |> ignore) |> ignore
builder.Services.AddSignalR() |> ignore
// The in-app map is an HTML string loaded into a WebView, so its document has a
// *null* origin — every call it makes to this backend is cross-origin. Without
// a policy the browser blocked both the map's initial position fetch and
// SignalR's negotiate, which is why the tracking screen never showed a moving
// provider: the page was there, the connection never was.
//
// AllowAnyOrigin is correct here and only here: this backend serves a local
// demo, holds no real credentials, and its only clients are two simulators and
// a console on the same machine. AllowCredentials is deliberately NOT set —
// that combination is the one browsers reject outright.
builder.Services.AddCors(fun opts ->
    opts.AddDefaultPolicy(fun p ->
        p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod() |> ignore)) |> ignore
builder.Services.AddScoped<IBroadcaster, SignalRBroadcaster>() |> ignore
builder.Services.AddScoped<JobService>() |> ignore
// Singleton: there is exactly one demo world, and every request must agree on
// what time it is in that world.
builder.Services.AddSingleton<FixItHere.Backend.Clock.DemoClockService>() |> ignore

let app = builder.Build()

// Every startup: drop, recreate, reseed — byte-identical demo data.
do
    use scope = app.Services.CreateScope()
    let db = scope.ServiceProvider.GetRequiredService<AppDb>()
    db.Database.EnsureDeleted() |> ignore
    db.Database.EnsureCreated() |> ignore
    FixItHere.Backend.Seed.run db

// Before any endpoint, so preflight is answered for all of them.
app.UseCors() |> ignore

app.MapHub<DemoHub>("/hub") |> ignore
app.MapGet("/health", Func<string>(fun () -> "ok")) |> ignore

FixItHere.Backend.Endpoints.mapAll app

if app.Environment.IsDevelopment() then
    FixItHere.Backend.DevEndpoints.mapAll app
    // "/" must land somewhere: a bare 404 renders as a solid black page in a
    // dark-mode browser, and the root URL is every natural entry point (typing
    // host:port, preview tools' default tab). Send it to the console.
    app.MapGet("/", Func<IResult>(fun () -> Results.Redirect "/dev/index.html")) |> ignore
    app.MapGet("/dev", Func<IResult>(fun () -> Results.Redirect "/dev/index.html")) |> ignore
    app.UseStaticFiles() |> ignore   // serves wwwroot/dev

app.Run()

type Program() = class end   // marker for WebApplicationFactory
