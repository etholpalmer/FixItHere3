namespace FixItHere.Shared

type JobState =
    | Scheduled
    | EnRoute
    | Arrived
    | InProgress
    | Completed
    | Closed
    | Cancelled

type JobEvent =
    | Accepted      // provider takes the job (stays Scheduled in demo; accept marks assignment)
    | DepartEnRoute
    | Arrive
    | StartWork
    | CompleteWork
    | RateAndClose
    | Cancel

type JobKind = Service

module ServiceNames =
    let all = ["Plumbing"; "Electrical"; "Painting"; "Mechanic"; "Moving"; "Cleaning"; "HVAC"]
