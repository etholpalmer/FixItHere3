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
