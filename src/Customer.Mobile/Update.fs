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
        let m =
            match target with
            | Payment _ -> { m with PaymentResult = None }
            | _ -> m
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
    | CancelActiveJob jobId ->
        model, apiCmd (fun () -> deps.CancelJob jobId) HubJobUpdated
    | MessagesLoaded xs -> { model with Messages = xs }, Cmd.none
    | ChatDraftChanged t -> { model with ChatDraft = t }, Cmd.none
    | StarsChanged n -> { model with RatingStars = n }, Cmd.none
    | RatingCommentChanged t -> { model with RatingComment = t }, Cmd.none
    | SendChatMessage (jobId, text, photo) ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some _ when System.String.IsNullOrWhiteSpace text && System.String.IsNullOrEmpty photo ->
            model, Cmd.none   // nothing to send
        | Some s ->
            let req = { JobId = jobId; SenderId = s.UserId; Text = text; PhotoBase64 = photo }
            { model with ChatDraft = "" }, apiCmd (fun () -> deps.SendMessage req) ChatMessageSent
    | PickAndSendPhoto jobId ->
        // Spec: at most 5 photos per job from this customer.
        let myId = model.Session |> Option.map (fun s -> s.UserId)
        let sentPhotos =
            model.Messages
            |> List.filter (fun m ->
                m.JobId = jobId && Some m.SenderId = myId
                && not (System.String.IsNullOrEmpty m.PhotoBase64))
            |> List.length
        if sentPhotos >= 5 then
            { model with Error = Some "Photo limit reached (5 per job)" }, Cmd.none
        else
            model, apiCmd deps.PickPhoto (fun b64 -> SendChatMessage (jobId, "", b64))
    | ChatMessageSent m2 ->
        let msgs =
            if model.Messages |> List.exists (fun x -> x.Id = m2.Id)
            then model.Messages else model.Messages @ [m2]
        { model with Messages = msgs }, Cmd.none
    | PaymentDelayDone jobId ->
        model, apiCmd (fun () -> deps.SimulatePayment jobId) PaymentSimulated
    | PaymentSimulated r -> { model with PaymentResult = Some r }, Cmd.none
    | SubmitRating (jobId, stars, comment) ->
        match model.Session, model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
        | Some s, Some job ->
            let req =
                { JobId = jobId; RaterId = s.UserId; RateeId = job.ProviderId
                  Stars = stars; Comment = comment }
            model, apiCmd (fun () -> deps.SubmitRating req) (fun _ -> RatingSubmitted)
        | _ -> model, Cmd.ofMsg (ApiError "Job not found")
    | RatingSubmitted ->
        let refresh =
            match model.Session with
            | Some s -> apiCmd (fun () -> deps.GetJobs s.UserId) JobsLoaded
            | None -> Cmd.none
        Nav.resetTo Home
            { model with Toast = Some "Thanks for your rating!"; PaymentResult = None
                         RatingStars = 5; RatingComment = "" },
        refresh
    | StartFakeCall -> { model with FakeCallActive = true }, delayCmd 10000 EndFakeCall
    | EndFakeCall -> { model with FakeCallActive = false }, Cmd.none
    | SetLocation (lat, lng) -> { model with MyLocation = (lat, lng) }, Cmd.none
    | SetUseRealGps true ->
        { model with UseRealGps = true },
        apiCmd deps.GetGpsLocation (fun (la, ln) -> SetLocation (la, ln))
    | SetUseRealGps false ->
        { model with UseRealGps = false; MyLocation = Model.initial.MyLocation }, Cmd.none
    | HubJobUpdated job ->
        let jobs =
            if model.Jobs |> List.exists (fun j -> j.Id = job.Id)
            then model.Jobs |> List.map (fun j -> if j.Id = job.Id then job else j)
            else job :: model.Jobs
        let m = { model with Jobs = jobs }
        match model.Screen with
        | Tracking id when id = job.Id && job.State = "Completed" ->
            Nav.push m (Payment job.Id), delayCmd 2000 (PaymentDelayDone job.Id)
        | Tracking id when id = job.Id && job.State = "Cancelled" ->
            Nav.resetTo Home m, Cmd.none
        | _ -> m, Cmd.none
    | HubMessageReceived m2 ->
        let activeJob =
            match model.Screen with
            | Chat id | Tracking id -> Some id
            | _ -> None
        if activeJob = Some m2.JobId
           && not (model.Messages |> List.exists (fun x -> x.Id = m2.Id))
        then { model with Messages = model.Messages @ [m2] }, Cmd.none
        else model, Cmd.none
    | HubLocationUpdated loc ->
        { model with ProviderPositions = model.ProviderPositions.Add(loc.ProviderId, (loc.Lat, loc.Lng)) },
        Cmd.none
    | HubNotification text -> { model with Toast = Some text }, Cmd.none
