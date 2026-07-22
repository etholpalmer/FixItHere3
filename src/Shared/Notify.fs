namespace FixItHere.Shared

open System

/// How loud a notification is, and therefore how it looks.
///
/// Untyped strings meant every notification rendered identically — "Provider
/// Accepted" and "Your provider never arrived" in the same grey bar. A kind is
/// the minimum needed for the second to look different from the first.
[<RequireQualifiedAccess>]
type NoticeKind =
    | Info
    | Success
    | Warning
    /// Something the user must answer, not merely read. These never expire on
    /// their own: a reschedule request that quietly vanishes after four seconds
    /// is worse than no request at all.
    | Ask

type Notice =
    { Id: int
      Kind: NoticeKind
      Text: string
      /// Which job this is about. `None` is for account-level messages ("You
      /// are Online"); everything job-scoped carries its id so a notice can be
      /// tapped through to the thing it concerns, and so notices for a job that
      /// ends can be cleared together.
      JobId: int option
      /// Demo instant. `None` for `Ask`, which waits for an answer.
      ///
      /// Expiry in *demo* time means pausing the clock to talk over a beat also
      /// pauses dismissal — exactly right when an operator stops mid-sentence,
      /// and impossible if this were a real-time timer.
      ExpiresAt: DateTimeOffset option }

/// A small, ordered queue of notices.
///
/// Both apps held a single `Toast: string option`. A second notification
/// silently replaced the first, so the two-sided moments this phase is built
/// around — provider proposes, customer answers — could overwrite each other
/// mid-demo with nothing to show it happened.
module Notify =

    /// How long an ordinary notice stays up, in demo minutes. Long enough to
    /// read at 1x; at 60x it is gone in two seconds, which is correct — the
    /// world moved on that fast too.
    let lifetime = TimeSpan.FromMinutes 3.0

    /// Beyond this the stack stops being a notification and starts being a log.
    /// Oldest go first; `Ask` notices are never dropped this way because
    /// discarding an unanswered question is a correctness bug, not a display
    /// one.
    let maxVisible = 3

    let create (id: int) (kind: NoticeKind) (jobId: int option) (demoNow: DateTimeOffset) (text: string) =
        { Id = id
          Kind = kind
          Text = text
          JobId = jobId
          ExpiresAt = (match kind with NoticeKind.Ask -> None | _ -> Some (demoNow + lifetime)) }

    /// Newest first — the order they are read in.
    let push (notice: Notice) (queue: Notice list) =
        let withNew = notice :: queue
        let asks, rest = withNew |> List.partition (fun n -> n.Kind = NoticeKind.Ask)
        asks @ (rest |> List.truncate (max 0 (maxVisible - List.length asks)))

    /// Drop what demo time has passed. Pure in `demoNow`, so it can ride the
    /// same tick every countdown does and no notice needs a timer of its own.
    let prune (demoNow: DateTimeOffset) (queue: Notice list) =
        queue |> List.filter (fun n ->
            match n.ExpiresAt with
            | Some at -> demoNow < at
            | None -> true)

    let dismiss (id: int) (queue: Notice list) = queue |> List.filter (fun n -> n.Id <> id)

    /// Clear everything about a job. A job that closes should not leave "Your
    /// provider is running late" on screen.
    let clearJob (jobId: int) (queue: Notice list) =
        queue |> List.filter (fun n -> n.JobId <> Some jobId)

    /// Classify a server notification string.
    ///
    /// The hub sends text, not structure — changing that is a bigger contract
    /// change than this phase needs. Matching here at least stops a no-show and
    /// an acceptance looking identical. Unrecognised text is Info, which is the
    /// safe default: too quiet rather than falsely alarming.
    let classify (text: string) =
        let t = text.ToLowerInvariant()
        if t.Contains "no-show" || t.Contains "cancel" then NoticeKind.Warning
        elif t.Contains "running late" then NoticeKind.Warning
        elif t.Contains "accepted" || t.Contains "complete" || t.Contains "arriv" then NoticeKind.Success
        else NoticeKind.Info
