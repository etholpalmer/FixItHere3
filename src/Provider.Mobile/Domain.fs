namespace FixItHere.Provider

open System
open System.Threading.Tasks
open FixItHere.Shared
open FixItHere.Shared.Dtos

/// Customer and Provider ids are independent sequences that both start at 1,
/// so UserId alone is ambiguous — customer 1 and provider 1 are different
/// actors. Role namespaces it; compare both, never the id on its own.
type Session = { Token: string; UserId: int; Role: string; DisplayName: string }

[<AutoOpen>]
module Drafts =
    /// Draft for one job; empty when nothing has been typed there yet.
    let draftFor (drafts: Map<int, string>) (jobId: int) =
        drafts |> Map.tryFind jobId |> Option.defaultValue ""

[<AutoOpen>]
module Actor =
    /// True when (id, role) identifies the same actor as the session.
    /// Delegates to `Actor.isWire` rather than comparing by hand. Both apps
    /// carried their own copy of this expression, and the bare-id version of it
    /// has been fixed in four separate places in this codebase.
    let isSelf (s: Session) (id: int) (role: string) =
        match Actor.ofWire s.UserId s.Role with
        | Some me -> Actor.isWire me id role
        | None -> false

type Screen =
    | Splash | Login | Home
    | JobDetail of jobId: int
    | ActiveJob of jobId: int
    | Chat of jobId: int
    | Payment of jobId: int
    | RateCustomer of jobId: int

type Model =
    { Screen: Screen
      History: Screen list
      Session: Session option
      Online: bool
      MyLocation: float * float
      UseRealGps: bool
      SliderStart: (float * float) option
      /// Guards the 3s GPS polling loop. JobActioned can fire twice (e.g. a
      /// double-tapped "Depart"), which would start two concurrent loops.
      GpsLoopActive: bool
      Jobs: JobDto list
      Messages: MessageDto list
      CustomerTyping: bool
      /// Highest id of MY messages the customer has confirmed seeing. A bare
      /// bool latched forever: once set it marked "✓✓ seen" on later messages
      /// and on other jobs' chats. Ids are globally monotonic, so comparing
      /// `m.Id <= watermark` scopes the marker correctly without extra state.
      SeenUpToMessageId: int option
      TypingCooldown: bool
      AutoReply: bool
      AutoRepliesSent: int
      /// Draft text per job id. A single global draft meant an auto-reply (or a
      /// send on another job) wiped whatever was half-typed in the open chat.
      ChatDrafts: Map<int, string>
      /// Generation counter for typing-expiry timers. Each HubTyping schedules an
      /// independent 3s timer; without a token an older timer fires while the peer
      /// is still typing and clears the indicator early.
      TypingToken: int
      RatingStars: int
      RatingComment: string
      PaymentResult: PaymentResult option
      FakeCallActive: bool
      /// Sign-in form. Prefilled with the primary demo account so an operator
      /// is one tap from signing in, but editable — the field is the only way to
      /// switch accounts now that the name picker is gone.
      LoginEmail: string
      LoginPassword: string
      SigningIn: bool
      /// A queue, not a slot. A single `Toast: string option` meant the
      /// second notification silently replaced the first — so the two-sided
      /// moments this phase is built around could overwrite each other
      /// mid-demo with nothing left to show it happened.
      Notices: Notice list
      NextNoticeId: int
      /// The demo clock, mirrored as the server's affine *map* rather than as
      /// a time. `DemoNow` is recomputed from it on every tick, which is why
      /// no countdown and no notice expiry in this app owns a timer.
      Clock: DemoClock option
      DemoNow: DateTimeOffset
      /// Guards the tick loop against being started twice — the same
      /// re-entrancy shape as GpsLoopActive.
      TickActive: bool
      /// The job whose cancellation is awaiting confirmation.
      ///
      /// Inline rather than a modal alert: the product register is explicit
      /// that a modal is usually laziness, and an inline bar keeps the job it
      /// refers to on screen while the question is asked.
      ConfirmingCancel: int option
      /// The job whose next progress action (Arrive / Start Work / Complete) is
      /// armed and awaiting a confirming second tap. Each of those advances the
      /// job irreversibly, and the button sits under a thumb on the map screen,
      /// so a stray tap must not skip a step. Depart is deliberately not here —
      /// it is the deliberate "I'm heading out", not a mid-job progress step.
      ConfirmingAction: int option
      Error: string option
      /// Generation token for the error bar's self-dismissal. A bare delayed
      /// `DismissError` would let an *old* error's timer wipe a newer one that
      /// replaced it — the same stale-timer shape as `TypingToken`.
      ErrorToken: int }

