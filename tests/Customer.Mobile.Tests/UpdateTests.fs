module FixItHere.Customer.Tests.UpdateTests

open System.Threading.Tasks
open System
open Xunit
open FixItHere.ClientShared
open FixItHere.Customer
open FixItHere.Shared
open FixItHere.Shared.Dtos

/// A receipt fixture built through the real `Money.breakdown`, so a change to
/// the fee or tax rate updates these tests instead of silently disagreeing
/// with what the app actually renders.
let receipt (jobId: int) (subtotal: decimal) : PaymentResult =
    let callOut = 90m
    let lines = FixItHere.Shared.Money.breakdown callOut 90 (subtotal - callOut)
    { JobId = jobId
      CallOutFee = lines.CallOutFee; LabourMinutes = lines.LabourMinutes
      LabourAmount = lines.LabourAmount
      Subtotal = lines.Subtotal; Tax = lines.Tax; Amount = lines.Total
      PlatformFee = lines.PlatformFee; ProviderPayout = lines.ProviderPayout
      Method = "Visa ****4242"; Status = "Transferred" }

[<Fact>]
let ``push stores current screen in history`` () =
    let m = { Model.initial with Screen = Home }
    let m2 = Nav.push m Catalog
    Assert.Equal(Catalog, m2.Screen)
    Assert.Equal<Screen list>([Home], m2.History)

[<Fact>]
let ``back pops one screen`` () =
    let m = { Model.initial with Screen = Catalog; History = [Home] }
    let m2 = Nav.back m
    Assert.Equal(Home, m2.Screen)
    Assert.Empty(m2.History)

[<Fact>]
let ``back on empty history lands on Home`` () =
    let m = { Model.initial with Screen = Catalog; History = [] }
    Assert.Equal(Home, (Nav.back m).Screen)

[<Fact>]
let ``resetTo clears history`` () =
    let m = { Model.initial with Screen = Payment 7; History = [Home; Catalog] }
    let m2 = Nav.resetTo Home m
    Assert.Equal(Home, m2.Screen)
    Assert.Empty(m2.History)

/// Minimal job for stub returns. Declared before `stubDeps` because F# is
/// order-sensitive and `mkJob` lives below it.
let private stubJob : JobDto =
    { Id = 1; CustomerId = 1; CustomerName = "John Reyes"; ProviderId = 1; ProviderName = "Mike's Plumbing"
      ServiceId = 1; ServiceName = "Plumbing"; State = "Scheduled"; Price = 85m
      ScheduledFor = "Now"; PromisedStart = "Now"
      ProposedStart = ""; ProposedBy = ""
      ProposalReason = ""; ProposalExpiresAt = ""; IsDemoTracked = true
      Lat = 43.65; Lng = -79.38; Address = "1 Demo St" }

let stubDeps : ApiDeps =
    { Login = fun _ _ -> Task.FromResult(Ok { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" })
      GetServices = fun () -> Task.FromResult(Ok [])
      GetProviders = fun _ _ _ -> Task.FromResult(Ok [])
      GetRatings = fun _ -> Task.FromResult(Ok [])
      GetJobs = fun _ -> Task.FromResult(Ok [])
      CreateJob = fun _ -> Task.FromResult(Error "unused")
      CancelJob = fun _ -> Task.FromResult(Error "unused")
      GetMessages = fun _ -> Task.FromResult(Ok [])
      SendMessage = fun _ -> Task.FromResult(Error "unused")
      SimulatePayment = fun _ -> Task.FromResult(Error "unused")
      SubmitRating = fun _ -> Task.FromResult(Error "unused")
      StartDemo = fun _ _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.65, -79.38))
      GetClock = fun () -> Task.FromResult(Ok ({ DemoNow = ""; AnchorDemo = ""; AnchorReal = ""
                                                 Rate = 1.0; Running = true } : DemoClockDto))
      GetLocation = fun pid -> Task.FromResult(Ok ({ ProviderId = pid; Lat = 43.70; Lng = -79.40
                                                     UpdatedAt = "" } : LocationDto))
      DecideReschedule = fun _ -> Task.FromResult(Ok stubJob)
      ReportNoShow = fun _ -> Task.FromResult(Ok stubJob)
      SendTyping = fun _ _ _ -> ()
      SendSeen = fun _ _ _ -> () }

