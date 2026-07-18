module FixItHere.Shared.Tests.DtoTests

open Xunit
open FixItHere.Shared.Dtos

[<Fact>]
let ``Envelope.ok wraps data`` () =
    let e = Envelope.ok 42
    Assert.True(e.Success)
    Assert.Equal(42, e.Data)
    Assert.Null(e.Error)

[<Fact>]
let ``Envelope.fail carries message`` () =
    let e = Envelope.fail "boom"
    Assert.False(e.Success)
    Assert.Equal("boom", e.Error)
