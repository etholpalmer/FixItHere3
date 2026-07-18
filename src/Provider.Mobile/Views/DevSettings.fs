module FixItHere.Provider.Views.DevSettings

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let cities = [ "Toronto", (43.6532, -79.3832); "Mississauga", (43.5890, -79.6441); "Brampton", (43.7315, -79.7624) ]

let view (model: Model) =
    let lat, lng = model.MyLocation
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        Label("Developer Settings").font(size = 24.)
        Label(sprintf "Current location: %.4f, %.4f" lat lng)
        Label(if model.UseRealGps then "Mode: Real GPS" else "Mode: Simulated GPS")
        Button("Use Real GPS", SetUseRealGps true)
        Button("Use Simulated GPS", SetUseRealGps false)
        Label("Teleport to:").font(size = 18.)
        for (name, pos) in cities do
            Button(name, SetLocation pos)
        Label("Move along route").font(size = 18.)
        (HStack(spacing = 6.) {
            for pct in [ 0.0; 0.25; 0.5; 0.75; 1.0 ] do
                Button(sprintf "%d%%" (int (pct * 100.0)), SliderMoved pct)
        }).centerHorizontal()
        Button("▶ Start Demo (as this provider)", StartDemo)
    }).padding(24.)