let mkSession () : Session = { Token = "t"; UserId = 1; Role = "Customer"; DisplayName = "John" }

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 1; ProviderName = "Mike's Plumbing"
      ServiceId = 3; ServiceName = "Plumbing"; State = state; Price = 85m
      ScheduledFor = "Now"; PromisedStart = "Now"
      ProposedStart = ""; ProposedBy = ""
      ProposalReason = ""; ProposalExpiresAt = ""; IsDemoTracked = true
      Lat = 43.65; Lng = -79.38; Address = "1 Demo St" }

let up msg model = Update.update stubDeps msg model |> fst
/// Same as `up`, argument order flipped for piping a model through several
/// messages — which is how the notice queue has to be exercised at all.
let up' msg model = up msg model

[<Fact>]
let ``splash advances to Login`` () =
    Assert.Equal(Login, (up SplashDone { Model.initial with Screen = Splash }).Screen)

[<Fact>]
let ``login stores session and lands on Home with empty history`` () =
    let resp : LoginResponse = { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    let expected : Session = { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" }
    Assert.Equal(Some expected, m.Session)

[<Fact>]
let ``navigate pushes current screen`` () =
    let m = up (Navigate Catalog) { Model.initial with Screen = Home }
    Assert.Equal(Catalog, m.Screen)
    Assert.Equal<Screen list>([Home], m.History)

[<Fact>]
let ``job created goes to Tracking with job stored`` () =
    let m = up (JobCreated (mkJob 42 "Scheduled")) { Model.initial with Screen = Booking (2, 3) }
    Assert.Equal(Tracking 42, m.Screen)
    Assert.True(m.Jobs |> List.exists (fun j -> j.Id = 42))

[<Fact>]
let ``api error sets banner`` () =
    Assert.Equal(Some "boom", (up (ApiError "boom") Model.initial).Error)

/// From the provider. SenderId 1 deliberately collides with customer 1 — that is
/// the real seed shape (Mike's Plumbing is provider 1, John is customer 1), and
/// the pre-fix id-only comparisons mistook this for the customer's own message.
let mkChatMsg id jobId : MessageDto =
    { Id = id; JobId = jobId; SenderId = 1; SenderRole = "Provider"; SenderName = "Mike's Plumbing"
      Text = "On my way"; PhotoBase64 = null; SentAt = "2026-01-01T00:00:00Z"; Seen = false }

[<Fact>]
let ``hub job update upserts by id`` () =
    let m0 = { Model.initial with Jobs = [mkJob 7 "Scheduled"] }
    let m = up (HubJobUpdated (mkJob 7 "EnRoute")) m0
    Assert.Equal("EnRoute", (m.Jobs |> List.find (fun j -> j.Id = 7)).State)
    Assert.Equal(1, List.length m.Jobs)

[<Fact>]
let ``completed job while tracking advances to Payment`` () =
    let m0 = { Model.initial with Screen = Tracking 7; Jobs = [mkJob 7 "InProgress"] }
    let m = up (HubJobUpdated (mkJob 7 "Completed")) m0
    Assert.Equal(Payment 7, m.Screen)

[<Fact>]
let ``completed job on another screen does not navigate`` () =
    let m0 = { Model.initial with Screen = Home; Jobs = [mkJob 7 "InProgress"] }
    Assert.Equal(Home, (up (HubJobUpdated (mkJob 7 "Completed")) m0).Screen)

[<Fact>]
let ``hub message appends only for the active chat job and dedupes`` () =
    let m0 = { Model.initial with Screen = Chat 7; Messages = [mkChatMsg 1 7] }
    let m1 = up (HubMessageReceived (mkChatMsg 2 7)) m0
    Assert.Equal(2, List.length m1.Messages)
    let m2 = up (HubMessageReceived (mkChatMsg 2 7)) m1      // duplicate id
    Assert.Equal(2, List.length m2.Messages)
    let m3 = up (HubMessageReceived (mkChatMsg 3 99)) m2     // other job
    Assert.Equal(2, List.length m3.Messages)

[<Fact>]
let ``hub location updates position map only`` () =
    let loc : LocationDto = { ProviderId = 2; Lat = 43.7; Lng = -79.4; UpdatedAt = "" }
    let m = up (HubLocationUpdated loc) Model.initial
    Assert.Equal((43.7, -79.4), m.ProviderPositions.[2])

[<Fact>]
let ``hub notifications queue instead of replacing each other`` () =
    // The old model held one `Toast: string option`, so a second notification
    // silently discarded the first — and the two-sided beats this phase is
    // built around arrive in pairs.
    let m =
        Model.initial
        |> up' (HubNotification "Provider Accepted")
        |> up' (HubNotification "Provider is running late")
    Assert.Equal(2, List.length m.Notices)
    Assert.Equal("Provider is running late", (List.head m.Notices).Text)
    // ...and they are classified, not all rendered the same.
    Assert.Equal(NoticeKind.Warning, (List.head m.Notices).Kind)
    Assert.Equal(NoticeKind.Success, (List.item 1 m.Notices).Kind)

[<Fact>]
let ``a notice expires on demo time, and the tick is what expires it`` () =
    // Expiry in demo time means pausing the clock to talk over a beat also
    // pauses dismissal — impossible if this were a real-time timer.
    let m0 = up' (HubNotification "Provider Accepted") Model.initial
    Assert.Single m0.Notices |> ignore
    let stillFresh = up' DemoTick { m0 with Clock = None; DemoNow = m0.DemoNow }
    Assert.Single stillFresh.Notices |> ignore
    let later = { m0 with DemoNow = m0.DemoNow + Notify.lifetime + TimeSpan.FromSeconds 1.0 }
    Assert.Empty (up' DemoTick later).Notices

[<Fact>]
let ``payment result stored`` () =
    let r : PaymentResult = receipt 7 85m
    Assert.Equal(Some r, (up (PaymentSimulated r) Model.initial).PaymentResult)

[<Fact>]
let ``rating submitted resets to Home and clears payment`` () =
    let m0 = { Model.initial with Screen = Rating 7; History = [Payment 7; Tracking 7; Home]
                                  PaymentResult = Some (receipt 7 85m) }
    let m = up RatingSubmitted m0
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(None, m.PaymentResult)
    Assert.False(List.isEmpty m.Notices)

[<Fact>]
let ``fake call toggles`` () =
    let m = up StartFakeCall Model.initial
    Assert.True(m.FakeCallActive)
    Assert.False((up EndFakeCall m).FakeCallActive)

[<Fact>]
let ``set location updates model`` () =
    Assert.Equal((43.59, -79.64), (up (SetLocation (43.59, -79.64)) Model.initial).MyLocation)

[<Fact>]
let ``sixth photo for a job is rejected with an error`` () =
    let photoMsg id : MessageDto =
        { Id = id; JobId = 7; SenderId = 1; SenderRole = "Customer"; SenderName = "John"
          Text = ""; PhotoBase64 = "ZmFrZQ=="; SentAt = ""; Seen = false }
    let m0 =
        { Model.initial with
            Session = Some (mkSession ())
            Screen = Chat 7
            Messages = [ for i in 1 .. 5 -> photoMsg i ] }
    let m = up (PickAndSendPhoto 7) m0
    Assert.True(m.Error.IsSome)

[<Fact>]
let ``chat draft tracks input and clears on send`` () =
    let session = Some (mkSession ())
    let m1 = up (ChatDraftChanged (7, "hello")) { Model.initial with Session = session }
    Assert.Equal("hello", draftFor m1.ChatDrafts 7)
    Assert.Equal("", draftFor (up (SendChatMessage (7, "hello", null)) m1).ChatDrafts 7)

[<Fact>]
let ``stars and comment update`` () =
    let m = up (StarsChanged 3) Model.initial
    Assert.Equal(3, m.RatingStars)
    Assert.Equal("great", (up (RatingCommentChanged "great") m).RatingComment)

[<Fact>]
let ``geo distance Toronto to Mississauga is about 21km`` () =
    let d = Geo.distanceKm (43.6532, -79.3832) (43.5890, -79.6441)
    Assert.InRange(d, 19.0, 24.0)

[<Fact>]
let ``navigating to Payment clears any stale payment result`` () =
    let m0 =
        { Model.initial with
            Screen = Tracking 7
            PaymentResult = Some (receipt 3 85m) }
    let m = up (Navigate (Payment 7)) m0
    Assert.Equal(Payment 7, m.Screen)
    Assert.Equal(None, m.PaymentResult)

[<Fact>]
let ``cancelled job while tracking navigates to Home`` () =
    let m0 = { Model.initial with Screen = Tracking 7; Jobs = [mkJob 7 "InProgress"] }
    let m = up (HubJobUpdated (mkJob 7 "Cancelled")) m0
    Assert.Equal(Home, m.Screen)
    Assert.Equal("Cancelled", (m.Jobs |> List.find (fun j -> j.Id = 7)).State)

[<Fact>]
let ``disabling real GPS resets location to the seed default`` () =
    let m0 = { Model.initial with UseRealGps = true; MyLocation = (10.0, 20.0) }
    let m = up (SetUseRealGps false) m0
    Assert.False(m.UseRealGps)
    Assert.Equal(Model.initial.MyLocation, m.MyLocation)

[<Fact>]
let ``typing cooldown blocks resend until done`` () =
    let session = Some (mkSession ())
    let m0 = { Model.initial with Screen = Chat 7; Session = session }
    let m1 = up (ChatDraftChanged (7, "h")) m0
    Assert.True(m1.TypingCooldown)
    let m2 = up TypingCooldownDone m1
    Assert.False(m2.TypingCooldown)

[<Fact>]
let ``hub typing shows indicator for open chat only`` () =
    let session = Some (mkSession ())
    let m0 = { Model.initial with Screen = Chat 7; Session = session }
    Assert.True((up (HubTyping (7, 1, "Provider")) m0).ProviderTyping)
    Assert.False((up (HubTyping (99, 1, "Provider")) m0).ProviderTyping)
    let typing = up (HubTyping (7, 1, "Provider")) m0
    Assert.False((up (TypingExpired typing.TypingToken) typing).ProviderTyping)

[<Fact>]
let ``hub seen marks messages seen`` () =
    let session = Some (mkSession ())
    // The watermark records which of MY messages were seen, so one must exist.
    let mine : MessageDto =
        { Id = 10; JobId = 7; SenderId = 1; SenderRole = "Customer"; SenderName = "John"
          Text = "hi"; PhotoBase64 = null; SentAt = ""; Seen = false }
    let m0 = { Model.initial with Screen = Chat 7; Session = session; Messages = [mine] }
    Assert.Equal(Some 10, (up (HubSeen (7, 1, "Provider")) m0).SeenUpToMessageId)

[<Fact>]
let ``seen watermark is cleared when a chat loads`` () =
    let m0 =
        { Model.initial with
            Screen = Chat 7; Session = Some (mkSession ()); SeenUpToMessageId = Some 99 }
    Assert.Equal(None, (up (MessagesLoaded []) m0).SeenUpToMessageId)

[<Fact>]
let ``start demo errors when not logged in`` () =
    let _, cmd = Update.update stubDeps StartDemo Model.initial
    let mutable dispatched = []
    for sub in cmd do sub (fun m -> dispatched <- m :: dispatched)
    Assert.Contains(ApiError "Not logged in", dispatched)

// ---------------------------------------------------------------------------
// Cmd-executing tests — mirrors Provider.Mobile. The `up` helper discards the
// Cmd, and SendTyping/SendSeen only ever run inside one, so flag-only tests
// could not detect a removed throttle.
// ---------------------------------------------------------------------------

let private runWith deps msg model =
    let m, cmd = Update.update deps msg model
    let dispatched = ResizeArray<Msg>()
    for sub in cmd do sub dispatched.Add
    m, List.ofSeq dispatched

[<Fact>]
let ``typing throttle actually suppresses the second send`` () =
    let typing = ResizeArray<int * int * string>()
    let deps = { stubDeps with SendTyping = fun j s r -> typing.Add(j, s, r) }
    let m0 = { Model.initial with Screen = Chat 7; Session = Some (mkSession ()) }
    let m1, _ = runWith deps (ChatDraftChanged (7, "h")) m0
    Assert.Equal(1, typing.Count)
    let m2, _ = runWith deps (ChatDraftChanged (7, "he")) m1
    Assert.Equal(1, typing.Count)
    let m3, _ = runWith deps TypingCooldownDone m2
    runWith deps (ChatDraftChanged (7, "hel")) m3 |> ignore
    Assert.Equal(2, typing.Count)
    let (_, senderId, senderRole) = typing.[0]
    Assert.Equal(1, senderId)
    Assert.Equal("Customer", senderRole)

[<Fact>]
let ``seen is sent only for the chat that is open`` () =
    let seen = ResizeArray<int * int * string>()
    let deps = { stubDeps with SendSeen = fun j s r -> seen.Add(j, s, r) }
    let incoming = mkChatMsg 10 7
    let openChat = { Model.initial with Screen = Chat 7; Session = Some (mkSession ()) }
    runWith deps (HubMessageReceived incoming) openChat |> ignore
    Assert.Equal(1, seen.Count)
    let otherChat = { Model.initial with Screen = Chat 9; Session = Some (mkSession ()) }
    runWith deps (HubMessageReceived incoming) otherChat |> ignore
    Assert.Equal(1, seen.Count)

[<Fact>]
let ``a stale typing-expiry timer does not clear an extended indicator`` () =
    let m0 = { Model.initial with Screen = Chat 7; Session = Some (mkSession ()) }
    let m1 = up (HubTyping (7, 1, "Provider")) m0
    let m2 = up (HubTyping (7, 1, "Provider")) m1
    Assert.True(m2.ProviderTyping)
    Assert.True((up (TypingExpired 1) m2).ProviderTyping)
    Assert.False((up (TypingExpired m2.TypingToken) m2).ProviderTyping)

[<Fact>]
let ``hub provider update refreshes the cached provider list`` () =
    let p0 : ProviderDto =
        { Id = 1; BusinessName = "Mike's Plumbing"; ServiceId = 3; ServiceName = "Plumbing"
          Rating = 4.0; RatingCount = 2; Lat = 43.6; Lng = -79.4
          Online = false; Vehicle = "White van"; PhotoUrl = "" }
    let m0 = { Model.initial with Providers = [p0] }
    let m1 = up (HubProviderUpdated { p0 with Online = true }) m0
    Assert.True((m1.Providers |> List.head).Online)

[<Fact>]
let ``the demo tick pump starts once, however many clock syncs arrive`` () =
    // This assertion has to look at the *Cmd*, not the model. The `up` helper
    // discards it, and the guard lives entirely in whether a second
    // `delayCmd tickMs DemoTick` is emitted — this repo has already shipped a
    // guard with zero real coverage for exactly that reason.
    //
    // Without the guard every ClockUpdated push adds another pump, and each one
    // dispatches DemoTick forever: countdowns would update at 4 Hz, then 8, then
    // 12, for the rest of the session.
    let dto : DemoClockDto =
        { DemoNow = ""; AnchorDemo = DemoClock.epoch.ToString "o"
          AnchorReal = DateTimeOffset.UtcNow.ToString "o"; Rate = 1.0; Running = true }
    let m1, cmd1 = Update.update stubDeps (ClockSynced dto) Model.initial
    Assert.True m1.TickActive
    Assert.False(List.isEmpty cmd1)          // the first sync starts the pump

    let _, cmd2 = Update.update stubDeps (ClockSynced dto) m1
    Assert.True(List.isEmpty cmd2)           // every later sync must not

[<Fact>]
let ``a malformed clock leaves the last known map in place`` () =
    // A broken countdown is cosmetic; a crash on the tracking screen ends the
    // demo. Anchors that will not parse must be ignored, not adopted.
    let bad : DemoClockDto =
        { DemoNow = ""; AnchorDemo = "not a time"; AnchorReal = "also not"
          Rate = 1.0; Running = true }
    let m, cmd = Update.update stubDeps (ClockSynced bad) Model.initial
    Assert.True m.Clock.IsNone
    Assert.False m.TickActive
    Assert.True(List.isEmpty cmd)

[<Fact>]
let ``answering a proposal says what declining actually does`` () =
    // Declining does not cancel anything — it holds the provider to the time
    // they already agreed. The notice has to say so, or a customer reads
    // "Decline" as a threat they did not intend to make.
    let accepted = up (AnswerReschedule (7, true)) Model.initial
    Assert.Contains("accepted", (List.head accepted.Notices).Text)
    Assert.Equal(NoticeKind.Success, (List.head accepted.Notices).Kind)

    let declined = up (AnswerReschedule (7, false)) Model.initial
    Assert.Contains("original time still stands", (List.head declined.Notices).Text)
    Assert.Equal(NoticeKind.Warning, (List.head declined.Notices).Kind)

[<Fact>]
let ``answering a proposal actually calls the server`` () =
    // The guard lives in the emitted Cmd; a model-only assertion would pass
    // against a handler that queued a notice and did nothing else.
    let _, cmd = Update.update stubDeps (AnswerReschedule (7, true)) Model.initial
    Assert.False(List.isEmpty cmd)
    let _, noShowCmd = Update.update stubDeps (ReportNoShow 7) Model.initial
    Assert.False(List.isEmpty noShowCmd)
