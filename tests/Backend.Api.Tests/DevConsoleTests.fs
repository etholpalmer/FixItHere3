module FixItHere.Backend.Tests.DevConsoleTests

open System.Net
open Xunit
open FixItHere.Backend.Tests.AppFactory

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
