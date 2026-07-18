module FixItHere.Provider.Views.JobDetail

open Fabulous.Maui
open type Fabulous.Maui.View
open FixItHere.Provider

let view (model: Model) (jobId: int) =
    let job = model.Jobs |> List.tryFind (fun j -> j.Id = jobId)
    (VStack(spacing = 12.) {
        Button("← Back", GoBack)
        match job with
        | Some j ->
            Label(j.ServiceName).font(size = 24.)
            Label(sprintf "Customer: %s" j.CustomerName)
            Label(sprintf "Address: %s" j.Address)
            Label(sprintf "Price: $%M" j.Price)
            Label(sprintf "Scheduled for: %s" j.ScheduledFor)
            Button("Accept", AcceptJob j.Id)
        | None -> Label("Job not found")
    }).padding(24.)
