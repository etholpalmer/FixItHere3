module FixItHere.Backend.Movement

open System
open System.Linq
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Hosting
open FixItHere.Shared
open FixItHere.Backend.Db
open FixItHere.Backend.Services

/// Drive a provider from where they are to their customer, in demo time.
///
/// This is the single travel interpolator. It runs when `DepartEnRoute` is
/// applied — from the real in-app **Depart** (the `/jobs/{id}/enroute` endpoint)
/// and from the scripted `/dev` timeline alike — so that departing *always*
/// starts the car moving. Without it, tapping Depart flipped the state but left
/// the provider dot sitting at its origin: the two dots never met, the map never
/// re-fitted, and the customer's ETA never counted down (so a normally-progressing
/// job read "Late by …").
///
/// Paces on the demo clock, not real seconds, so at 60x the car keeps step with
/// the countdown. Steps in twelve increments; each `LocationUpdated` is what the
/// clients redraw and re-`fitBounds` on, which is what makes the map zoom in as
/// the dots converge. Bails the instant the job leaves `EnRoute` (arrived,
/// cancelled), so a provider who taps Arrived early is not dragged onward, and a
/// stale drive cannot move a job it no longer owns.
///
/// Fire-and-forget from the endpoint; `await`-ed from the scripted timeline,
/// which applies `Arrive` only once this returns.
let driveEnRoute (sp: IServiceProvider) (jobId: int) : Task =
    task {
        use scope = sp.CreateScope()
        let db = scope.ServiceProvider.GetRequiredService<AppDb>()
        let hub = scope.ServiceProvider.GetRequiredService<IBroadcaster>()
        let clock = sp.GetRequiredService<Clock.DemoClockService>()
        let lifetime = sp.GetRequiredService<IHostApplicationLifetime>()
        let ct = lifetime.ApplicationStopping

        match db.Jobs.AsNoTracking().SingleOrDefault(fun j -> j.Id = jobId) |> Option.ofObj with
        | None -> ()
        | Some job ->
            let prov = db.Providers.AsNoTracking().Single(fun p -> p.Id = job.ProviderId)
            let startLat, startLng = prov.Lat, prov.Lng
            let km = Geo.distanceKm (startLat, startLng) (job.Lat, job.Lng)
            // The drive takes as long as the ETA says it takes — any other number
            // here is the contradiction between the car and the countdown.
            let journey = Travel.durationFor km
            let steps = 12

            /// A fresh read each tick: the provider may tap Arrived mid-drive.
            let stillEnRoute () =
                db.Jobs.AsNoTracking().SingleOrDefault(fun j -> j.Id = jobId)
                |> Option.ofObj
                |> Option.map (fun j -> j.State = "EnRoute")
                |> Option.defaultValue false

            let waitDemo (span: TimeSpan) =
                task {
                    let until = clock.Now() + span
                    while clock.Now() < until && not ct.IsCancellationRequested do
                        do! Task.Delay(100, ct)
                }

            let moveTo (lat: float) (lng: float) =
                task {
                    let tracked = db.Providers.Single(fun p -> p.Id = job.ProviderId)
                    db.Entry(tracked).CurrentValues.SetValues({ tracked with Lat = lat; Lng = lng })
                    db.SaveChanges() |> ignore
                    do! hub.LocationUpdated
                            { ProviderId = job.ProviderId; Lat = lat; Lng = lng
                              UpdatedAt = (clock.Now()).ToString "o" }
                }

            let mutable i = 1
            while i <= steps && not ct.IsCancellationRequested && stillEnRoute () do
                do! waitDemo (journey / float steps)
                if stillEnRoute () then
                    let t = float i / float steps
                    do! moveTo (startLat + (job.Lat - startLat) * t) (startLng + (job.Lng - startLng) * t)
                i <- i + 1
    } :> Task
