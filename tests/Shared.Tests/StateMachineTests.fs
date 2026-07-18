module FixItHere.Shared.Tests.StateMachineTests

open Xunit
open FsCheck.Xunit
open FixItHere.Shared
open FixItHere.Shared.StateMachine

[<Fact>]
let ``happy path walks scheduled to closed`` () =
    let step st ev = Result.bind (fun s -> transition s ev) st
    let final =
        Ok Scheduled
        |> fun s -> step s DepartEnRoute
        |> fun s -> step s Arrive
        |> fun s -> step s StartWork
        |> fun s -> step s CompleteWork
        |> fun s -> step s RateAndClose
    Assert.Equal(Ok Closed, final)

[<Fact>]
let ``cannot start work before arriving`` () =
    match transition EnRoute StartWork with
    | Error msg -> Assert.Contains("EnRoute", msg)
    | Ok s -> failwithf "expected rejection, got %A" s

[<Theory>]
[<InlineData("Scheduled")>] [<InlineData("EnRoute")>]
[<InlineData("Arrived")>]   [<InlineData("InProgress")>]
let ``cancel allowed from any pre-completed state`` (name: string) =
    let st =
        match name with
        | "Scheduled" -> Scheduled | "EnRoute" -> EnRoute
        | "Arrived" -> Arrived | _ -> InProgress
    Assert.Equal(Ok Cancelled, transition st Cancel)

[<Property>]
let ``terminal states accept no events`` (ev: JobEvent) =
    [Closed; Cancelled]
    |> List.forall (fun st ->
        match transition st ev with Error _ -> true | Ok _ -> false)
