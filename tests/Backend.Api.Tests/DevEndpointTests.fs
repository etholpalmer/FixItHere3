module FixItHere.Backend.Tests.DevEndpointTests

open System.Net
open System.Net.Http.Json
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Backend.Tests.AppFactory

[<Fact>]
let ``dev reset responds ok`` () =
    use factory = new Factory()
    use c = factory.CreateClient()
    let resp = c.PostAsync("/dev/reset", null).Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

[<Fact>]
let ``demo start creates a scheduled job immediately`` () =
    use factory = new Factory()
    use c = factory.CreateClient()
    let resp = c.PostAsJsonAsync("/dev/demo/start", {| customerId = 1; providerId = 1 |}).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<JobDto>>().Result
    Assert.True(env.Success)
    Assert.Equal("Scheduled", env.Data.State)
