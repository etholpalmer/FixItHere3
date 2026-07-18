module FixItHere.Customer.Tests.UpdateTests

open System.Threading.Tasks
open Xunit
open FixItHere.Customer
open FixItHere.Shared.Dtos

[<Fact>]
let ``push stores current screen in history`` () =
    let m = { Model.initial with Screen = Home }
    let m2 = Nav.push m Catalog
    Assert.Equal(Catalog, m2.Screen)
    Assert.Equal<Screen list>([Home], m2.History)

[<Fact>]
let ``back pops one screen`` () =
    let m = { Model.initial with Screen = Catalog; History = [Home] }
    let m2 = Nav.back m
    Assert.Equal(Home, m2.Screen)
    Assert.Empty(m2.History)

[<Fact>]
let ``back on empty history lands on Home`` () =
    let m = { Model.initial with Screen = Catalog; History = [] }
    Assert.Equal(Home, (Nav.back m).Screen)

[<Fact>]
let ``resetTo clears history`` () =
    let m = { Model.initial with Screen = Payment 7; History = [Home; Catalog] }
    let m2 = Nav.resetTo Home m
    Assert.Equal(Home, m2.Screen)
    Assert.Empty(m2.History)

let stubDeps : ApiDeps =
    { Login = fun _ -> Task.FromResult(Ok { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" })
      GetServices = fun () -> Task.FromResult(Ok [])
      GetProviders = fun _ _ _ -> Task.FromResult(Ok [])
      GetRatings = fun _ -> Task.FromResult(Ok [])
      GetJobs = fun _ -> Task.FromResult(Ok [])
      CreateJob = fun _ -> Task.FromResult(Error "unused")
      CancelJob = fun _ -> Task.FromResult(Error "unused")
      GetMessages = fun _ -> Task.FromResult(Ok [])
      SendMessage = fun _ -> Task.FromResult(Error "unused")
      SimulatePayment = fun _ -> Task.FromResult(Error "unused")
      SubmitRating = fun _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.65, -79.38)) }

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 2; ProviderName = "Mike's Plumbing"
      ServiceId = 3; ServiceName = "Plumbing"; State = state; Price = 85m
      ScheduledFor = "Now"; Lat = 43.65; Lng = -79.38; Address = "1 Demo St" }

let up msg model = Update.update stubDeps msg model |> fst

[<Fact>]
let ``splash advances to Login`` () =
    Assert.Equal(Login, (up SplashDone { Model.initial with Screen = Splash }).Screen)

[<Fact>]
let ``login stores session and lands on Home with empty history`` () =
    let resp = { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(Some { Token = "fake-customer-1"; UserId = 1; DisplayName = "John" }, m.Session)

[<Fact>]
let ``navigate pushes current screen`` () =
    let m = up (Navigate Catalog) { Model.initial with Screen = Home }
    Assert.Equal(Catalog, m.Screen)
    Assert.Equal<Screen list>([Home], m.History)

[<Fact>]
let ``job created goes to Tracking with job stored`` () =
    let m = up (JobCreated (mkJob 42 "Scheduled")) { Model.initial with Screen = Booking (2, 3) }
    Assert.Equal(Tracking 42, m.Screen)
    Assert.True(m.Jobs |> List.exists (fun j -> j.Id = 42))

[<Fact>]
let ``api error sets banner`` () =
    Assert.Equal(Some "boom", (up (ApiError "boom") Model.initial).Error)
