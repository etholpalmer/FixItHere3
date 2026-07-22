module FixItHere.Backend.Tests.SeedTests

open System.Linq
open Xunit
open FixItHere.Shared
open FixItHere.Backend.Db
open FixItHere.Backend
open FixItHere.Backend.Tests.DbTests

[<Fact>]
let ``seed produces required counts`` () =
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    Assert.Equal(7,  db.Services.Count())
    Assert.Equal(20, db.Customers.Count())
    Assert.Equal(20, db.Providers.Count())
    Assert.Equal(50, db.Jobs.Count(fun j -> j.State = "Completed" || j.State = "Closed"))
    Assert.Equal(30, db.Jobs.Count(fun j -> j.State = "Scheduled"))
    Assert.True(db.Ratings.Count() > 0)
    Assert.True(db.Messages.Count() > 0)

[<Fact>]
let ``named personas exist with correct services`` () =
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    // Full names, not first names: a roster of "John, Mary, Steve" reads as
    // seed data the moment a second screen shows it.
    for name in ["John Reyes"; "Mary Okonkwo"; "Steve Lindqvist"; "Susan Chaudhry"; "Bob Tremblay"] do
        Assert.True(db.Customers.Any(fun c -> c.Name = name), name)
    let svcId name = db.Services.Single(fun s -> s.Name = name).Id
    let check biz svc =
        Assert.Equal(svcId svc, db.Providers.Single(fun p -> p.BusinessName = biz).ServiceId)
    check "Mike's Plumbing" "Plumbing"
    check "Joe Electric" "Electrical"
    check "Rapid Tire Repair" "Mechanic"
    check "Elite HVAC" "HVAC"

[<Fact>]
let ``seed is deterministic across two runs`` () =
    let snapshot () =
        let db, conn = makeDb ()
        Seed.run db
        let s =
            db.Jobs.OrderBy(fun j -> j.Id)
            |> Seq.map (fun j -> sprintf "%d|%s|%M|%s" j.Id j.State j.Price j.ScheduledFor)
            |> String.concat ";"
        conn.Dispose()
        s
    Assert.Equal(snapshot (), snapshot ())

// ---------------------------------------------------------------------------
// Geography. The seed used to draw coordinates from a uniform bounding box,
// ~15-18% of which is Lake Ontario — so customers, and the jobs that inherit
// their point, could sit in open water. Rather than re-deriving a shoreline,
// the assertion is that every coordinate came from the curated anchor list:
// exact, cheap, and it cannot drift from the data.
// ---------------------------------------------------------------------------

[<Fact>]
let ``every seeded coordinate comes from the curated place list`` () =
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    for c in db.Customers do
        Assert.True(Places.isKnownPlace c.Lat c.Lng,
                    sprintf "customer %d (%s) is not at a known place: %f, %f" c.Id c.Name c.Lat c.Lng)
    for p in db.Providers do
        Assert.True(Places.isKnownPlace p.Lat p.Lng,
                    sprintf "provider %d (%s) is not at a known place: %f, %f" p.Id p.BusinessName p.Lat p.Lng)
    for j in db.Jobs do
        Assert.True(Places.isKnownPlace j.Lat j.Lng,
                    sprintf "job %d is not at a known place: %f, %f" j.Id j.Lat j.Lng)

[<Fact>]
let ``no seeded job carries a placeholder address`` () =
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    for j in db.Jobs do
        Assert.DoesNotContain("Demo Street", j.Address)
        Assert.DoesNotContain("My location", j.Address)
        Assert.Contains(",", j.Address)   // "501 Bloor St W, The Annex"

[<Fact>]
let ``providers do not stand on their customers' doorsteps`` () =
    // Customers take the first 20 anchors and providers the next 20, so a
    // provider marker never renders exactly on top of a job pin.
    let db, conn = makeDb ()
    use _ = conn
    Seed.run db
    let customerPoints = db.Customers |> Seq.map (fun c -> c.Lat, c.Lng) |> Set.ofSeq
    for p in db.Providers do
        Assert.DoesNotContain((p.Lat, p.Lng), customerPoints)