/// What the marketplace may offer this provider right now.
///
/// Derived, never stored. A provider standing in someone's kitchen is not
/// available whatever their shift toggle last said, and holding that as a
/// second flag would be one more thing to leave stale — the same shape as the
/// stale-timer bugs this codebase has already paid for twice. One rule, one
/// place, recomputed from the jobs list every frame.
///
/// `OnAJob` outranks `Offline` deliberately: someone mid-job is on a job, not
/// off shift, and telling them "Offline" while they stand at a customer's door
/// would be the screen contradicting the work.
[<RequireQualifiedAccess>]
type Availability =
    /// Off shift, by their own choice.
    | Offline
    /// Committed to a job that is under way. Not offered anything else.
    | OnAJob
    /// On shift and free to be offered work.
    | Available

module Model =
    let initial =
        { Screen = Splash; History = []; Session = None; Online = false
          MyLocation = (43.70, -79.45); UseRealGps = false; SliderStart = None; GpsLoopActive = false
          Jobs = []; Messages = []
          CustomerTyping = false; SeenUpToMessageId = None; TypingCooldown = false
          AutoReply = false; AutoRepliesSent = 0
          ChatDrafts = Map.empty; TypingToken = 0; RatingStars = 5; RatingComment = ""
          PaymentResult = None; FakeCallActive = false
          LoginEmail = "contact@mikesplumbing.ca"; LoginPassword = "Provider1!"; SigningIn = false
          Notices = []; NextNoticeId = 1
          Clock = None; DemoNow = DemoClock.epoch; TickActive = false
          ConfirmingCancel = None
          ConfirmingAction = None
          Error = None; ErrorToken = 0 }

type Msg =
    | SplashDone
    | LoginEmailChanged of string
    | LoginPasswordChanged of string
    | SignIn
    | LoggedIn of LoginResponse
    | Navigate of Screen
    | GoBack
    | SetOnline of bool
    | ProviderHydrated of ProviderDto
    | OnlineChanged of ProviderDto
    | JobsLoaded of JobDto list
    | AcceptJob of jobId: int
    | Depart of jobId: int
    | MarkArrived of jobId: int
    | BeginWork of jobId: int
    | FinishWork of jobId: int
    /// Ask the customer for more time. The delay is chosen from a short list
    /// rather than typed: the provider is in a vehicle, and a free-text time
    /// picker is not a thing anyone completes at the roadside.
    | ProposeDelay of jobId: int * minutes: int
    /// The provider could not cancel at all. A marketplace where only one side
    /// can walk away is not a marketplace, and the plan's asymmetry table had
    /// this as its first row.
    /// Asks; does not act. Cancelling is irreversible and was one tap.
    | RequestCancel of jobId: int
    | DismissCancel
    /// Two-tap guard on the progress actions. `RequestAction` arms the confirm
    /// (first tap), `ConfirmAction` fires the state's real transition (second
    /// tap), `DismissAction` backs out. Depart is exempt — it stays one tap.
    | RequestAction of jobId: int
    | ConfirmAction of jobId: int
    | DismissAction
    | CancelJob of jobId: int
    | JobActioned of JobDto
    | GpsTick of jobId: int
    | GpsFetched of jobId: int * lat: float * lng: float
    | LocationPushed of LocationDto
    /// No longer reachable from the UI. The /dev console's route walk performs
    /// the same interpolation but PUTs /location directly, bypassing this Msg —
    /// so the two implementations must stay in step (see Slider.position below).
    | SliderMoved of pct: float
    | MessagesLoaded of MessageDto list
    | ChatDraftChanged of jobId: int * text: string
    | TypingCooldownDone
    | SendChatMessage of jobId: int * text: string * photoBase64: string
    | PickAndSendPhoto of jobId: int
    | ChatMessageSent of MessageDto
    /// No longer reachable from the UI — the Auto-Reply switch was removed from
    /// provider Chat as demo scaffolding, and no /dev control replaces it yet, so
    /// AutoReply stays false for the whole session. Retained because the handlers
    /// carry the regression tests for the customer/provider id-collision fix.
    | AutoReplyToggled of bool
    | AutoReplyDue of jobId: int
    | PaymentDelayDone of jobId: int
    | PaymentSimulated of PaymentResult
    | StarsChanged of int
    | RatingCommentChanged of string
    | SubmitRating of jobId: int * stars: int * comment: string
    | RatingSubmitted
    | StartFakeCall
    | EndFakeCall
    /// No longer reachable from the UI (see the note on SliderMoved). The /dev
    /// console drives provider position via PUT /location, not via these.
    | SetLocation of lat: float * lng: float
    | SetUseRealGps of bool
    | HubJobUpdated of JobDto
    | HubMessageReceived of MessageDto
    | HubLocationUpdated of LocationDto
    | HubProviderUpdated of ProviderDto
    | HubNotification of string
    | HubTyping of jobId: int * senderId: int * senderRole: string
    | HubSeen of jobId: int * senderId: int * senderRole: string
    | CustomerTypingExpired of token: int
    | DismissNotice of int
    /// One tick, every 250 ms of real time. Recomputes DemoNow from the clock
    /// map and prunes expired notices. 250 rather than 1000 because at 60x a
    /// one-second tick advances demo time a full minute and every countdown
    /// visibly skips.
    | DemoTick
    | ClockSynced of DemoClockDto
    | DismissError
    /// The operator reseeded the backend. Everything this app holds is stale.
    | DataReset
    /// Self-dismissal, ignored unless it names the error still on screen.
    | ErrorExpired of token: int
    | ApiError of string

