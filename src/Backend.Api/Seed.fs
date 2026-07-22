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

    // Full names, and a Toronto-plausible mix of them. First-name-only customers
    // ("John", "Mary") read as seed data the moment they appear next to a real
    // street address.
    let customerNames =
        [ "John Reyes"; "Mary Okonkwo"; "Steve Lindqvist"; "Susan Chaudhry"; "Bob Tremblay"
          "Alice Nakamura"; "Tom Belanger"; "Grace Adeyemi"; "Henry Vasquez"; "Ivy Chen"
          "Jack O'Brien"; "Karen Silva"; "Leo Mancini"; "Mona Haddad"; "Nate Fitzgerald"
          "Olive Kowalski"; "Paul Dhillon"; "Quinn Gallagher"; "Rita Moreau"; "Sam Petrov" ]
    db.Customers.AddRange(customerNames |> List.mapi (fun i n ->
        let place = customerPlace i
        { Id = 0; Name = n; Email = Auth.customerEmail i n
          Address = Places.fullAddress place
          // Well-known test numbers, last four only — a receipt can name a card
          // without inventing plausible-looking PANs.
          CardBrand = (if i % 3 = 0 then "Visa" elif i % 3 = 1 then "Mastercard" else "Amex")
          CardLast4 = (if i % 3 = 0 then "4242" elif i % 3 = 1 then "4444" else "0005")
          Lat = place.Lat; Lng = place.Lng })) |> ignore

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
          // Server-rendered initials rather than a file path. The old
          // "/img/provider-N.png" pointed at images that were never shipped, so
          // every avatar 404'd the moment a view tried to render one.
          Vehicle = vehicle; PhotoUrl = sprintf "/avatar/provider/%d.svg" (i + 1) })) |> ignore
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
          Stars = 3 + rng.Next(0, 3); Comment = comments.[rng.Next(comments.Length)]
          // Reviews are dated relative to the fixed Epoch, so the seed stays
          // byte-identical while each review still carries a plausible date.
          CreatedAt = DateTimeOffset.Parse(Epoch).AddDays(float -(j.Id % 60)).ToString("o") })) |> ignore

    db.Messages.AddRange(doneJobs |> List.truncate 20 |> List.map (fun j ->
        { Id = 0; JobId = j.Id; SenderId = j.CustomerId; SenderRole = "Customer"
          Text = "Hi, see you soon!"; PhotoBase64 = null
          SentAt = Epoch; Seen = true })) |> ignore
    db.SaveChanges() |> ignore
