namespace FixItHere.Customer

open System.Threading.Tasks
open FixItHere.Shared.Dtos

type Session = { Token: string; UserId: int; DisplayName: string }

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
    | DevSettings

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
      ChatDraft: string
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
          ChatDraft = ""; RatingStars = 5; RatingComment = ""
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
    | ChatDraftChanged of string
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
    | SetLocation of lat: float * lng: float
    | SetUseRealGps of bool
    | HubJobUpdated of JobDto
    | HubMessageReceived of MessageDto
    | HubLocationUpdated of LocationDto
    | HubNotification of string
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
      // MAUI-implemented effects, injected like the HTTP calls so update stays pure:
      PickPhoto: unit -> Task<Result<string, string>>          // base64 jpeg/png ≤ ~100KB
      GetGpsLocation: unit -> Task<Result<float * float, string>> }

module Nav =
    let push (m: Model) (s: Screen) = { m with Screen = s; History = m.Screen :: m.History }
    let back (m: Model) =
        match m.History with
        | prev :: rest -> { m with Screen = prev; History = rest }
        | [] -> { m with Screen = Home; History = [] }
    let resetTo (s: Screen) (m: Model) = { m with Screen = s; History = [] }

module Geo =
    let distanceKm (lat1: float, lng1: float) (lat2: float, lng2: float) =
        let rad d = d * System.Math.PI / 180.0
        let dLat = rad (lat2 - lat1)
        let dLng = rad (lng2 - lng1)
        let a =
            sin (dLat / 2.0) ** 2.0
            + cos (rad lat1) * cos (rad lat2) * sin (dLng / 2.0) ** 2.0
        6371.0 * 2.0 * atan2 (sqrt a) (sqrt (1.0 - a))
