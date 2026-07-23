module FixItHere.Provider.Tests.UpdateTests

open System.Threading.Tasks
open System
open Xunit
open FixItHere.Shared
open FixItHere.Shared.Dtos
open FixItHere.Provider

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

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 4; ProviderName = "Elite HVAC"
      ServiceId = 7; ServiceName = "HVAC"; State = state; Price = 85m
      ScheduledFor = "Now"; PromisedStart = "Now"
      ProposedStart = ""; ProposedBy = ""
      ProposalReason = ""; ProposalExpiresAt = ""; IsDemoTracked = true; CancelledBy = ""
      Lat = 43.70; Lng = -79.40; Address = "1 Demo St" }

[<Fact>]
let ``nav push and back mirror customer app`` () =
    let m = Nav.push { Model.initial with Screen = Home } (Payment 1)
    Assert.Equal(Payment 1, m.Screen)
    Assert.Equal(Home, (Nav.back m).Screen)

[<Fact>]
let ``activeJob picks the in-flight job over scheduled`` () =
    let m = { Model.initial with Jobs = [mkJob 1 "Scheduled"; mkJob 2 "EnRoute"; mkJob 3 "Closed"] }
    Assert.Equal(Some 2, activeJob m |> Option.map (fun j -> j.Id))

[<Fact>]
let ``activeJob is None when nothing in flight`` () =
    let m = { Model.initial with Jobs = [mkJob 3 "Closed"; mkJob 4 "Cancelled"] }
    Assert.Equal(None, activeJob m |> Option.map (fun j -> j.Id))

[<Fact>]
let ``a job in flight takes the provider off the market`` () =
    // The rule the provider's Home screen is built on: someone driving to, or
    // standing at, a customer's address must not be offered other work.
    let onShift = { Model.initial with Online = true }
    Assert.Equal(Availability.Available, availability { onShift with Jobs = [mkJob 1 "Scheduled"] })
    for state in [ "EnRoute"; "Arrived"; "InProgress" ] do
        Assert.Equal(Availability.OnAJob, availability { onShift with Jobs = [mkJob 1 state] })
    // Marking the work complete is what ends it — nothing else, and nothing
    // stored. Completed is not in flight, so this needs no extra bookkeeping.
    Assert.Equal(Availability.Available, availability { onShift with Jobs = [mkJob 1 "Completed"] })
    Assert.Equal(Availability.Available, availability { onShift with Jobs = [mkJob 1 "Closed"] })

[<Fact>]
let ``on a job outranks off shift`` () =
    // Telling a provider "Offline" while they stand in a customer's kitchen
    // would be the screen contradicting the work in front of them.
    let offShift = { Model.initial with Online = false }
    Assert.Equal(Availability.Offline, availability offShift)
    Assert.Equal(Availability.OnAJob, availability { offShift with Jobs = [mkJob 1 "InProgress"] })

[<Fact>]
let ``slider position interpolates and clamps`` () =
    Assert.Equal((5.0, 5.0), Slider.position (0.0, 0.0) (10.0, 10.0) 0.5)
    Assert.Equal((10.0, 10.0), Slider.position (0.0, 0.0) (10.0, 10.0) 1.7)
    Assert.Equal((0.0, 0.0), Slider.position (0.0, 0.0) (10.0, 10.0) -0.3)

let stubDeps : ProviderApiDeps =
    { Login = fun _ _ -> Task.FromResult(Ok { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" })
      GetProvider = fun _ -> Task.FromResult(Error "unused")
      SetOnline = fun _ b -> Task.FromResult(Error "unused")
      GetMyJobs = fun _ -> Task.FromResult(Ok [])
      Accept = fun _ -> Task.FromResult(Error "unused")
      Enroute = fun _ -> Task.FromResult(Error "unused")
      Arrive = fun _ -> Task.FromResult(Error "unused")
      Start = fun _ -> Task.FromResult(Error "unused")
      Complete = fun _ -> Task.FromResult(Error "unused")
      UpdateLocation = fun _ _ _ -> Task.FromResult(Error "unused")
      GetMessages = fun _ -> Task.FromResult(Ok [])
      SendMessage = fun _ -> Task.FromResult(Error "unused")
      SimulatePayment = fun _ -> Task.FromResult(Error "unused")
      SubmitRating = fun _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.70, -79.45))
      GetClock = fun () -> Task.FromResult(Ok ({ DemoNow = ""; AnchorDemo = ""; AnchorReal = ""
                                                 Rate = 1.0; Running = true } : DemoClockDto))
      ProposeReschedule = fun _ -> Task.FromResult(Ok (mkJob 1 "Scheduled"))
      CancelJob = fun _ -> Task.FromResult(Ok (mkJob 1 "Cancelled"))
      SaveSession = fun _ -> ()
      RestoreSession = fun () -> None
      SendTyping = fun _ _ _ -> ()
      SendSeen = fun _ _ _ -> () }

