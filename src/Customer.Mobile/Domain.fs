namespace FixItHere.Customer

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
    | Splash
    | Login
    | Home
    | Catalog
    | ProviderList of serviceId: int
    | ProviderProfile of providerId: int
    | Booking of providerId: int * serviceId: int
    | Tracking of jobId: int
    | Chat of jobId: int
    | Payment of jobId: int
    | Rating of jobId: int

type Model =
    { Screen: Screen
      History: Screen list
      Session: Session option
      MyLocation: float * float
      UseRealGps: bool
      Services: ServiceDto list
      Providers: ProviderDto list
      ProfileRatings: RatingDto list
      Jobs: JobDto list
      Messages: MessageDto list
      ProviderPositions: Map<int, float * float>
      PaymentResult: PaymentResult option
      FakeCallActive: bool
      /// Draft text per job id. A single global draft meant an auto-reply (or a
      /// send on another job) wiped whatever was half-typed in the open chat.
      ChatDrafts: Map<int, string>
      /// Generation counter for typing-expiry timers. Each HubTyping schedules an
      /// independent 3s timer; without a token an older timer fires while the peer
      /// is still typing and clears the indicator early.
      TypingToken: int
      ProviderTyping: bool
      /// Highest id of MY messages the provider has confirmed seeing. See the
      /// Provider app for why this is a watermark rather than a bool.
      SeenUpToMessageId: int option
      TypingCooldown: bool
      RatingStars: int
      RatingComment: string
      Toast: string option
      Error: string option }

module Model =
    /// Default location = downtown Toronto; replaced by seed/GPS via SetLocation.
    let initial =
        { Screen = Splash; History = []; Session = None
          MyLocation = (43.65, -79.38); UseRealGps = false
          Services = []; Providers = []; ProfileRatings = []
          Jobs = []; Messages = []; ProviderPositions = Map.empty
          PaymentResult = None; FakeCallActive = false
          ChatDrafts = Map.empty; TypingToken = 0; ProviderTyping = false; SeenUpToMessageId = None; TypingCooldown = false
          RatingStars = 5; RatingComment = ""
          Toast = None; Error = None }

type Msg =
    | SplashDone
    | SelectCustomer of name: string
    | LoggedIn of LoginResponse
    | Navigate of Screen
    | GoBack
    | ServicesLoaded of ServiceDto list
    | ProvidersLoaded of ProviderDto list
    | ProfileRatingsLoaded of RatingDto list
    | JobsLoaded of JobDto list
    | BookJob of providerId: int * serviceId: int * schedule: string
    | JobCreated of JobDto
    | CancelActiveJob of jobId: int
    | MessagesLoaded of MessageDto list
    | ChatDraftChanged of jobId: int * text: string
    | SendChatMessage of jobId: int * text: string * photoBase64: string
    | PickAndSendPhoto of jobId: int
    | ChatMessageSent of MessageDto
    | StarsChanged of int
    | RatingCommentChanged of string
    | PaymentDelayDone of jobId: int
    | PaymentSimulated of PaymentResult
    | SubmitRating of jobId: int * stars: int * comment: string
    | RatingSubmitted
    | StartFakeCall
    | EndFakeCall
    /// No longer reachable from the UI — the DevSettings screen that raised
    /// these was removed as demo scaffolding. Deliberately retained rather than
    /// deleted: the capability may return via an operator channel, and the
    /// handlers carry regression coverage. There is currently no /dev control
    /// that dispatches them.
    | SetLocation of lat: float * lng: float
    | SetUseRealGps of bool
    | HubJobUpdated of JobDto
    | HubMessageReceived of MessageDto
    | HubLocationUpdated of LocationDto
    | HubProviderUpdated of ProviderDto
    | HubNotification of string
    | HubTyping of jobId: int * senderId: int * senderRole: string
    | HubSeen of jobId: int * senderId: int * senderRole: string
    | TypingExpired of token: int
    | TypingCooldownDone
    /// No longer reachable from the UI. The /dev console has its own Start Demo,
    /// but it POSTs /dev/demo/start directly rather than dispatching this.
    | StartDemo
    | DismissToast
    | DismissError
    | ApiError of string

type ApiDeps =
    { Login: string -> Task<Result<LoginResponse, string>>
      GetServices: unit -> Task<Result<ServiceDto list, string>>
      GetProviders: int -> float -> float -> Task<Result<ProviderDto list, string>>
      GetRatings: int -> Task<Result<RatingDto list, string>>
      GetJobs: int -> Task<Result<JobDto list, string>>
      CreateJob: CreateJobRequest -> Task<Result<JobDto, string>>
      CancelJob: int -> Task<Result<JobDto, string>>
      GetMessages: int -> Task<Result<MessageDto list, string>>
      SendMessage: SendMessageRequest -> Task<Result<MessageDto, string>>
      SimulatePayment: int -> Task<Result<PaymentResult, string>>
      SubmitRating: CreateRatingRequest -> Task<Result<RatingDto, string>>
      StartDemo: int -> int -> Task<Result<JobDto, string>>   // customerId, providerId
      // MAUI-implemented effects, injected like the HTTP calls so update stays pure:
      PickPhoto: unit -> Task<Result<string, string>>          // base64 jpeg/png ≤ ~100KB
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
