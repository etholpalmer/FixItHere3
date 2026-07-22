module FixItHere.Shared.Tests.FormatTests

open Xunit
open FixItHere.Shared

[<Fact>]
let ``a malformed timestamp formats to empty, never throws`` () =
    // Total by design: a blank chat timestamp is cosmetic, an exception on the
    // tracking screen ends the demo.
    Assert.Equal("", Format.clockTime "not a date")
    Assert.Equal("", Format.clockTime "")
    Assert.Equal("", Format.clockTime null)
    Assert.Equal("", Format.shortDate "nonsense")

[<Fact>]
let ``a real timestamp renders a clock time and a short date`` () =
    let iso = System.DateTimeOffset(2026, 1, 12, 15, 42, 0, System.TimeSpan.Zero).ToString("o")
    Assert.Matches(@"^\d{1,2}:\d{2} (AM|PM)$", Format.clockTime iso)
    Assert.Matches(@"^\d{1,2} \w{3}$", Format.shortDate iso)

[<Fact>]
let ``display name abbreviates the surname`` () =
    Assert.Equal("Mary O.", Format.displayName "Mary Okonkwo")
    Assert.Equal("Jack O.", Format.displayName "Jack O'Brien")
    Assert.Equal("Cher", Format.displayName "Cher")
    Assert.Equal("Someone", Format.displayName "")

[<Fact>]
let ``durations read as durations`` () =
    Assert.Equal("45m", Format.duration 45)
    Assert.Equal("2h", Format.duration 120)
    Assert.Equal("1h 30m", Format.duration 90)
