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
    let resp =
        c.PostAsJsonAsync("/login",
            { Role = "Customer"; Email = "john.reyes@gmail.com"; Password = "Customer1!" }).Result
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

[<Fact>]
let ``provider online toggle flips and returns dto`` () =
    use c = client ()
    let resp = c.PutAsJsonAsync("/providers/1/online", {| online = false |}).Result
    let env = resp.Content.ReadFromJsonAsync<Envelope<ProviderDto>>().Result
    Assert.True(env.Success)
    Assert.False(env.Data.Online)
    // and it persists:
    let env2 = c.GetFromJsonAsync<Envelope<ProviderDto>>("/providers/1").Result
    Assert.False(env2.Data.Online)

[<Fact>]
let ``provider online toggle 404s on unknown id`` () =
    use c = client ()
    let resp = c.PutAsJsonAsync("/providers/9999/online", {| online = true |}).Result
    Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode)

// ---------------------------------------------------------------------------
// Rating identity. Customer and provider ids are independent sequences that
// both start at 1, so RateeId alone cannot say WHO was rated. Before RateeRole
// existed, a provider rating customer 1 dropped provider 1's public average
// (measured live: 3.33 -> 2.75) and put "customer was late" in that provider's
// public feedback. Same class as the message-identity bug fixed earlier.
// ---------------------------------------------------------------------------

[<Fact>]
let ``rating a customer does not change the provider's public rating`` () =
    use c = client ()
    let providerBefore =
        c.GetFromJsonAsync<Envelope<ProviderDto>>("/providers/1").Result.Data
    // A completed job, rated by its provider, about the CUSTOMER — who shares
    // the id 1 with Mike's Plumbing.
    let jobs = c.GetFromJsonAsync<Envelope<JobDto list>>("/jobs").Result.Data
    // Any finished job serves; seeded history is all Closed (a job never lingers
    // in the transient Completed state), so match on that.
    let job = jobs |> List.find (fun j -> j.State = "Closed")
    let req =
        { JobId = job.Id; RaterId = job.ProviderId; RaterRole = "Provider"
          RateeId = job.CustomerId; RateeRole = "Customer"
          Stars = 1; Comment = "customer was late" }
    let resp = c.PostAsJsonAsync("/ratings", req).Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)

    let providerAfter =
        c.GetFromJsonAsync<Envelope<ProviderDto>>("/providers/1").Result.Data
    Assert.Equal(providerBefore.RatingCount, providerAfter.RatingCount)
    Assert.Equal(providerBefore.Rating, providerAfter.Rating)

[<Fact>]
let ``customer-directed ratings stay out of the provider's public feedback`` () =
    use c = client ()
    let jobs = c.GetFromJsonAsync<Envelope<JobDto list>>("/jobs").Result.Data
    // Any finished job serves; seeded history is all Closed (a job never lingers
    // in the transient Completed state), so match on that.
    let job = jobs |> List.find (fun j -> j.State = "Closed")
    let req =
        { JobId = job.Id; RaterId = job.ProviderId; RaterRole = "Provider"
          RateeId = job.CustomerId; RateeRole = "Customer"
          Stars = 1; Comment = "customer was late" }
    c.PostAsJsonAsync("/ratings", req).Result |> ignore

    let feedback =
        c.GetFromJsonAsync<Envelope<RatingDto list>>("/ratings?providerId=1").Result.Data
    Assert.DoesNotContain(feedback, fun r -> r.Comment = "customer was late")
    Assert.All(feedback, fun r -> Assert.Equal("Provider", r.RateeRole))

// ---------------------------------------------------------------------------
// Sign-in failure modes. A login that accepts any password is a tell the moment
// someone tests it — which is exactly what a sceptical demo audience does.
// ---------------------------------------------------------------------------

[<Fact>]
let ``login rejects a wrong password`` () =
    use c = client ()
    let resp =
        c.PostAsJsonAsync("/login",
            { Role = "Customer"; Email = "john.reyes@gmail.com"; Password = "wrong" }).Result
    Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

[<Fact>]
let ``login rejects an unknown email`` () =
    use c = client ()
    let resp =
        c.PostAsJsonAsync("/login",
            { Role = "Customer"; Email = "nobody@nowhere.com"; Password = "Customer1!" }).Result
    Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

[<Fact>]
let ``login is case-insensitive on email but not on password`` () =
    use c = client ()
    let upper =
        c.PostAsJsonAsync("/login",
            { Role = "Customer"; Email = "John.Reyes@Gmail.com"; Password = "Customer1!" }).Result
    Assert.Equal(HttpStatusCode.OK, upper.StatusCode)
    let lowerPwd =
        c.PostAsJsonAsync("/login",
            { Role = "Customer"; Email = "john.reyes@gmail.com"; Password = "customer1!" }).Result
    Assert.Equal(HttpStatusCode.Unauthorized, lowerPwd.StatusCode)

[<Fact>]
let ``a customer cannot sign in with the provider password`` () =
    use c = client ()
    let resp =
        c.PostAsJsonAsync("/login",
            { Role = "Customer"; Email = "john.reyes@gmail.com"; Password = "Provider1!" }).Result
    Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode)

[<Fact>]
let ``provider signs in with its business email`` () =
    use c = client ()
    let resp =
        c.PostAsJsonAsync("/login",
            { Role = "Provider"; Email = "contact@mikesplumbing.ca"; Password = "Provider1!" }).Result
    Assert.Equal(HttpStatusCode.OK, resp.StatusCode)
    let env = resp.Content.ReadFromJsonAsync<Envelope<LoginResponse>>().Result
    Assert.Equal("Mike's Plumbing", env.Data.DisplayName)
    Assert.Equal("Provider", env.Data.Role)
