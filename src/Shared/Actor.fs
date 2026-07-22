namespace FixItHere.Shared

/// Which id space an id belongs to.
///
/// Customer ids and Provider ids are independent sequences that both start at
/// 1, so a bare int is not an identity — it is half of one. This project has
/// now shipped that bug four separate times (chat messages, typing/seen
/// receipts, ratings, and SignalR group keys), each time fixing exactly the one
/// call site that was reported. The documented demo pair is customer 1 with
/// provider 1, so the collision is not a corner case: it is the default.
///
/// `RequireQualifiedAccess` because `Customer` and `Provider` are also record
/// names in the backend's `Db` module, and an unqualified case would shadow
/// them in any file that opens both.
[<RequireQualifiedAccess>]
type ActorRole =
    | Customer
    | Provider

/// A whole identity. Compare these, never bare ids.
type Actor = { Role: ActorRole; Id: int }

module ActorRole =
    /// The strings already on the wire — DTOs carry the role as text because
    /// System.Text.Json has no idiomatic representation for an F# union.
    let toWire =
        function
        | ActorRole.Customer -> "Customer"
        | ActorRole.Provider -> "Provider"

    /// Exact match, not case-insensitive: every producer of this string is our
    /// own code, so a variant spelling is a bug to surface rather than absorb.
    let ofWire (s: string) =
        match s with
        | "Customer" -> Some ActorRole.Customer
        | "Provider" -> Some ActorRole.Provider
        | _ -> None

    let counterpart =
        function
        | ActorRole.Customer -> ActorRole.Provider
        | ActorRole.Provider -> ActorRole.Customer

module Actor =
    let customer id = { Role = ActorRole.Customer; Id = id }
    let provider id = { Role = ActorRole.Provider; Id = id }

    /// `None` on an unrecognised role, deliberately. Defaulting an unknown role
    /// to Customer is precisely the mechanism by which a provider's id silently
    /// matches a customer's — the failure this type exists to make impossible.
    let ofWire (id: int) (role: string) =
        ActorRole.ofWire role |> Option.map (fun r -> { Role = r; Id = id })

    let toWire (a: Actor) = a.Id, ActorRole.toWire a.Role

    /// The comparison the whole type exists for. An unparseable role is "not
    /// me", which is the safe direction: it under-matches rather than
    /// over-matches.
    let isWire (me: Actor) (id: int) (role: string) =
        match ofWire id role with
        | Some other -> me = other
        | None -> false

    /// Group key for job-scoped signalling. Same shape the hub already uses.
    let groupKey (a: Actor) = sprintf "%s-%d" (ActorRole.toWire a.Role) a.Id
