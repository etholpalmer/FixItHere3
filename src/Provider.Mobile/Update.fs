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
        // Session and LoginResponse now share a field set, so annotate explicitly.
        let session : Session = { Token = resp.Token; UserId = resp.UserId
                                  Role = resp.Role; DisplayName = resp.DisplayName }
        Nav.resetTo Home { model with Session = Some session },
        Cmd.batch
            [ apiCmd (fun () -> deps.GetMyJobs resp.UserId) JobsLoaded
              // Online lives on the server (the seed marks providers online), so
              // hydrate it instead of assuming the local default of false — which
              // otherwise hid the available-jobs list until "Go Online" was pressed.
              apiCmd (fun () -> deps.GetProvider resp.UserId) ProviderHydrated ]
    | Navigate target ->
        let m = Nav.push model target
        let cmd =
            match target, model.Session with
            | Home, Some s -> apiCmd (fun () -> deps.GetMyJobs s.UserId) JobsLoaded
            | Chat jobId, _ -> apiCmd (fun () -> deps.GetMessages jobId) MessagesLoaded
            | Payment jobId, _ -> delayCmd 2000 (PaymentDelayDone jobId)
            | _ -> Cmd.none
        // Drop any previous job's receipt before the new one arrives, or the
        // Payment screen renders the old job's number and amount for ~2s while
        // PaymentDelayDone is in flight. (Customer.Mobile already did this.)
        let m =
            match target with
            | Payment _ -> { m with PaymentResult = None }
            | _ -> m
        m, cmd
    | GoBack -> Nav.back model, Cmd.none
    | SetOnline b ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some s -> model, apiCmd (fun () -> deps.SetOnline s.UserId b) OnlineChanged
    | ProviderHydrated dto -> { model with Online = dto.Online }, Cmd.none
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
        | ActiveJob id, "EnRoute" when id = job.Id && m.UseRealGps && not m.GpsLoopActive ->
            // SliderStart is the origin of THIS travel leg; a value left over from a
            // previous job (only cleared on RatingSubmitted) would anchor the
            // simulated route to the wrong city.
            { m with GpsLoopActive = true; SliderStart = None },
            delayCmd 3000 (GpsTick job.Id)   // start GPS loop (once)
        | ActiveJob id, "Completed" when id = job.Id ->
            Nav.push m (Payment job.Id), delayCmd 2000 (PaymentDelayDone job.Id)
        | _ -> m, Cmd.none
    | ApiError e -> { model with Error = Some e }, Cmd.none
    | DismissError -> { model with Error = None }, Cmd.none
    | DismissToast -> { model with Toast = None }, Cmd.none
    | GpsTick jobId ->
        // stream own position while the job is EnRoute and Real GPS is on
        match activeJob model, model.Session with
        | Some j, Some _ when j.Id = jobId && j.State = "EnRoute" && model.UseRealGps ->
            model,
            Cmd.batch
                [ apiCmd deps.GetGpsLocation (fun (la, ln) -> GpsFetched (jobId, la, ln))
                  delayCmd 3000 (GpsTick jobId) ]
        | _ -> { model with GpsLoopActive = false }, Cmd.none
    | GpsFetched (_, la, ln) ->
        // apply the freshly-fetched reading and push that SAME reading to the server
        // (compute once, use twice — avoids pushing a stale model.MyLocation)
        match model.Session with
        | Some s ->
            { model with MyLocation = (la, ln) },
            apiCmd (fun () -> deps.UpdateLocation s.UserId la ln) LocationPushed
        | None -> { model with MyLocation = (la, ln) }, Cmd.none
    | LocationPushed loc -> { model with MyLocation = (loc.Lat, loc.Lng) }, Cmd.none
    | SliderMoved pct ->
        match activeJob model, model.Session with
        | Some job, Some s ->
            let start = model.SliderStart |> Option.defaultValue model.MyLocation
            let (la, ln) = Slider.position start (job.Lat, job.Lng) pct
            { model with SliderStart = Some start },
            apiCmd (fun () -> deps.UpdateLocation s.UserId la ln) LocationPushed
        | _ -> model, Cmd.none
    | MessagesLoaded xs ->
        // Reset the seen watermark when a chat loads — a value carried over from
        // another job can exceed this job's older ids and render a false marker.
        { model with Messages = xs; SeenUpToMessageId = None }, Cmd.none
    | ChatDraftChanged (draftJobId, t) ->
        let m = { model with ChatDrafts = model.ChatDrafts |> Map.add draftJobId t }
        match model.Screen, model.Session with
        | Chat jobId, Some s when not model.TypingCooldown ->
            { m with TypingCooldown = true },
            Cmd.batch
                [ Cmd.ofSub (fun _ -> deps.SendTyping jobId s.UserId s.Role)
                  delayCmd 2000 TypingCooldownDone ]
        | _ -> m, Cmd.none
    | TypingCooldownDone -> { model with TypingCooldown = false }, Cmd.none
    | SendChatMessage (jobId, text, photo) ->
        match model.Session with
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
        | Some _ when System.String.IsNullOrWhiteSpace text && System.String.IsNullOrEmpty photo ->
            model, Cmd.none   // nothing to send
        | Some s ->
            let req = { JobId = jobId; SenderId = s.UserId; SenderRole = s.Role
                        Text = text; PhotoBase64 = photo }
            // Clear only THIS job's draft, so an auto-reply on another job cannot
            // wipe what the user is composing in the chat they have open.
            { model with ChatDrafts = model.ChatDrafts |> Map.remove jobId },
            apiCmd (fun () -> deps.SendMessage req) ChatMessageSent
    | PickAndSendPhoto jobId ->
        // Spec: at most 5 photos per job from this provider.
        let isMineMsg (m: FixItHere.Shared.Dtos.MessageDto) =
            match model.Session with
            | Some s -> isSelf s m.SenderId m.SenderRole
            | None -> false
        let sentPhotos =
            model.Messages
            |> List.filter (fun m ->
                m.JobId = jobId && isMineMsg m
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
    | AutoReplyToggled b -> { model with AutoReply = b }, Cmd.none
    | AutoReplyDue jobId when not model.AutoReply ->
        // Toggled off during the 5s delay — drop the scheduled reply.
        model, Cmd.none
    | AutoReplyDue jobId ->
        let canned = [ "On my way."; "Looks good."; "See you shortly." ]
        let text = canned.[model.AutoRepliesSent % canned.Length]
        { model with AutoRepliesSent = model.AutoRepliesSent + 1 },
        Cmd.ofMsg (SendChatMessage (jobId, text, null))
    | PaymentDelayDone jobId ->
        model, apiCmd (fun () -> deps.SimulatePayment jobId) PaymentSimulated
    | PaymentSimulated r -> { model with PaymentResult = Some r }, Cmd.none
    | StarsChanged n -> { model with RatingStars = n }, Cmd.none
    | RatingCommentChanged t -> { model with RatingComment = t }, Cmd.none
    | SubmitRating (jobId, stars, comment) ->
        match model.Session, model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
        | Some s, Some job ->
            let req = { JobId = jobId; RaterId = s.UserId; RateeId = job.CustomerId
                        Stars = stars; Comment = comment }
            model, apiCmd (fun () -> deps.SubmitRating req) (fun _ -> RatingSubmitted)
        | _ -> model, Cmd.ofMsg (ApiError "Job not found")
    | RatingSubmitted ->
        let refresh =
            match model.Session with
            | Some s -> apiCmd (fun () -> deps.GetMyJobs s.UserId) JobsLoaded
            | None -> Cmd.none
        Nav.resetTo Home
            { model with Toast = Some "Thanks!"; PaymentResult = None
                         RatingStars = 5; RatingComment = ""; SliderStart = None },
        refresh
    | StartFakeCall -> { model with FakeCallActive = true }, delayCmd 10000 EndFakeCall
    | EndFakeCall -> { model with FakeCallActive = false }, Cmd.none
    | SetLocation (lat, lng) -> { model with MyLocation = (lat, lng) }, Cmd.none
    | SetUseRealGps true ->
        { model with UseRealGps = true },
        apiCmd deps.GetGpsLocation (fun (la, ln) -> SetLocation (la, ln))
    | SetUseRealGps false ->
        { model with UseRealGps = false; MyLocation = Model.initial.MyLocation
                     SliderStart = None }, Cmd.none
    | StartDemo ->
        match model.Session with
        | Some s -> model, apiCmd (fun () -> deps.StartDemo 1 s.UserId) DemoStarted   // customer 1 = John (seed order)
        | None -> model, Cmd.ofMsg (ApiError "Not logged in")
    | DemoStarted job ->
        { model with Toast = Some (sprintf "Demo started (job #%d)" job.Id) }, Cmd.none
    | HubJobUpdated job ->
        let jobs =
            if model.Jobs |> List.exists (fun j -> j.Id = job.Id)
            then model.Jobs |> List.map (fun j -> if j.Id = job.Id then job else j)
            else job :: model.Jobs
        { model with Jobs = jobs }, Cmd.none
    | HubMessageReceived m2 ->
        let activeChatJob = match model.Screen with Chat id -> Some id | ActiveJob id -> Some id | _ -> None
        let isMine =
            match model.Session with
            | Some s -> isSelf s m2.SenderId m2.SenderRole
            | None -> false
        let append =
            activeChatJob = Some m2.JobId
            && not (model.Messages |> List.exists (fun x -> x.Id = m2.Id))
        let m = if append then { model with Messages = model.Messages @ [m2] } else model
        let cmds =
            [ // mark seen if I'm looking at this chat and it's not my own message
              match model.Screen, model.Session with
              | Chat id, Some s when id = m2.JobId && not isMine ->
                  Cmd.ofSub (fun _ -> deps.SendSeen m2.JobId s.UserId s.Role)
              | _ -> Cmd.none
              // auto-reply to the customer's message on one of my jobs
              if shouldAutoReply model.Session model m2 then
                  delayCmd 5000 (AutoReplyDue m2.JobId)
              else Cmd.none ]
        m, Cmd.batch cmds
    | HubLocationUpdated _ -> model, Cmd.none
    | HubProviderUpdated dto ->
        // Reflect my own online state if it was changed elsewhere (e.g. /dev console).
        match model.Session with
        | Some s when s.UserId = dto.Id -> { model with Online = dto.Online }, Cmd.none
        | _ -> model, Cmd.none
    | HubNotification text -> { model with Toast = Some text }, Cmd.none
    | HubTyping (jobId, senderId, senderRole) ->
        match model.Screen, model.Session with
        | Chat id, Some s when id = jobId && not (isSelf s senderId senderRole) ->
            let token = model.TypingToken + 1
            { model with CustomerTyping = true; TypingToken = token }, delayCmd 3000 (CustomerTypingExpired token)
        | _ -> model, Cmd.none
    | HubSeen (jobId, senderId, senderRole) ->
        match model.Screen, model.Session with
        | Chat id, Some s when id = jobId && not (isSelf s senderId senderRole) ->
            // The peer has seen everything I've sent on this job so far; record
            // the high-water mark rather than latching a bool forever.
            let myLatest =
                model.Messages
                |> List.filter (fun m -> m.JobId = jobId && isSelf s m.SenderId m.SenderRole)
                |> List.map (fun m -> m.Id)
                |> function [] -> None | ids -> Some (List.max ids)
            (match myLatest with
             | Some _ -> { model with SeenUpToMessageId = myLatest }
             | None -> model), Cmd.none
        | _ -> model, Cmd.none
    | CustomerTypingExpired token ->
        // Ignore stale timers: a newer HubTyping has already extended the window.
        if token = model.TypingToken then { model with CustomerTyping = false }, Cmd.none
        else model, Cmd.none
