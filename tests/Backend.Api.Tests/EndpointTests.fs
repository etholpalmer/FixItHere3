module FixItHere.Backend.Tests.EndpointTests

open System.Net
open System.Net.Http.Json
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Backend.Tests.AppFactory

let client () = (new Factory()).CreateClient()

[<Fact>]
let ``login returns fake token for named customer`` () =
    use c = client ()
    let resp = c.PostAsJsonAsync("/login", { Role = "Customer"; Name = "John" }).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<LoginResponse>>().Result
    Assert.True(env.Success)
    Assert.Equal("Customer", env.Data.Role)
    Assert.StartsWith("fake-customer-", env.Data.Token)

[<Fact>]
let ``services returns the seven catalog services`` () =
    use c = client ()
    let env = c.GetFromJsonAsync<Envelope<ServiceDto list>>("/services").Result
    Assert.Equal(7, List.length env.Data)

[<Fact>]
let ``providers are sorted by proximity to query point`` () =
    use c = client ()
    let env =
        c.GetFromJsonAsync<Envelope<ProviderDto list>>(
            "/providers?lat=43.65&lng=-79.38").Result
    // Match the endpoint's metric (haversine) — Euclidean lat/lng ordering
    // differs near 43N because longitude degrees shrink by cos(lat).
    let dist (p: ProviderDto) =
        let rad d = d * System.Math.PI / 180.0
        let lat1, lng1 = 43.65, -79.38
        let dLat = rad (p.Lat - lat1)
        let dLng = rad (p.Lng - lng1)
        let a = sin (dLat / 2.0) ** 2.0 + cos (rad lat1) * cos (rad p.Lat) * sin (dLng / 2.0) ** 2.0
        6371.0 * 2.0 * atan2 (sqrt a) (sqrt (1.0 - a))
    let ds = env.Data |> List.map dist
    Assert.Equal<float list>(List.sort ds, ds)

[<Fact>]
let ``full job lifecycle over http`` () =
    use c = client ()
    let created =
        c.PostAsJsonAsync("/jobs",
            { CustomerId = 1; ProviderId = 1; ServiceId = 1
              ScheduleChoice = "Now"; Lat = 43.65; Lng = -79.38
              Address = "1 Yonge St" }).Result
    let job = created.Content.ReadFromJsonAsync<Envelope<JobDto>>().Result.Data
    let put (path: string) =
        c.PutAsync(sprintf "/jobs/%d/%s" job.Id path, null).Result
    Assert.Equal(HttpStatusCode.OK, (put "accept").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "enroute").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "arrive").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "start").StatusCode)
    Assert.Equal(HttpStatusCode.OK, (put "complete").StatusCode)
    // invalid: complete again -> 409
    Assert.Equal(HttpStatusCode.Conflict, (put "complete").StatusCode)

[<Fact>]
let ``payment simulate returns transferred amount`` () =
    use c = client ()
    let resp = c.PostAsJsonAsync("/payment/simulate", { JobId = 1 }).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<PaymentResult>>().Result
    Assert.Equal("Transferred", env.Data.Status)
