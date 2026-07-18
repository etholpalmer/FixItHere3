module FixItHere.Provider.Tests.UpdateTests

open System.Threading.Tasks
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Provider

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 4; ProviderName = "Elite HVAC"
      ServiceId = 7; ServiceName = "HVAC"; State = state; Price = 85m
      ScheduledFor = "Now"; Lat = 43.70; Lng = -79.40; Address = "1 Demo St" }

[<Fact>]
let ``nav push and back mirror customer app`` () =
    let m = Nav.push { Model.initial with Screen = Home } DevSettings
    Assert.Equal(DevSettings, m.Screen)
    Assert.Equal(Home, (Nav.back m).Screen)

[<Fact>]
let ``activeJob picks the in-flight job over scheduled`` () =
    let m = { Model.initial with Jobs = [mkJob 1 "Scheduled"; mkJob 2 "EnRoute"; mkJob 3 "Closed"] }
    Assert.Equal(Some 2, activeJob m |> Option.map (fun j -> j.Id))

[<Fact>]
let ``activeJob is None when nothing in flight`` () =
    let m = { Model.initial with Jobs = [mkJob 3 "Closed"; mkJob 4 "Cancelled"] }
    Assert.Equal(None, activeJob m |> Option.map (fun j -> j.Id))

[<Fact>]
let ``slider position interpolates and clamps`` () =
    Assert.Equal((5.0, 5.0), Slider.position (0.0, 0.0) (10.0, 10.0) 0.5)
    Assert.Equal((10.0, 10.0), Slider.position (0.0, 0.0) (10.0, 10.0) 1.7)
    Assert.Equal((0.0, 0.0), Slider.position (0.0, 0.0) (10.0, 10.0) -0.3)

let stubDeps : ProviderApiDeps =
    { Login = fun _ -> Task.FromResult(Ok { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" })
      SetOnline = fun _ b -> Task.FromResult(Error "unused")
      GetMyJobs = fun _ -> Task.FromResult(Ok [])
      Accept = fun _ -> Task.FromResult(Error "unused")
      Enroute = fun _ -> Task.FromResult(Error "unused")
      Arrive = fun _ -> Task.FromResult(Error "unused")
      Start = fun _ -> Task.FromResult(Error "unused")
      Complete = fun _ -> Task.FromResult(Error "unused")
      UpdateLocation = fun _ _ _ -> Task.FromResult(Error "unused")
      GetMessages = fun _ -> Task.FromResult(Ok [])
      SendMessage = fun _ -> Task.FromResult(Error "unused")
      SimulatePayment = fun _ -> Task.FromResult(Error "unused")
      SubmitRating = fun _ -> Task.FromResult(Error "unused")
      StartDemo = fun _ _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.70, -79.45))
      SendTyping = fun _ _ -> ()
      SendSeen = fun _ _ -> () }

let up msg model = Update.update stubDeps msg model |> fst

[<Fact>]
let ``login lands Home with session`` () =
    let resp = { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Equal(Some 4, m.Session |> Option.map (fun s -> s.UserId))

[<Fact>]
let ``online changed updates flag and toast`` () =
    let dto : ProviderDto =
        { Id = 4; BusinessName = "Elite HVAC"; ServiceId = 7; ServiceName = "HVAC"
          Rating = 4.5; RatingCount = 3; Lat = 43.7; Lng = -79.4
          Online = true; Vehicle = "Box truck"; PhotoUrl = "" }
    let m = up (OnlineChanged dto) Model.initial
    Assert.True(m.Online)

[<Fact>]
let ``job actioned upserts and navigates to ActiveJob on accept from JobDetail`` () =
    let m0 = { Model.initial with Screen = JobDetail 7; Jobs = [mkJob 7 "Scheduled"] }
    let m = up (JobActioned (mkJob 7 "Scheduled")) m0
    Assert.Equal(ActiveJob 7, m.Screen)

[<Fact>]
let ``job actioned elsewhere just upserts`` () =
    let m0 = { Model.initial with Screen = ActiveJob 7; Jobs = [mkJob 7 "EnRoute"] }
    let m = up (JobActioned (mkJob 7 "Arrived")) m0
    Assert.Equal(ActiveJob 7, m.Screen)
    Assert.Equal("Arrived", (m.Jobs |> List.find (fun j -> j.Id = 7)).State)
