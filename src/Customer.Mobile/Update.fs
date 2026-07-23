module FixItHere.Customer.Update

open System
open System.Threading.Tasks
open Fabulous
open FixItHere.Shared
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

/// 250 ms, not 1000. At 60x a one-second tick advances demo time a full
/// minute, and every countdown on screen visibly skips.
let tickMs = 250

/// Queue a notice. Kind and job scope come from the caller because only the
/// caller knows what the message is about.
let private notify kind jobId text (m: Model) : Model * Cmd<Msg> =
    let id = m.NextNoticeId
    let m =
        { m with
            Notices = Notify.push (Notify.create id kind jobId m.DemoNow text) m.Notices
            NextNoticeId = id + 1 }
    // Non-Ask notices clear themselves after ~7s of *real* time — independent of
    // the demo clock's rate — so they never pile up or sit over the screen. Ask
    // waits for its answer and is dismissed when the answer is given.
    m, (match kind with NoticeKind.Ask -> Cmd.none | _ -> delayCmd 7000 (DismissNotice id))


/// Turn the wire DTO into the affine map. A malformed clock leaves the app on
/// its last known map rather than throwing: a broken countdown is a cosmetic
/// failure, a crash on the tracking screen ends the demo.
let private clockOfDto (d: DemoClockDto) =
    let parse (s: string) =
        match DateTimeOffset.TryParse(s, Globalization.CultureInfo.InvariantCulture,
                                      Globalization.DateTimeStyles.RoundtripKind) with
        | true, v -> Some v
        | _ -> None
    match parse d.AnchorDemo, parse d.AnchorReal with
    | Some ad, Some ar -> Some { AnchorDemo = ad; AnchorReal = ar; Rate = d.Rate; Running = d.Running }
    | _ -> None

let init () = Model.initial, delayCmd 1500 SplashDone

