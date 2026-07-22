namespace FixItHere.Shared.Dtos

[<CLIMutable>]
type Envelope<'t> = { Success: bool; Data: 't; Error: string }

module Envelope =
    let ok data = { Success = true; Data = data; Error = null }
    let fail (msg: string) : Envelope<obj> = { Success = false; Data = null; Error = msg }

[<CLIMutable>]
/// Demo auth: real-shaped (email + password, with real failure modes) but not
/// real security — tokens are literally "fake-customer-1". See Auth.fs.
type LoginRequest = { Role: string; Email: string; Password: string }

[<CLIMutable>]
type LoginResponse = { Token: string; UserId: int; Role: string; DisplayName: string }

/// The /dev console needs a customer roster for its persona picker; it used to
/// POST /login per hardcoded name to enumerate ids, which broke the moment
/// login required credentials.
[<CLIMutable>]
type CustomerDto = { Id: int; Name: string; Email: string }

[<CLIMutable>]
type ServiceDto =
    { Id: int; Name: string
      /// Indicative price for a typical job of this trade, so the catalogue can
      /// say "from $277" rather than listing bare trade names.
      FromPrice: decimal
      TypicalMinutes: int }

[<CLIMutable>]
type ProviderDto =
    { Id: int; BusinessName: string; ServiceId: int; ServiceName: string
      Rating: float; RatingCount: int; Lat: float; Lng: float
      Online: bool; Vehicle: string; PhotoUrl: string }

[<CLIMutable>]
type JobDto =
    { Id: int; CustomerId: int; CustomerName: string
      ProviderId: int; ProviderName: string
      ServiceId: int; ServiceName: string
      State: string; Price: decimal; ScheduledFor: string
      /// The arrival time that currently stands, ISO-8601. Equal to
      /// `ScheduledFor` until a reschedule is accepted, after which the two
      /// diverge and both matter: the original booking is what the customer
      /// agreed to, the promise is what the countdown targets.
      PromisedStart: string
      // The pending proposal, flattened rather than nested.
      //
      // A nested `ProposalDto option` cannot cross System.Text.Json, and a
      // nested nullable record would force `Unchecked.defaultof<_>` at every
      // construction site — a NullReferenceException waiting for whichever
      // screen reads it first. Empty strings are the absent case, and every
      // `Format` function already treats an empty timestamp as "".
      ProposedStart: string
      /// "" | "Customer" | "Provider"
      ProposedBy: string
      ProposalReason: string
      ProposalExpiresAt: string
      /// False for seeded jobs, true for anything booked in-session.
      ///
      /// Without this the demo clock creates a storm: thirty seeded jobs have
      /// fixed start times, so any accelerated run marches demo-now past all
      /// thirty grace windows and fires thirty no-show notifications in a row.
      IsDemoTracked: bool
      Lat: float; Lng: float; Address: string }

[<CLIMutable>]
type CreateJobRequest =
    { CustomerId: int; ProviderId: int; ServiceId: int
      ScheduleChoice: string; Lat: float; Lng: float; Address: string }

/// Customer and Provider ids are independent sequences that both start at 1,
/// so a bare SenderId is ambiguous — customer 1 and provider 1 are different
/// actors. Every identity that crosses an app boundary carries its role.
[<CLIMutable>]
type MessageDto =
    { Id: int; JobId: int; SenderId: int; SenderRole: string; SenderName: string
      Text: string; PhotoBase64: string; SentAt: string; Seen: bool }

[<CLIMutable>]
type SendMessageRequest =
    { JobId: int; SenderId: int; SenderRole: string; Text: string; PhotoBase64: string }

/// Rater/Ratee carry a role for the same reason MessageDto.SenderRole does:
/// customer and provider ids are independent sequences that both start at 1,
/// so an id alone cannot say who was rated. Without the roles, a provider
/// rating a customer moved that customer's id-twin *provider*'s public average.
[<CLIMutable>]
type RatingDto =
    { Id: int; JobId: int
      RaterId: int; RaterRole: string
      /// Resolved for display. A review with no author and no date reads as
      /// filler; "Mary O. · 12 Jan" reads as a person.
      RaterName: string
      RateeId: int; RateeRole: string
      Stars: int; Comment: string
      CreatedAt: string }

[<CLIMutable>]
type CreateRatingRequest =
    { JobId: int
      RaterId: int; RaterRole: string
      RateeId: int; RateeRole: string
      Stars: int; Comment: string }

[<CLIMutable>]
type LocationDto = { ProviderId: int; Lat: float; Lng: float; UpdatedAt: string }

[<CLIMutable>]
type UpdateLocationRequest = { ProviderId: int; Lat: float; Lng: float }

[<CLIMutable>]
type PaymentRequest = { JobId: int }

[<CLIMutable>]
/// A receipt that shows its working. The screen previously displayed a single
/// number with no breakdown — no card, no fee, no payout — which is the missing
/// organ in a *marketplace* pitch: the investor is buying the take rate.
type PaymentResult =
    { JobId: int
      CallOutFee: decimal
      LabourMinutes: int
      LabourAmount: decimal
      Subtotal: decimal
      /// Ontario HST, on the customer side of the ledger.
      Tax: decimal
      /// What the customer is charged.
      Amount: decimal
      /// The marketplace's cut of the subtotal.
      PlatformFee: decimal
      /// Subtotal less the platform fee — what the provider actually receives.
      ProviderPayout: decimal
      Method: string
      Status: string }

/// The demo clock, as the map rather than as a time.
///
/// Clients apply this to their own wall clock, so a client that has been
/// disconnected for a minute resyncs by fetching this once — not by
/// accumulating ticks it missed. `DemoNow` is included for the console's
/// readout and for tests; it is redundant with the map by construction.
[<CLIMutable>]
type DemoClockDto =
    { DemoNow: string
      AnchorDemo: string
      AnchorReal: string
      Rate: float
      Running: bool }

[<CLIMutable>]
type SetClockRequest =
    { /// "pause" | "resume" | "rate" | "jump"
      Action: string
      Rate: float
      /// ISO-8601 demo instant for "jump"; ignored otherwise.
      Target: string }

/// Proposing party is taken from the request, not inferred from the job: either
/// side may propose, and `Reschedule.apply` refuses to let the proposer answer
/// its own proposal.
[<CLIMutable>]
type ProposeRescheduleRequest =
    { JobId: int
      ByRole: string
      ProposedStart: string
      Reason: string }

[<CLIMutable>]
type RescheduleDecisionRequest =
    { JobId: int
      /// The *answering* party, so the server can reject self-answering.
      ByRole: string
      Accept: bool }

[<CLIMutable>]
type ReportNoShowRequest = { JobId: int; ByRole: string }