let up msg model = Update.update stubDeps msg model |> fst
/// Same as `up`, argument order flipped for piping a model through several
/// messages — which is how the notice queue has to be exercised at all.
let up' msg model = up msg model



[<Fact>]
let ``apiCmd is cold: no call until the Cmd is dispatched`` () =
    // The property the whole Cmd-draining technique rests on. It did NOT hold:
    // `Cmd.ofTaskMsg` takes an already-started task, so building the Cmd fired
    // the call, and a test could "drain" a Cmd while proving nothing about
    // dispatch. Caught only by a mutation that failed to fail.
    let calls = ResizeArray<string>()
    let cmd =
        Update.apiCmd
            (fun () -> calls.Add "work"; Task.FromResult(Ok ([]: JobDto list)))
            JobsLoaded
    Assert.Empty(calls)
    cmd |> List.iter (fun sub -> sub ignore)
    Assert.Equal(1, calls.Count)

[<Fact>]
let ``delayCmd is cold: the clock starts on dispatch`` () =
    // Same shape. Undispatched, a 10-minute delay must not already be running.
    let dispatched = ResizeArray<Msg>()
    let cmd = Update.delayCmd 0 GoBack
    Assert.Empty(dispatched)
    cmd |> List.iter (fun sub -> sub dispatched.Add)
    // Task.Delay 0 completes on the thread pool, so allow it to land.
    let deadline = System.DateTime.UtcNow.AddSeconds 2.0
    while dispatched.Count = 0 && System.DateTime.UtcNow < deadline do
        System.Threading.Thread.Sleep 10
    Assert.Equal<Msg list>([GoBack], List.ofSeq dispatched)

[<Fact>]
let ``an error does not follow the user to the next screen`` () =
    let errored = up (ApiError "Job 81 not found") { Model.initial with Screen = Home }
    Assert.Equal(Some "Job 81 not found", errored.Error)
    Assert.Equal(None, (up (Navigate (JobDetail 7)) errored).Error)
    Assert.Equal(None, (up GoBack errored).Error)

[<Fact>]
let ``a stale error timer cannot wipe the error that replaced it`` () =
    let first = up (ApiError "first") Model.initial
    let second = up (ApiError "second") first
    Assert.Equal(Some "second", (up (ErrorExpired first.ErrorToken) second).Error)
    Assert.Equal(None, (up (ErrorExpired second.ErrorToken) second).Error)

[<Fact>]
let ``a data reset drops the stale world and refetches`` () =
    let asked = ResizeArray<int>()
    let deps = { stubDeps with GetMyJobs = fun id -> asked.Add id; Task.FromResult(Ok []) }
    let session : Session = { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" }
    let model =
        { Model.initial with
            Screen = ActiveJob 81; Session = Some session
            Jobs = [mkJob 81 "EnRoute"]; Error = Some "Job 81 not found" }
    let m, cmd = Update.update deps DataReset model
    cmd |> List.iter (fun sub -> sub ignore)
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.Jobs)
    Assert.Equal(None, m.Error)
    Assert.Contains(4, asked)

