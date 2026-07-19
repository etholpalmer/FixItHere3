module FixItHere.Provider.Tests.UpdateTests

open System.Threading.Tasks
open Xunit
open FixItHere.Shared.Dtos
open FixItHere.Provider

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 4; ProviderName = "Elite HVAC"
      ServiceId = 7; ServiceName = "HVAC"; State = state; Price = 85m
      ScheduledFor = "Now"; Lat = 43.70; Lng = -79.40; Address = "1 Demo St" }

[<Fact>]
let ``nav push and back mirror customer app`` () =
    let m = Nav.push { Model.initial with Screen = Home } DevSettings
    Assert.Equal(DevSettings, m.Screen)
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
let ``slider position interpolates and clamps`` () =
    Assert.Equal((5.0, 5.0), Slider.position (0.0, 0.0) (10.0, 10.0) 0.5)
    Assert.Equal((10.0, 10.0), Slider.position (0.0, 0.0) (10.0, 10.0) 1.7)
    Assert.Equal((0.0, 0.0), Slider.position (0.0, 0.0) (10.0, 10.0) -0.3)

let stubDeps : ProviderApiDeps =
    { Login = fun _ -> Task.FromResult(Ok { Token = "fake-provider-4"; UserId = 4; Role = "Provider"; DisplayName = "Elite HVAC" })
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
      StartDemo = fun _ _ -> Task.FromResult(Error "unused")
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.70, -79.45))
      SendTyping = fun _ _ _ -> ()
      SendSeen = fun _ _ _ -> () }

let up msg model = Update.update stubDeps msg model |> fst

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
                Screen = Home; PaymentResult = Some { JobId = 3; Amount = 85m; Status = "Transferred" } }
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