let update (deps: ApiDeps) (msg: Msg) (model: Model) : Model * Cmd<Msg> =
    match msg with
    | SplashDone ->
        // Restore rather than always landing on Login. An app backgrounded and
        // killed mid-demo used to come back to a sign-in screen, which reads as
        // the session having expired rather than the process having restarted.
        match deps.RestoreSession () with
        | None -> { model with Screen = Login; History = [] }, Cmd.none
        | Some s ->
            Nav.resetTo Home { model with Session = Some s },
            Cmd.batch
                [ apiCmd (fun () -> deps.GetJobs s.UserId) JobsLoaded
                  apiCmd deps.GetClock ClockSynced ]
    | LoginEmailChanged e -> { model with LoginEmail = e }, Cmd.none
    | LoginPasswordChanged p -> { model with LoginPassword = p }, Cmd.none
    | SignIn when model.SigningIn -> model, Cmd.none    // ignore a double tap
    | SignIn ->
        { model with SigningIn = true; Error = None },
        apiCmd (fun () -> deps.Login model.LoginEmail model.LoginPassword) LoggedIn
    | LoggedIn resp ->
        let model = { model with SigningIn = false }
        // Session and LoginResponse now share a field set, so annotate explicitly.
        let session : Session = { Token = resp.Token; UserId = resp.UserId
                                  Role = resp.Role; DisplayName = resp.DisplayName }
        deps.SaveSession (Some session)
        Nav.resetTo Home { model with Session = Some session },
        Cmd.batch
            [ apiCmd (fun () -> deps.GetJobs resp.UserId) JobsLoaded
              apiCmd deps.GetClock ClockSynced
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
            | Tracking jobId, _ ->
                // Seed the provider's position rather than waiting for a push.
                match model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
                | Some j -> apiCmd (fun () -> deps.GetLocation j.ProviderId) HubLocationUpdated
                | None -> Cmd.none
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
        // resetTo, not push. Pushing left Booking underneath Tracking, so
        // back-back-tap booked the same job a second time — and an investor
        // handed the phone does exactly that. Back from Tracking now lands on
        // Home, which is also where someone who just booked expects to be.
        let m = { model with Jobs = job :: model.Jobs }
        // Seed the provider's position here too. `Nav.resetTo` does not go
        // through the `Navigate` handler, so arriving at Tracking *by booking*
        // skipped the fetch that arriving by tap performs — and the ETA line
        // sat on "Locating provider…" for the whole wait. A regression from
        // the back-re-books fix, caught by the walkthrough.
        Nav.resetTo (Tracking job.Id) m,
        apiCmd (fun () -> deps.GetLocation job.ProviderId) HubLocationUpdated
    | ApiError e -> { model with Error = Some e; SigningIn = false }, Cmd.none
    | DismissError -> { model with Error = None }, Cmd.none
    | ClockSynced dto ->
        match clockOfDto dto with
        | None -> model, Cmd.none
        | Some c ->
        // Adopt the map and take one tick immediately, so the first frame
        // after sign-in shows real demo time rather than the epoch.
        let m = { model with Clock = Some c; DemoNow = DemoClock.nowAt c DateTimeOffset.UtcNow }
        // Guarded: ClockSynced fires on sign-in *and* on every ClockUpdated
        // push, and starting a second pump would double every countdown's
        // update rate for the rest of the session.
        if m.TickActive then (m, Cmd.none)
        else ({ m with TickActive = true }, delayCmd tickMs DemoTick)
    | DemoTick ->
        // The single pump. Everything time-dependent in this app is a pure
        // function of DemoNow, so there is exactly one repeating timer in the
        // process and a moved deadline can never strand a stale callback.
        let now =
            match model.Clock with
            | Some c -> DemoClock.nowAt c DateTimeOffset.UtcNow
            | None -> model.DemoNow
        { model with DemoNow = now; Notices = Notify.prune now model.Notices },
        delayCmd tickMs DemoTick
    | DismissNotice id -> { model with Notices = Notify.dismiss id model.Notices }, Cmd.none
    | RequestCancel jobId -> { model with ConfirmingCancel = Some jobId }, Cmd.none
    | DismissCancel -> { model with ConfirmingCancel = None }, Cmd.none
    | CancelActiveJob jobId ->
        let req : ReportNoShowRequest =
            { JobId = jobId; ByRole = ActorRole.toWire ActorRole.Customer }
        { model with ConfirmingCancel = None }, apiCmd (fun () -> deps.CancelJob req) HubJobUpdated
    | AnswerReschedule (jobId, accept) ->
        let req : RescheduleDecisionRequest =
            { JobId = jobId; ByRole = ActorRole.toWire ActorRole.Customer; Accept = accept }
        // The wording matters: declining does not cancel anything, it holds the
        // provider to the time they already agreed. Saying so here stops the
        // decline reading as a threat the customer did not intend.
        let said =
            if accept then "New arrival time accepted"
            else "Declined — the original time still stands"
        let m2, ncmd = notify (if accept then NoticeKind.Success else NoticeKind.Warning) (Some jobId) said model
        m2, Cmd.batch [ ncmd; apiCmd (fun () -> deps.DecideReschedule req) HubJobUpdated ]
    | ReportNoShow jobId ->
        let req : ReportNoShowRequest =
            { JobId = jobId; ByRole = ActorRole.toWire ActorRole.Customer }
        model, apiCmd (fun () -> deps.ReportNoShow req) HubJobUpdated
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
    | StarsChanged n -> { model with RatingStars = n }, Cmd.none
    | RatingCommentChanged t -> { model with RatingComment = t }, Cmd.none
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
        // Spec: at most 5 photos per job from this customer.
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
    | PaymentDelayDone jobId ->
        model, apiCmd (fun () -> deps.SimulatePayment jobId) PaymentSimulated
    | PaymentSimulated r -> { model with PaymentResult = Some r }, Cmd.none
    | SubmitRating (jobId, stars, comment) ->
        match model.Session, model.Jobs |> List.tryFind (fun j -> j.Id = jobId) with
        | Some s, Some job ->
            let req =
                { JobId = jobId
                  RaterId = s.UserId; RaterRole = s.Role
                  RateeId = job.ProviderId; RateeRole = "Provider"
                  Stars = stars; Comment = comment }
            model, apiCmd (fun () -> deps.SubmitRating req) (fun _ -> RatingSubmitted)
        | _ -> model, Cmd.ofMsg (ApiError "Job not found")
    | RatingSubmitted ->
        let refresh =
            match model.Session with
            | Some s -> apiCmd (fun () -> deps.GetJobs s.UserId) JobsLoaded
            | None -> Cmd.none
        let thanked, ncmd = notify NoticeKind.Success None "Thanks for your rating!" model
        Nav.resetTo Home
            { thanked with
                PaymentResult = None
                RatingStars = 5
                RatingComment = "" },
        Cmd.batch [ ncmd; refresh ]
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
        let isMine =
            match model.Session with
            | Some s -> isSelf s m2.SenderId m2.SenderRole
            | None -> false
        let activeJob =
            match model.Screen with
            | Chat id | Tracking id -> Some id
            | _ -> None
        let append =
            activeJob = Some m2.JobId
            && not (model.Messages |> List.exists (fun x -> x.Id = m2.Id))
        let m = if append then { model with Messages = model.Messages @ [m2] } else model
        let cmd =
            // mark seen if I'm looking at this chat and it's not my own message
            match model.Screen, model.Session with
            | Chat id, Some s when id = m2.JobId && not isMine ->
                Cmd.ofSub (fun _ -> deps.SendSeen m2.JobId s.UserId s.Role)
            | _ -> Cmd.none
        m, cmd
    | HubLocationUpdated loc ->
        { model with ProviderPositions = model.ProviderPositions.Add(loc.ProviderId, (loc.Lat, loc.Lng)) },
        Cmd.none
    | HubProviderUpdated dto ->
        // Keep the cached provider list fresh so online/offline changes are
        // reflected in the catalogue.
        let providers =
            if model.Providers |> List.exists (fun p -> p.Id = dto.Id)
            then model.Providers |> List.map (fun p -> if p.Id = dto.Id then dto else p)
            else model.Providers
        { model with Providers = providers }, Cmd.none
    | HubNotification text ->
        // Classified rather than dumped into one grey bar: a no-show and an
        // acceptance should not look identical.
        notify (Notify.classify text) None text model
    | HubTyping (jobId, senderId, senderRole) ->
        match model.Screen, model.Session with
        | Chat id, Some s when id = jobId && not (isSelf s senderId senderRole) ->
            let token = model.TypingToken + 1
            { model with ProviderTyping = true; TypingToken = token }, delayCmd 3000 (ProviderTypingExpired token)
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
    | ProviderTypingExpired token ->
        // Ignore stale timers: a newer HubTyping has already extended the window.
        if token = model.TypingToken then { model with ProviderTyping = false }, Cmd.none
        else model, Cmd.none