type ProviderApiDeps =
    { Login: string -> string -> Task<Result<LoginResponse, string>>
      GetProvider: int -> Task<Result<ProviderDto, string>>
      SetOnline: int -> bool -> Task<Result<ProviderDto, string>>
      GetMyJobs: int -> Task<Result<JobDto list, string>>
      Accept: int -> Task<Result<JobDto, string>>
      Enroute: int -> Task<Result<JobDto, string>>
      Arrive: int -> Task<Result<JobDto, string>>
      Start: int -> Task<Result<JobDto, string>>
      Complete: int -> Task<Result<JobDto, string>>
      UpdateLocation: int -> float -> float -> Task<Result<LocationDto, string>>
      GetMessages: int -> Task<Result<MessageDto list, string>>
      SendMessage: SendMessageRequest -> Task<Result<MessageDto, string>>
      SimulatePayment: int -> Task<Result<PaymentResult, string>>
      SubmitRating: CreateRatingRequest -> Task<Result<RatingDto, string>>
      PickPhoto: unit -> Task<Result<string, string>>
      GetGpsLocation: unit -> Task<Result<float * float, string>>
      /// Startup and reconnect resync. A client that missed a ClockUpdated
      /// while disconnected cannot rebuild the map from ticks it never got —
      /// it has to ask.
      GetClock: unit -> Task<Result<DemoClockDto, string>>
      ProposeReschedule: ProposeRescheduleRequest -> Task<Result<JobDto, string>>
      CancelJob: ReportNoShowRequest -> Task<Result<JobDto, string>>
      /// Remember who is signed in across app launches.
      ///
      /// Deliberately plain Preferences, not SecureStorage. The token is
      /// literally "fake-customer-1" (see Auth.fs) and putting it behind the
      /// keychain would imply a credential store this prototype does not have
      /// and must not pretend to have.
      SaveSession: Session option -> unit
      RestoreSession: unit -> Session option
      SendTyping: int -> int -> string -> unit
      SendSeen: int -> int -> string -> unit }

/// Navigation, and the one thing that must not survive it.
///
/// Every move clears `Error`. The bar reports the failure of an action taken
/// on the screen you were looking at, so it is meaningless on the next one —
/// and it used to follow the user everywhere until they happened to tap it.
/// Clearing lives here rather than in each `update` arm so a new call site
/// cannot forget: there is no way to change screen without going through these.
module Nav =
    let push (m: Model) (s: Screen) = { m with Screen = s; History = m.Screen :: m.History; Error = None }
    let back (m: Model) =
        match m.History with
        | prev :: rest -> { m with Screen = prev; History = rest; Error = None }
        | [] -> { m with Screen = Home; History = []; Error = None }
    let resetTo (s: Screen) (m: Model) = { m with Screen = s; History = []; Error = None }

