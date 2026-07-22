module FixItHere.Backend.Seed

open System
open FixItHere.Shared
open FixItHere.Backend.Db

/// Fixed timestamp for SEEDED rows, so the seed stays byte-identical across runs.
let Epoch = "2026-01-01T00:00:00Z"

/// Wall-clock timestamp for rows created at RUNTIME. Seeded and live rows shared
/// Epoch, which made live chat messages indistinguishable from seeded ones and
/// left every message and location stamped 2026-01-01.
let nowIso () = DateTimeOffset.UtcNow.ToString("o")

/// Deterministic: fixed name lists, Random(42), fixed epoch. No wall clock.
let run (db: AppDb) =
    let rng = Random(42)
    // Coordinates come from Places.all — real GTA addresses, all inland. The
    // previous uniform bounding box put ~15-18% of everything in Lake Ontario,
    // and jobs inherit their customer's point, so the defect duplicated.
    // Customers take the first 20 anchors, providers the next 20, so no
    // provider is ever standing exactly on a customer's doorstep.
    let customerPlace i = Places.at i
    let providerPlace i = Places.at (i + 20)

    let services =
        ServiceNames.all |> List.map (fun n -> { Id = 0; Name = n })
    db.Services.AddRange services |> ignore
    db.SaveChanges() |> ignore
    let svc name = db.Services.Local |> Seq.find (fun s -> s.Name = name)

    let customerNames =
        [ "John"; "Mary"; "Steve"; "Susan"; "Bob"
          "Alice"; "Tom"; "Grace"; "Henry"; "Ivy"
          "Jack"; "Karen"; "Leo"; "Mona"; "Nate"
          "Olive"; "Paul"; "Quinn"; "Rita"; "Sam" ]
    db.Customers.AddRange(customerNames |> List.mapi (fun i n ->
        let place = customerPlace i
        { Id = 0; Name = n; Email = Auth.customerEmail i n
          Address = Places.fullAddress place; Lat = place.Lat; Lng = place.Lng })) |> ignore

    let namedProviders =
        [ "Mike's Plumbing", "Plumbing", "White van"
          "Joe Electric", "Electrical", "Blue pickup"
          "Rapid Tire Repair", "Mechanic", "Service truck"
          "Elite HVAC", "HVAC", "Box truck" ]
    let fillerProviders =
        [ "Pro Painters Co", "Painting"; "Swift Movers", "Moving"
          "Sparkle Clean", "Cleaning"; "DrainMasters", "Plumbing"
          "Volt Bros", "Electrical"; "ColorWorks", "Painting"
          "GearHeads Mobile", "Mechanic"; "Box & Dolly", "Moving"
          "FreshNest Cleaning", "Cleaning"; "CoolFlow HVAC", "HVAC"
          "PipeDream Plumbing", "Plumbing"; "Amp It Up", "Electrical"
          "BrushStrokes", "Painting"; "WrenchWorks", "Mechanic"
          "HaulStars", "Moving"; "PolishPros", "Cleaning" ]
    let providers =
        (namedProviders |> List.map (fun (b, s, v) -> b, s, v))
        @ (fillerProviders |> List.map (fun (b, s) -> b, s, "Van"))
    db.Providers.AddRange(providers |> List.mapi (fun i (biz, s, vehicle) ->
        let place = providerPlace i
        { Id = 0; BusinessName = biz; Email = Auth.providerEmail biz; ServiceId = (svc s).Id
          Lat = place.Lat; Lng = place.Lng; Online = true
          Vehicle = vehicle; PhotoUrl = sprintf "/img/provider-%d.png" (rng.Next(1, 9)) })) |> ignore
    db.SaveChanges() |> ignore

    let customers = db.Customers.Local |> Seq.toArray
    let provs = db.Providers.Local |> Seq.toArray
    let svcNameOf serviceId =
        (db.Services.Local |> Seq.find (fun sv -> sv.Id = serviceId)).Name
    let mkJob i state =
        let c = customers.[i % customers.Length]
        let p = provs.[(i * 3) % provs.Length]
        { Id = 0; CustomerId = c.Id; ProviderId = p.Id; ServiceId = p.ServiceId
          State = state
          // Real invoices vary around the quote — the work runs long or short.
          // +/- 20% in 5-minute steps off the trade's typical duration keeps the
          // spread believable and tied to the service, instead of a bare random
          // number that had nothing to do with the trade.
          Price =
              let rate = ServiceRate.forService (svcNameOf p.ServiceId)
              let jitter = rate.TypicalMinutes * (80 + rng.Next(0, 9) * 5) / 100
              ServiceRate.total rate jitter
          ScheduledFor = DateTimeOffset.Parse(Epoch).AddHours(float i).ToString("o")
          Lat = c.Lat; Lng = c.Lng
          Address = c.Address }
    // 50 finished (alternate Completed/Closed), 30 pending
    let finished = [ for i in 0 .. 49 -> mkJob i (if i % 2 = 0 then "Closed" else "Completed") ]
    let pending  = [ for i in 50 .. 79 -> mkJob i "Scheduled" ]
    db.Jobs.AddRange(finished @ pending) |> ignore
    db.SaveChanges() |> ignore

    let comments = [ "Great work!"; "On time and professional."; "Would book again."; "Fixed it fast."; "Friendly and tidy." ]
    let doneJobs = db.Jobs.Local |> Seq.filter (fun j -> j.State = "Closed") |> Seq.toList
    db.Ratings.AddRange(doneJobs |> List.map (fun j ->
        { Id = 0; JobId = j.Id
          RaterId = j.CustomerId; RaterRole = "Customer"
          RateeId = j.ProviderId; RateeRole = "Provider"
          Stars = 3 + rng.Next(0, 3); Comment = comments.[rng.Next(comments.Length)] })) |> ignore

    db.Messages.AddRange(doneJobs |> List.truncate 20 |> List.map (fun j ->
        { Id = 0; JobId = j.Id; SenderId = j.CustomerId; SenderRole = "Customer"
          Text = "Hi, see you soon!"; PhotoBase64 = null
          SentAt = Epoch; Seen = true })) |> ignore
    db.SaveChanges() |> ignore
