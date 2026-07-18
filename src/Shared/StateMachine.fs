module FixItHere.Shared.StateMachine

open FixItHere.Shared

/// Pure transition function — the spine of the demo.
let transition (state: JobState) (event: JobEvent) : Result<JobState, string> =
    match state, event with
    | Scheduled,  Accepted      -> Ok Scheduled   // acceptance = assignment, state unchanged
    | Scheduled,  DepartEnRoute -> Ok EnRoute
    | EnRoute,    Arrive        -> Ok Arrived
    | Arrived,    StartWork     -> Ok InProgress
    | InProgress, CompleteWork  -> Ok Completed
    | Completed,  RateAndClose  -> Ok Closed
    | (Scheduled | EnRoute | Arrived | InProgress), Cancel -> Ok Cancelled
    | s, e -> Error (sprintf "Invalid transition: cannot apply %A while %A" e s)
