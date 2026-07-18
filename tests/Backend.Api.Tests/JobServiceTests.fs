module FixItHere.Backend.Tests.JobServiceTests

open System.Linq
open Xunit
open FixItHere.Shared
open FixItHere.Backend
open FixItHere.Backend.Services
open FixItHere.Backend.Tests.DbTests

let setup () =
    let db, conn = makeDb ()
    Seed.run db
    JobService(db, NullBroadcaster()), db, conn

[<Fact>]
let ``valid transition persists new state`` () =
    let svc, db, conn = setup ()
    use _ = conn
    let job = db.Jobs.First(fun j -> j.State = "Scheduled")
    let result = (svc.Apply job.Id DepartEnRoute).Result
    match result with
    | Ok dto -> Assert.Equal("EnRoute", dto.State)
    | Error e -> failwith e
    Assert.Equal("EnRoute", db.Jobs.Single(fun j -> j.Id = job.Id).State)

[<Fact>]
let ``invalid transition returns Error and does not persist`` () =
    let svc, db, conn = setup ()
    use _ = conn
    let job = db.Jobs.First(fun j -> j.State = "Scheduled")
    match (svc.Apply job.Id CompleteWork).Result with
    | Error msg -> Assert.Contains("Invalid transition", msg)
    | Ok _ -> failwith "expected Error"
    Assert.Equal("Scheduled", db.Jobs.Single(fun j -> j.Id = job.Id).State)
