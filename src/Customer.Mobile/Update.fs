module FixItHere.Customer.Update

open System.Threading.Tasks
open Fabulous
open FixItHere.Shared.Dtos
open FixItHere.Customer

/// Run an ApiDeps call; map Ok to a message, Error/exception to ApiError.
let apiCmd (work: unit -> Task<Result<'a, string>>) (ok: 'a -> Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task {
        try
            match! work () with
            | Ok v -> return ok v
            | Error e -> return ApiError e
        with ex -> return ApiError ex.Message
    })

let delayCmd (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task {
        do! Task.Delay ms
        return msg
    })

let init () = Model.initial, delayCmd 1500 SplashDone

let update (deps: ApiDeps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SplashDone -> { model with Screen = Login; History = [] }, Cmd.none
    | SelectCustomer name -> model, apiCmd (fun () -> deps.Login name) LoggedIn
    | LoggedIn resp ->
        let session = { Token = resp.Token; UserId = resp.UserId; DisplayName = resp.DisplayName }
        Nav.resetTo Home { model with Session = Some session },
        Cmd.batch
            [ apiCmd (fun () -> deps.GetJobs resp.UserId) JobsLoaded
              apiCmd deps.GetServices ServicesLoaded ]
    | Navigate target ->
        let m = Nav.push model target
        let cmd =
            match target, model.Session with
            | Catalog, _ -> apiCmd deps.GetServices ServicesLoaded
            | ProviderList serviceId, _ ->
                let lat, lng = model.MyLocation
                apiCmd (fun () -> deps.GetProviders serviceId lat lng) ProvidersLoaded
            | ProviderProfile providerId, _ ->
                apiCmd (fun () -> deps.GetRatings providerId) ProfileRatingsLoaded
            | Home, Some s -> apiCmd (fun () -> deps.GetJobs s.UserId) JobsLoaded
            | Chat jobId, _ -> apiCmd (fun () -> deps.GetMessages jobId) MessagesLoaded
            | Payment jobId, _ -> delayCmd 2000 (PaymentDelayDone jobId)
            | _ -> Cmd.none
        m, cmd
    | GoBack -> Nav.back model, Cmd.none
    | ServicesLoaded xs -> { model with Services = xs }, Cmd.none
    | ProvidersLoaded xs -> { model with Providers = xs }, Cmd.none
    | ProfileRatingsLoaded xs -> { model with ProfileRatings = xs }, Cmd.none
    | JobsLoaded xs -> { model with Jobs = xs }, Cmd.none
    | BookJob (providerId, serviceId, schedule) ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some s ->
            let lat, lng = model.MyLocation
            let req =
                { CustomerId = s.UserId; ProviderId = providerId; ServiceId = serviceId
                  ScheduleChoice = schedule; Lat = lat; Lng = lng; Address = "My location" }
            model, apiCmd (fun () -> deps.CreateJob req) JobCreated
    | JobCreated job ->
        let m = { model with Jobs = job :: model.Jobs }
        Nav.push m (Tracking job.Id), Cmd.none
    | ApiError e -> { model with Error = Some e }, Cmd.none
    | DismissError -> { model with Error = None }, Cmd.none
    | DismissToast -> { model with Toast = None }, Cmd.none
    // Part-2 arms (Task 5) — inert until then:
    | CancelActiveJob _ | MessagesLoaded _ | ChatDraftChanged _ | SendChatMessage _
    | PickAndSendPhoto _ | ChatMessageSent _ | StarsChanged _ | RatingCommentChanged _
    | PaymentDelayDone _ | PaymentSimulated _ | SubmitRating _ | RatingSubmitted
    | StartFakeCall | EndFakeCall | SetLocation _ | SetUseRealGps _
    | HubJobUpdated _ | HubMessageReceived _ | HubLocationUpdated _ | HubNotification _ ->
        model, Cmd.none
