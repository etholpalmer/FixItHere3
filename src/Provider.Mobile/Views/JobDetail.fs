module FixItHere.Provider.Views.JobDetail

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.ClientShared
open FixItHere.Provider
open FixItHere.Shared

let view (model: Model) (jobId: int) =
    let job = model.Jobs |> List.tryFind (fun j -> j.Id = jobId)
    ScrollView(
     (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        match job with
        | Some j ->
            Label(j.ServiceName).font(size = 24.)
            Label(sprintf "Customer: %s" j.CustomerName)
            Label(sprintf "Address: %s" j.Address)
                .lineBreakMode(Microsoft.Maui.LineBreakMode.WordWrap)
            // Format.money, not "$%M". A price rendered as "$277.5" is a
            // number that escaped, not a price.
            Label(Format.money j.Price).font(size = 20.)
            // BookingSlot.describe, not the raw field. This screen showed
            // "Scheduled for: 2026-01-01T00:31:31.5622679+00:00" to a user —
            // the loudest tell the walkthrough found, and a straight leak of
            // storage format onto the product surface.
            Label(sprintf "Arrive %s" (BookingSlot.describe (rescheduleOf j).PromisedStart model.DemoNow))
            match countdownFor model j with
            | Some c ->
                Label(Countdown.oneLine c).font(size = 15.)
            | None -> ()
            Button("Accept", AcceptJob j.Id)
        | None -> Label("Job not found")
     }).padding(24.))