[<AutoOpen>]
module Domain =
    /// The job's reschedule sub-status, rebuilt from the flat wire fields.
    /// Empty strings are the absent case — see Dtos.JobDto for why the wire is
    /// flat rather than nested.
    let rescheduleOf (j: JobDto) : Reschedule =
        let parse (s: string) =
            match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture,
                                          Globalization.DateTimeStyles.RoundtripKind) with
            | true, v -> Some v
            | _ -> None
        let promised =
            parse j.PromisedStart
            |> Option.orElseWith (fun () -> parse j.ScheduledFor)
            |> Option.defaultValue DemoClock.epoch
        let pending =
            match parse j.ProposedStart, ActorRole.ofWire j.ProposedBy, parse j.ProposalExpiresAt with
            | Some at, Some by, Some expires ->
                Some { ProposedStart = at; By = by; Reason = j.ProposalReason; ExpiresAt = expires }
            | _ -> None
        { PromisedStart = promised; Pending = pending }

    /// The countdown for a job, from the model's own DemoNow. Provider-side:
    /// while a job is only Scheduled the useful number is when to *leave*, not
    /// when they are expected.
    let countdownFor (m: Model) (j: JobDto) : Countdown option =
        JobStateCodec.tryParse j.State
        |> Option.bind (fun state ->
            // The provider app knows its *own* position directly — it is the
            // one pushing it — so there is no positions map to consult.
            let km = Some (Geo.distanceKm m.MyLocation (j.Lat, j.Lng))
            Countdown.forProvider state (rescheduleOf j) km m.DemoNow)


    /// The single job this provider is committed to right now (spec: one Active
    /// Job at a time).
    ///
    /// Committed means either *in flight* (driving to or working the job) or
    /// *accepted but not yet departed*. The second case is why `IsAccepted`
    /// exists: `Accepted` leaves the state `Scheduled`, so without the flag an
    /// accepted job would sit in "Available jobs" looking untaken and a second
    /// job could be accepted before this one departs. The in-flight set lives in
    /// Shared and is exhaustive over JobState, so a new state cannot silently
    /// fall outside it.
    let activeJob (m: Model) : JobDto option =
        m.Jobs
        |> List.tryFind (fun j ->
            match JobStateCodec.tryParse j.State with
            | Some st when JobStatus.isInFlight st -> true
            | Some Scheduled -> j.IsAccepted
            | Some _ | None -> false)

    /// The one rule for whether this provider can be offered work.
    ///
    /// A job in flight takes them off the market for its whole duration — they
    /// are driving to, or standing at, someone's address — and only marking it
    /// complete puts them back. Nothing stores that: the moment the job leaves
    /// the in-flight set this returns `Available` again on its own.
    let availability (m: Model) =
        match activeJob m with
        | Some _ -> Availability.OnAJob
        | None -> if m.Online then Availability.Available else Availability.Offline

    /// True when an incoming hub chat message should trigger a canned auto-reply:
    /// auto-reply is enabled, the message isn't my own, and the sender is the
    /// customer on one of my jobs.
    ///
    /// The sender must be matched on (id, role): a provider whose id happens to
    /// equal the job's CustomerId is NOT the customer. Comparing ids alone made
    /// this return false for every colliding pair (e.g. customer 1 + provider 1),
    /// silently disabling auto-reply for the default demo pairing.
    let shouldAutoReply (me: Session option) (m: Model) (msg: MessageDto) : bool =
        msg.SenderRole = "Customer"
        && (match me with Some s -> not (isSelf s msg.SenderId msg.SenderRole) | None -> false)
        && m.AutoReply
        && m.Jobs |> List.exists (fun j -> j.Id = msg.JobId && j.CustomerId = msg.SenderId)

module Slider =
    /// Linear interpolation from start toward target; pct clamped to [0, 1].
    let position (startPos: float * float) (target: float * float) (pct: float) =
        let p = max 0.0 (min 1.0 pct)
        let (sLat, sLng), (tLat, tLng) = startPos, target
        (sLat + (tLat - sLat) * p, sLng + (tLng - sLng) * p)
