module FixItHere.Backend.Tests.DbTests

open System.Linq
open Microsoft.Data.Sqlite
open Microsoft.EntityFrameworkCore
open Xunit
open FixItHere.Backend.Db

let makeDb () =
    let conn = new SqliteConnection("DataSource=:memory:")
    conn.Open()
    let opts = DbContextOptionsBuilder<AppDb>().UseSqlite(conn).Options
    let db = new AppDb(opts)
    db.Database.EnsureCreated() |> ignore
    db, conn

[<Fact>]
let ``job round-trips through sqlite`` () =
    let db, conn = makeDb ()
    use _ = conn
    let job =
        { Id = 0; CustomerId = 1; ProviderId = 2; ServiceId = 3
          State = "Scheduled"; Price = 85.00m
          ScheduledFor = "2026-01-01T09:00:00Z"
          PromisedStart = "2026-01-01T09:00:00Z"
          ProposedStart = ""; ProposedBy = ""
          ProposalReason = ""; ProposalExpiresAt = ""; IsDemoTracked = false; IsAccepted = false; CancelledBy = ""
          Lat = 43.65; Lng = -79.38; Address = "1 Yonge St, Toronto" }
    db.Jobs.Add(job) |> ignore
    db.SaveChanges() |> ignore
    let loaded = db.Jobs.Single()
    Assert.Equal("Scheduled", loaded.State)
    Assert.Equal(85.00m, loaded.Price)
