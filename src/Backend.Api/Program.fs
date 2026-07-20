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
builder.Services.AddScoped<IBroadcaster, SignalRBroadcaster>() |> ignore
builder.Services.AddScoped<JobService>() |> ignore

let app = builder.Build()

// Every startup: drop, recreate, reseed — byte-identical demo data.
do
    use scope = app.Services.CreateScope()
    let db = scope.ServiceProvider.GetRequiredService<AppDb>()
    db.Database.EnsureDeleted() |> ignore
    db.Database.EnsureCreated() |> ignore
    FixItHere.Backend.Seed.run db

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
