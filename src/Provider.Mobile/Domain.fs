namespace FixItHere.Provider

open System.Threading.Tasks
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
    let isSelf (s: Session) (id: int) (role: string) = id = s.UserId && role = s.Role

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
      Toast: string option
      Error: string option }

module Model =
    let initial =
        { Screen = Splash; History = []; Session = None; Online = false
          MyLocation = (43.70, -79.45); UseRealGps = false; SliderStart = None; GpsLoopActive = false
          Jobs = []; Messages = []
          CustomerTyping = false; SeenUpToMessageId = None; TypingCooldown = false
          AutoReply = false; AutoRepliesSent = 0
          ChatDrafts = Map.empty; TypingToken = 0; RatingStars = 5; RatingComment = ""
          PaymentResult = None; FakeCallActive = false; Toast = None; Error = None }

type Msg =
    | SplashDone
    | SelectProvider of name: string
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
    | StartDemo
    | DemoStarted of JobDto
    | HubJobUpdated of JobDto
    | HubMessageReceived of MessageDto
    | HubLocationUpdated of LocationDto
    | HubProviderUpdated of ProviderDto
    | HubNotification of string
    | HubTyping of jobId: int * senderId: int * senderRole: string
    | HubSeen of jobId: int * senderId: int * senderRole: string
    | CustomerTypingExpired of token: int
    | DismissToast
    | DismissError
    | ApiError of string

type ProviderApiDeps =
    { Login: string -> Task<Result<LoginResponse, string>>
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
      StartDemo: int -> int -> Task<Result<JobDto, string>>   // customerId, providerId
      PickPhoto: unit -> Task<Result<string, string>>
      GetGpsLocation: unit -> Task<Result<float * float, string>>
      SendTyping: int -> int -> string -> unit
      SendSeen: int -> int -> string -> unit }

module Nav =
    let push (m: Model) (s: Screen) = { m with Screen = s; History = m.Screen :: m.History }
    let back (m: Model) =
        match m.History with
        | prev :: rest -> { m with Screen = prev; History = rest }
        | [] -> { m with Screen = Home; History = [] }
    let resetTo (s: Screen) (m: Model) = { m with Screen = s; History = [] }

[<AutoOpen>]
module Domain =
    let private inFlight = [ "EnRoute"; "Arrived"; "InProgress" ]
    /// The single job currently being worked (spec: one Active Job at a time).
    let activeJob (m: Model) : JobDto option =
        m.Jobs |> List.tryFind (fun j -> List.contains j.State inFlight)

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
