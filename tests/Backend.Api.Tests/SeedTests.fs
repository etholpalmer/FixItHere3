module FixItHere.Backend.Tests.SeedTests

open System.Linq
open Xunit
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
    for name in ["John"; "Mary"; "Steve"; "Susan"; "Bob"] do
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