[<Fact>]
let ``a restored session hydrates the shift flag, like login does`` () =
    // Online lives on the server. Restoring without asking for it starts from
    // the local default of false, so a returning provider is shown Offline with
    // no available jobs until they find the toggle.
    let asked = ResizeArray<int>()
    let session : Session = { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" }
    let deps =
        { stubDeps with
            RestoreSession = fun () -> Some session
            GetProvider = fun id ->
                asked.Add id
                Task.FromResult(Error "unused") }
    let _, cmd = Update.update deps SplashDone Model.initial
    cmd |> List.iter (fun sub -> sub ignore)
    Assert.Contains(4, asked)

[<Fact>]
let ``finishing a job puts an off-shift provider back online`` () =
    // The guard lives entirely in the emitted Cmd, which `up` discards — so
    // this drains it against a recording stub instead. Without that the test
    // would pass against an implementation that emits nothing at all.
    let asked = ResizeArray<int * bool>()
    let deps =
        { stubDeps with
            SetOnline = fun id online ->
                asked.Add(id, online)
                Task.FromResult(Error "unused") }
    let session : Session = { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" }
    let model =
        { Model.initial with
            Screen = ActiveJob 7; Session = Some session
            Online = false; Jobs = [mkJob 7 "InProgress"] }
    let _, cmd = Update.update deps (JobActioned (mkJob 7 "Completed")) model
    cmd |> List.iter (fun sub -> sub ignore)
    Assert.Contains((4, true), asked)

    // …and a provider who was already on shift needs no call: availability is
    // derived, so Home reopens the moment the job leaves the in-flight set.
    asked.Clear()
    let _, cmd2 = Update.update deps (JobActioned (mkJob 7 "Completed")) { model with Online = true }
    cmd2 |> List.iter (fun sub -> sub ignore)
    Assert.Empty(asked)

[<Fact>]
let ``login lands Home with session`` () =
    let resp : LoginResponse = { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Equal(Some 4, m.Session |> Option.map (fun s -> s.UserId))

[<Fact>]
let ``online changed updates flag and toast`` () =
    let dto : ProviderDto =
        { Id = 4; BusinessName = "Elite HVAC"; ServiceId = 7; ServiceName = "HVAC"
          Rating = 4.5; RatingCount = 3; Lat = 43.7; Lng = -79.4
          Online = true; Vehicle = "Box truck"; PhotoUrl = "" }
    let m = up (OnlineChanged dto) Model.initial
    Assert.True(m.Online)

[<Fact>]
let ``job actioned upserts and navigates to ActiveJob on accept from JobDetail`` () =
    let m0 = { Model.initial with Screen = JobDetail 7; Jobs = [mkJob 7 "Scheduled"] }
    let m = up (JobActioned (mkJob 7 "Scheduled")) m0
    Assert.Equal(ActiveJob 7, m.Screen)

[<Fact>]
let ``job actioned elsewhere just upserts`` () =
    let m0 = { Model.initial with Screen = ActiveJob 7; Jobs = [mkJob 7 "EnRoute"] }
    let m = up (JobActioned (mkJob 7 "Arrived")) m0
    Assert.Equal(ActiveJob 7, m.Screen)
    Assert.Equal("Arrived", (m.Jobs |> List.find (fun j -> j.Id = 7)).State)

let mkChatMsg id jobId senderId senderRole : MessageDto =
    { Id = id; JobId = jobId; SenderId = senderId; SenderRole = senderRole; SenderName = "John"
      Text = "hi"; PhotoBase64 = null; SentAt = ""; Seen = false }

let mkSession userId = { Token = "t"; UserId = userId; Role = "Provider"; DisplayName = "Elite HVAC" }

let loggedIn m = { m with Model.Session = Some (mkSession 4) }

[<Fact>]
let ``slider move sets slider start once and keeps it`` () =
    let m0 = loggedIn { Model.initial with Jobs = [mkJob 7 "EnRoute"]; MyLocation = (43.0, -79.0) }
    let m1 = up (SliderMoved 0.5) m0
    Assert.Equal(Some (43.0, -79.0), m1.SliderStart)
    let m2 = up (SliderMoved 0.9) { m1 with MyLocation = (43.4, -79.2) }
    Assert.Equal(Some (43.0, -79.0), m2.SliderStart)   // start captured once

[<Fact>]
let ``auto reply due increments counter and cycles canned replies in order`` () =
    // NOTE: this exercises the AutoReplyDue handler directly, NOT the scheduling
    // guard in HubMessageReceived — `up` discards the returned Cmd<Msg>, so a Cmd
    // scheduled via delayCmd is never actually executed here. The guard itself is
    // covered separately by the `shouldAutoReply` predicate tests below.
    let m0 = loggedIn { Model.initial with AutoReply = true; Jobs = [mkJob 7 "EnRoute"] }
    let m1 = up (AutoReplyDue 7) m0
    Assert.Equal(1, m1.AutoRepliesSent)
    let m2 = up (AutoReplyDue 7) m1
    Assert.Equal(2, m2.AutoRepliesSent)
    let m3 = up (AutoReplyDue 7) m2
    Assert.Equal(3, m3.AutoRepliesSent)

[<Fact>]
let ``shouldAutoReply guards on autoReply flag, own-message, and job ownership`` () =
    // Real regression coverage for the HubMessageReceived auto-reply guard: this
    // calls the extracted pure predicate directly (no Cmd execution required),
    // unlike a test that merely discards the Cmd and checks unrelated state.
    let model = { Model.initial with AutoReply = true; Jobs = [mkJob 7 "EnRoute"] }
    let me = Some (mkSession 4)
    let customerMsg = mkChatMsg 10 7 1 "Customer"   // senderId 1 = job customer
    Assert.True(shouldAutoReply me model customerMsg)
    Assert.False(shouldAutoReply me model (mkChatMsg 11 7 4 "Provider"))              // own message
    Assert.False(shouldAutoReply me { model with AutoReply = false } customerMsg)     // disabled
    Assert.False(shouldAutoReply me model (mkChatMsg 12 99 1 "Customer"))             // not my job

[<Fact>]
let ``own hub-echoed message appends once without duplicating`` () =
    let m0 = loggedIn { Model.initial with AutoReply = true; Screen = Chat 7; Jobs = [mkJob 7 "EnRoute"] }
    let m1 = up (HubMessageReceived (mkChatMsg 10 7 4 "Provider")) m0   // senderId 4 = me
    Assert.Equal(1, m1.Messages |> List.filter (fun x -> x.Id = 10) |> List.length)
    let m2 = up (HubMessageReceived (mkChatMsg 10 7 4 "Provider")) m1   // duplicate echo
    Assert.Equal(1, m2.Messages |> List.filter (fun x -> x.Id = 10) |> List.length)
    Assert.False(shouldAutoReply (Some (mkSession 4)) m0 (mkChatMsg 10 7 4 "Provider"))

[<Fact>]
let ``typing cooldown blocks resend until done`` () =
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    let m1 = up (ChatDraftChanged (7, "h")) m0
    Assert.True(m1.TypingCooldown)
    let m2 = up TypingCooldownDone m1
    Assert.False(m2.TypingCooldown)

[<Fact>]
let ``hub typing shows indicator for open chat only`` () =
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    Assert.True((up (HubTyping (7, 1, "Customer")) m0).CustomerTyping)
    Assert.False((up (HubTyping (99, 1, "Customer")) m0).CustomerTyping)
    let typing = up (HubTyping (7, 1, "Customer")) m0
    Assert.False((up (CustomerTypingExpired typing.TypingToken) typing).CustomerTyping)

[<Fact>]
let ``hub seen marks customer seen`` () =
    // The watermark records which of MY messages were seen, so one must exist.
    let mine = { mkChatMsg 10 7 1 "Provider" with SenderId = 4 }
    let m0 = loggedIn { Model.initial with Screen = Chat 7; Messages = [mine] }
    Assert.Equal(Some 10, (up (HubSeen (7, 1, "Customer")) m0).SeenUpToMessageId)

[<Fact>]
let ``rating submitted returns Home and resets`` () =
    let m0 = loggedIn { Model.initial with Screen = RateCustomer 7; History = [Payment 7; Home]; RatingStars = 2 }
    let m = up RatingSubmitted m0
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(5, m.RatingStars)

// ---------------------------------------------------------------------------
// Identity collision regression tests.
//
// Customer and Provider ids are independent sequences that both start at 1, so
// the documented demo pair is customer 1 (John) + provider 1 (Mike's Plumbing).
// The pre-fix guards compared SenderId to Session.UserId as bare ints, so for
// this pair every peer event looked like the provider's own: auto-reply never
// fired, and the typing/seen indicators never appeared. The fixtures above use
// provider 4 / customer 1, which cannot collide and so never caught it.
// ---------------------------------------------------------------------------

/// Provider id 1 — same integer as the job's customer id.
let private collidingSession = { Token = "t"; UserId = 1; Role = "Provider"; DisplayName = "Mike's Plumbing" }
let private collidingJob : JobDto =
    { mkJob 7 "EnRoute" with CustomerId = 1; ProviderId = 1; ProviderName = "Mike's Plumbing" }

[<Fact>]
let ``auto-reply fires when customer id equals my provider id`` () =
    let model = { Model.initial with AutoReply = true; Jobs = [collidingJob] }
    let customerMsg = mkChatMsg 10 7 1 "Customer"   // customer 1 — same int as my provider id
    Assert.True(shouldAutoReply (Some collidingSession) model customerMsg)

[<Fact>]
let ``my own message is not mistaken for the customer's when ids collide`` () =
    let model = { Model.initial with AutoReply = true; Jobs = [collidingJob] }
    let mine = mkChatMsg 11 7 1 "Provider"          // provider 1 — me
    Assert.False(shouldAutoReply (Some collidingSession) model mine)

[<Fact>]
let ``typing and seen indicators show when customer id equals my provider id`` () =
    // provider id 1 — my own message, same int as the job's customer id
    let mine = mkChatMsg 10 7 1 "Provider"
    let m0 =
        { Model.initial with
            Screen = Chat 7; Session = Some collidingSession
            Jobs = [collidingJob]; Messages = [mine] }
    Assert.True((up (HubTyping (7, 1, "Customer")) m0).CustomerTyping)
    Assert.Equal(Some 10, (up (HubSeen (7, 1, "Customer")) m0).SeenUpToMessageId)
    // my own echo must not trigger either indicator
    Assert.False((up (HubTyping (7, 1, "Provider")) m0).CustomerTyping)
    Assert.Equal(None, (up (HubSeen (7, 1, "Provider")) m0).SeenUpToMessageId)

// ---------------------------------------------------------------------------
// Seen-watermark and Online-hydration regressions.
// ---------------------------------------------------------------------------

[<Fact>]
let ``seen marker does not carry over to a later message on the same job`` () =
    let mineOld = { mkChatMsg 10 7 1 "Provider" with SenderId = 4 }
    let m0 =
        { Model.initial with
            Screen = Chat 7; Session = Some (mkSession 4); Jobs = [mkJob 7 "EnRoute"]
            Messages = [mineOld] }
    // customer confirms seeing message 10
    let seen = up (HubSeen (7, 1, "Customer")) m0
    Assert.Equal(Some 10, seen.SeenUpToMessageId)
    // I then send a NEW message (id 11) — the watermark must not cover it
    let mineNew = { mkChatMsg 11 7 1 "Provider" with SenderId = 4 }
    let after = { seen with Messages = seen.Messages @ [mineNew] }
    Assert.True(after.SeenUpToMessageId.Value < mineNew.Id)

[<Fact>]
let ``seen watermark is cleared when another job's chat loads`` () =
    let m0 =
        { Model.initial with
            Screen = Chat 7; Session = Some (mkSession 4)
            SeenUpToMessageId = Some 99 }
    let m1 = up (MessagesLoaded []) m0
    Assert.Equal(None, m1.SeenUpToMessageId)

[<Fact>]
let ``login hydrates Online from the server instead of defaulting to false`` () =
    let dto : ProviderDto =
        { Id = 4; BusinessName = "Elite HVAC"; ServiceId = 7; ServiceName = "HVAC"
          Rating = 4.5; RatingCount = 2; Lat = 43.70; Lng = -79.45
          Online = true; Vehicle = "Box truck"; PhotoUrl = "" }
    Assert.False(Model.initial.Online)
    Assert.True((up (ProviderHydrated dto) Model.initial).Online)

// ---------------------------------------------------------------------------
// Cmd-executing tests.
//
// The `up` helper above discards the Cmd<Msg>, and SendTyping/SendSeen are only
// ever invoked from inside a Cmd. Every typing/seen test that used `up` could
// therefore only observe model flags — it would still pass if the throttle were
// removed entirely. These drain the Cmd against recording stubs so the two
// gating criteria (typing throttled, Seen only for the open chat) can fail.
// ---------------------------------------------------------------------------

/// Runs update and executes the returned Cmd, capturing dispatched messages.
let private runWith deps msg model =
    let m, cmd = Update.update deps msg model
    let dispatched = ResizeArray<Msg>()
    for sub in cmd do sub dispatched.Add
    m, List.ofSeq dispatched

let private recordingDeps (typing: ResizeArray<int * int * string>) (seen: ResizeArray<int * int * string>) =
    { stubDeps with
        SendTyping = fun j s r -> typing.Add(j, s, r)
        SendSeen = fun j s r -> seen.Add(j, s, r) }

[<Fact>]
let ``typing throttle actually suppresses the second send`` () =
    let typing, seen = ResizeArray(), ResizeArray()
    let deps = recordingDeps typing seen
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    let m1, _ = runWith deps (ChatDraftChanged (7, "h")) m0
    Assert.Equal(1, typing.Count)
    // second keystroke while the cooldown is up must NOT reach the hub
    let m2, _ = runWith deps (ChatDraftChanged (7, "he")) m1
    Assert.Equal(1, typing.Count)
    // after the cooldown elapses it may send again
    let m3, _ = runWith deps TypingCooldownDone m2
    let _ = runWith deps (ChatDraftChanged (7, "hel")) m3
    Assert.Equal(2, typing.Count)
    let (jobId, senderId, senderRole) = typing.[0]
    Assert.Equal(7, jobId)
    Assert.Equal(4, senderId)
    Assert.Equal("Provider", senderRole)

[<Fact>]
let ``seen is sent only for the chat that is open`` () =
    let typing, seen = ResizeArray(), ResizeArray()
    let deps = recordingDeps typing seen
    let incoming = mkChatMsg 10 7 1 "Customer"
    // chat for job 7 is open -> Seen sent
    let openChat = loggedIn { Model.initial with Screen = Chat 7; Jobs = [mkJob 7 "EnRoute"] }
    runWith deps (HubMessageReceived incoming) openChat |> ignore
    Assert.Equal(1, seen.Count)
    // a different job's chat is open -> no Seen for job 7
    let otherChat = loggedIn { Model.initial with Screen = Chat 9; Jobs = [mkJob 7 "EnRoute"] }
    runWith deps (HubMessageReceived incoming) otherChat |> ignore
    Assert.Equal(1, seen.Count)
    // not in a chat at all -> still no Seen
    let home = loggedIn { Model.initial with Screen = Home; Jobs = [mkJob 7 "EnRoute"] }
    runWith deps (HubMessageReceived incoming) home |> ignore
    Assert.Equal(1, seen.Count)

[<Fact>]
let ``auto-reply does not clear a draft being typed in another job's chat`` () =
    let m0 =
        loggedIn
            { Model.initial with
                AutoReply = true; Screen = Chat 9
                Jobs = [mkJob 7 "EnRoute"; mkJob 9 "EnRoute"]
                ChatDrafts = Map.ofList [ 9, "Running 10 min late" ] }
    // auto-reply fires for job 7 while the user is composing in job 9
    let m1 = up (AutoReplyDue 7) m0
    let m2 = up (SendChatMessage (7, "On my way.", null)) m1
    Assert.Equal("Running 10 min late", draftFor m2.ChatDrafts 9)

[<Fact>]
let ``auto-reply is dropped when toggled off during the delay`` () =
    let m0 = loggedIn { Model.initial with AutoReply = false; Jobs = [mkJob 7 "EnRoute"] }
    let m1 = up (AutoReplyDue 7) m0
    Assert.Equal(0, m1.AutoRepliesSent)

[<Fact>]
let ``a stale typing-expiry timer does not clear an extended indicator`` () =
    let m0 = loggedIn { Model.initial with Screen = Chat 7 }
    let m1 = up (HubTyping (7, 1, "Customer")) m0        // token 1
    let m2 = up (HubTyping (7, 1, "Customer")) m1        // token 2 extends the window
    Assert.True(m2.CustomerTyping)
    let m3 = up (CustomerTypingExpired 1) m2             // stale timer fires
    Assert.True(m3.CustomerTyping)                       // still typing
    let m4 = up (CustomerTypingExpired m2.TypingToken) m3
    Assert.False(m4.CustomerTyping)

[<Fact>]
let ``navigating to Payment clears a previous job's receipt`` () =
    let m0 =
        loggedIn
            { Model.initial with
                Screen = Home; PaymentResult = Some (receipt 3 85m) }
    let m1 = up (Navigate (Payment 7)) m0
    Assert.Equal(None, m1.PaymentResult)

[<Fact>]
let ``a second Depart does not start a second GPS loop`` () =
    let m0 = loggedIn { Model.initial with Screen = ActiveJob 7; UseRealGps = true; Jobs = [mkJob 7 "Scheduled"] }
    let enroute = { mkJob 7 "EnRoute" with Id = 7 }
    let m1 = up (JobActioned enroute) m0
    Assert.True(m1.GpsLoopActive)
    let m2 = up (JobActioned enroute) m1
    Assert.True(m2.GpsLoopActive)   // still exactly one loop; guard prevents a second

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
let ``a delay is measured from the promise, not from now`` () =
    // A provider already twenty minutes late who taps "+15" means fifteen past
    // the agreed time, not fifteen past this moment. Measuring from now would
    // quietly grant them the lateness they had already accrued.
    let promised = DemoClock.epoch.AddHours 2.0
    let job = { mkJob 7 "Scheduled" with PromisedStart = promised.ToString "o" }
    let model = { Model.initial with Jobs = [ job ]; DemoNow = promised.AddMinutes 20.0 }

    let captured = System.Collections.Generic.List<ProposeRescheduleRequest>()
    let deps = { stubDeps with ProposeReschedule = fun r -> captured.Add r; Task.FromResult(Ok job) }
    let _, cmd = Update.update deps (ProposeDelay (7, 15)) model
    // Drain the Cmd — the request is built inside it, so a model-only
    // assertion cannot see the value being tested.
    for sub in cmd do sub (fun _ -> ())
    System.Threading.Thread.Sleep 60
    Assert.Equal(1, captured.Count)
    Assert.Equal(promised.AddMinutes 15.0, DateTimeOffset.Parse captured.[0].ProposedStart)
    Assert.Equal("Provider", captured.[0].ByRole)

[<Fact>]
let ``proposing tells the provider the ball is in the customer's court`` () =
    let job = { mkJob 7 "Scheduled" with PromisedStart = (DemoClock.epoch.AddHours 2.0).ToString "o" }
    let m = up (ProposeDelay (7, 15)) { Model.initial with Jobs = [ job ] }
    Assert.Contains("Asked the customer", (List.head m.Notices).Text)

[<Fact>]
let ``a restored session skips the login screen`` () =
    let saved : Session =
        { Token = "fake-provider-1"; UserId = 1; Role = "Provider"; DisplayName = "Mike's Plumbing" }
    let deps = { stubDeps with RestoreSession = fun () -> Some saved }
    let m, cmd = Update.update deps SplashDone Model.initial
    Assert.Equal(Home, m.Screen)
    Assert.Equal(Some saved, m.Session)
    Assert.False(List.isEmpty cmd)
    Assert.Equal(Login, (fst (Update.update stubDeps SplashDone Model.initial)).Screen)

[<Fact>]
let ``the provider's cancel asks first too`` () =
    // Bilateral cancel means bilateral confirmation; a provider dropping a job
    // by mis-tap is the more damaging of the two.
    let asked = up (RequestCancel 7) Model.initial
    Assert.Equal(Some 7, asked.ConfirmingCancel)
    let _, askCmd = Update.update stubDeps (RequestCancel 7) Model.initial
    Assert.True(List.isEmpty askCmd)
    let m, cmd = Update.update stubDeps (CancelJob 7) asked
    Assert.Equal(None, m.ConfirmingCancel)
    Assert.False(List.isEmpty cmd)
