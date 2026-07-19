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
    // GTA-ish bounding box for coordinates
    let lat () = 43.55 + rng.NextDouble() * 0.30
    let lng () = -79.75 + rng.NextDouble() * 0.55

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
    db.Customers.AddRange(customerNames |> List.map (fun n ->
        { Id = 0; Name = n; Lat = lat (); Lng = lng () })) |> ignore

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
    db.Providers.AddRange(providers |> List.map (fun (biz, s, vehicle) ->
        { Id = 0; BusinessName = biz; ServiceId = (svc s).Id
          Lat = lat (); Lng = lng (); Online = true
          Vehicle = vehicle; PhotoUrl = sprintf "/img/provider-%d.png" (rng.Next(1, 9)) })) |> ignore
    db.SaveChanges() |> ignore

    let customers = db.Customers.Local |> Seq.toArray
    let provs = db.Providers.Local |> Seq.toArray
    let mkJob i state =
        let c = customers.[i % customers.Length]
        let p = provs.[(i * 3) % provs.Length]
        { Id = 0; CustomerId = c.Id; ProviderId = p.Id; ServiceId = p.ServiceId
          State = state
          Price = decimal (40 + rng.Next(0, 25) * 5)
          ScheduledFor = DateTimeOffset.Parse(Epoch).AddHours(float i).ToString("o")
          Lat = c.Lat; Lng = c.Lng
          Address = sprintf "%d Demo Street" (100 + i) }
    // 50 finished (alternate Completed/Closed), 30 pending
    let finished = [ for i in 0 .. 49 -> mkJob i (if i % 2 = 0 then "Closed" else "Completed") ]
    let pending  = [ for i in 50 .. 79 -> mkJob i "Scheduled" ]
    db.Jobs.AddRange(finished @ pending) |> ignore
    db.SaveChanges() |> ignore

    let comments = [ "Great work!"; "On time and professional."; "Would book again."; "Fixed it fast."; "Friendly and tidy." ]
    let doneJobs = db.Jobs.Local |> Seq.filter (fun j -> j.State = "Closed") |> Seq.toList
    db.Ratings.AddRange(doneJobs |> List.map (fun j ->
        { Id = 0; JobId = j.Id; RaterId = j.CustomerId; RateeId = j.ProviderId
          Stars = 3 + rng.Next(0, 3); Comment = comments.[rng.Next(comments.Length)] })) |> ignore

    db.Messages.AddRange(doneJobs |> List.truncate 20 |> List.map (fun j ->
        { Id = 0; JobId = j.Id; SenderId = j.CustomerId; SenderRole = "Customer"
          Text = "Hi, see you soon!"; PhotoBase64 = null
          SentAt = Epoch; Seen = true })) |> ignore
    db.SaveChanges() |> ignore
