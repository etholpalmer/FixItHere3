module FixItHere.Provider.Update

open System.Threading.Tasks
open Fabulous
open FixItHere.Shared.Dtos
open FixItHere.Provider

let apiCmd (work: unit -> Task<Result<'a, string>>) (ok: 'a -> Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task {
        try
            match! work () with
            | Ok v -> return ok v
            | Error e -> return ApiError e
        with ex -> return ApiError ex.Message
    })

let delayCmd (ms: int) (msg: Msg) : Cmd<Msg> =
    Cmd.ofTaskMsg (task { do! Task.Delay ms
                          return msg })

let init () = Model.initial, delayCmd 1500 SplashDone

let update (deps: ProviderApiDeps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SplashDone -> { model with Screen = Login; History = [] }, Cmd.none
    | SelectProvider name -> model, apiCmd (fun () -> deps.Login name) LoggedIn
    | LoggedIn resp ->
        let session = { Token = resp.Token; UserId = resp.UserId; DisplayName = resp.DisplayName }
        Nav.resetTo Home { model with Session = Some session },
        apiCmd (fun () -> deps.GetMyJobs resp.UserId) JobsLoaded
    | Navigate target ->
        let m = Nav.push model target
        let cmd =
            match target, model.Session with
            | Home, Some s -> apiCmd (fun () -> deps.GetMyJobs s.UserId) JobsLoaded
            | Chat jobId, _ -> apiCmd (fun () -> deps.GetMessages jobId) MessagesLoaded
            | Payment jobId, _ -> delayCmd 2000 (PaymentDelayDone jobId)
            | _ -> Cmd.none
        m, cmd
    | GoBack -> Nav.back model, Cmd.none
    | SetOnline b ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some s -> model, apiCmd (fun () -> deps.SetOnline s.UserId b) OnlineChanged
    | OnlineChanged dto ->
        { model with Online = dto.Online
                     Toast = Some (if dto.Online then "You are Online" else "You are Offline") },
        Cmd.none
    | JobsLoaded xs -> { model with Jobs = xs }, Cmd.none
    | AcceptJob id -> model, apiCmd (fun () -> deps.Accept id) JobActioned
    | Depart id -> model, apiCmd (fun () -> deps.Enroute id) JobActioned
    | MarkArrived id -> model, apiCmd (fun () -> deps.Arrive id) JobActioned
    | BeginWork id -> model, apiCmd (fun () -> deps.Start id) JobActioned
    | FinishWork id -> model, apiCmd (fun () -> deps.Complete id) JobActioned
    | JobActioned job ->
        let jobs =
            if model.Jobs |> List.exists (fun j -> j.Id = job.Id)
            then model.Jobs |> List.map (fun j -> if j.Id = job.Id then job else j)
            else job :: model.Jobs
        let m = { model with Jobs = jobs }
        match model.Screen, job.State with
        | JobDetail id, _ when id = job.Id -> Nav.push m (ActiveJob job.Id), Cmd.none
        | ActiveJob id, "EnRoute" when id = job.Id && m.UseRealGps ->
            m, delayCmd 3000 (GpsTick job.Id)          // start GPS streaming loop
        | ActiveJob id, "Completed" when id = job.Id ->
            Nav.push m (Payment job.Id), delayCmd 2000 (PaymentDelayDone job.Id)
        | _ -> m, Cmd.none
    | ApiError e -> { model with Error = Some e }, Cmd.none
    | DismissError -> { model with Error = None }, Cmd.none
    | DismissToast -> { model with Toast = None }, Cmd.none
    // Part-2 arms (Task 7) — inert until then:
    | GpsTick _ | LocationPushed _ | SliderMoved _ | MessagesLoaded _
    | ChatDraftChanged _ | TypingCooldownDone | SendChatMessage _ | PickAndSendPhoto _
    | ChatMessageSent _ | AutoReplyToggled _ | AutoReplyDue _
    | PaymentDelayDone _ | PaymentSimulated _ | StarsChanged _ | RatingCommentChanged _
    | SubmitRating _ | RatingSubmitted | StartFakeCall | EndFakeCall
    | SetLocation _ | SetUseRealGps _ | StartDemo | DemoStarted _
    | HubJobUpdated _ | HubMessageReceived _ | HubLocationUpdated _ | HubNotification _
    | HubTyping _ | HubSeen _ | CustomerTypingExpired ->
        model, Cmd.none
