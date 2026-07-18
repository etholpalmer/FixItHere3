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
    Assert.Contains("FixItHere Demo Control Panel", body)
