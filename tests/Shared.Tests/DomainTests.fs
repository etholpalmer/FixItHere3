module FixItHere.Shared.Tests.DomainTests

open Xunit
open FixItHere.Shared

[<Fact>]
let ``there are exactly seven catalog services ending with HVAC`` () =
    Assert.Equal(7, List.length ServiceNames.all)
    Assert.Equal<string list>(
        ["Plumbing"; "Electrical"; "Painting"; "Mechanic"; "Moving"; "Cleaning"; "HVAC"],
        ServiceNames.all)
