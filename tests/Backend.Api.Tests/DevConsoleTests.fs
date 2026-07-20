module FixItHere.Backend.Tests.DevConsoleTests

open System.Net
open Xunit
open FixItHere.Backend.Tests.AppFactory

[<Fact>]
let ``root url lands on the dev console in development`` () =
    // "Backend was a black screen": every natural entry point — typing
    // localhost:5162, the preview tool's default tab — hits "/", which served a
    // zero-byte 404 that renders as a solid black page in a dark-mode browser.
    // Opening the root must land on the console, not require knowing /dev.
    use factory = new Factory()
    use c = factory.CreateClient()   // follows redirects by default
    let resp = c.GetAsync("/").Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let body = resp.Content.ReadAsStringAsync().Result
    Assert.Contains("id=\"map\"", body)

[<Fact>]
let ``dev console page is served in development`` () =
    use factory = new Factory()
    use c = factory.CreateClient()
    let resp = c.GetAsync("/dev/index.html").Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let body = resp.Content.ReadAsStringAsync().Result
    // Assert on the structural anchors the console's own script binds to, not on
    // heading copy. This test exists to prove the page is served under
    // Development; it previously asserted the exact <title> string, so a purely
    // visual redesign failed it for reasons unrelated to what it guards.
    Assert.Contains("id=\"map\"", body)
    Assert.Contains("id=\"jobs\"", body)
    Assert.Contains("id=\"log\"", body)
