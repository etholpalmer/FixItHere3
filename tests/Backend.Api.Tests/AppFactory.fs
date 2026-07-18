module FixItHere.Backend.Tests.AppFactory

open System
open System.IO
open System.Net
open Microsoft.AspNetCore.Mvc.Testing
open Xunit
open FixItHere.Backend.Program

/// SUT content root, resolved from this source file's compile-time location
/// (tests/Backend.Api.Tests) up to the repo root, then into src/Backend.Api.
let private sutContentRoot =
    Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "..", "src", "Backend.Api"))

type Factory() as this =
    inherit WebApplicationFactory<Program>()
    // Program.fs reads environment AND content root once, at module load
    // (WebApplication.CreateBuilder reads these env vars). The demo's dev
    // endpoints + /dev console are Development-only and the console lives in
    // src/Backend.Api/wwwroot, so both must be set before this factory's first
    // host build — hence the constructor, and via env vars (not ConfigureWebHost,
    // which lands after UseStaticFiles has already bound its file provider).
    do
        ignore this
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development")
        Environment.SetEnvironmentVariable("ASPNETCORE_CONTENTROOT", sutContentRoot)

[<Fact>]
let ``app boots, seeds, and serves health`` () =
    use factory = new Factory()
    use client = factory.CreateClient()
    let resp = client.GetAsync("/health").Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
