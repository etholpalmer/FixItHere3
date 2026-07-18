module FixItHere.Customer.Tests.UpdateTests

open System.Threading.Tasks
open Xunit
open FixItHere.Customer
open FixItHere.Shared.Dtos

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

let stubDeps : ApiDeps =
    { Login = fun _ -> Task.FromResult(Ok { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" })
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
      PickPhoto = fun () -> Task.FromResult(Ok "ZmFrZQ==")
      GetGpsLocation = fun () -> Task.FromResult(Ok (43.65, -79.38)) }

let mkJob id state : JobDto =
    { Id = id; CustomerId = 1; CustomerName = "John"; ProviderId = 2; ProviderName = "Mike's Plumbing"
      ServiceId = 3; ServiceName = "Plumbing"; State = state; Price = 85m
      ScheduledFor = "Now"; Lat = 43.65; Lng = -79.38; Address = "1 Demo St" }

let up msg model = Update.update stubDeps msg model |> fst

[<Fact>]
let ``splash advances to Login`` () =
    Assert.Equal(Login, (up SplashDone { Model.initial with Screen = Splash }).Screen)

[<Fact>]
let ``login stores session and lands on Home with empty history`` () =
    let resp = { Token = "fake-customer-1"; UserId = 1; Role = "Customer"; DisplayName = "John" }
    let m = up (LoggedIn resp) { Model.initial with Screen = Login }
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(Some { Token = "fake-customer-1"; UserId = 1; DisplayName = "John" }, m.Session)

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

let mkChatMsg id jobId : MessageDto =
    { Id = id; JobId = jobId; SenderId = 2; SenderName = "Mike's Plumbing"
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
let ``hub notification sets toast`` () =
    Assert.Equal(Some "Provider Accepted", (up (HubNotification "Provider Accepted") Model.initial).Toast)

[<Fact>]
let ``payment result stored`` () =
    let r : PaymentResult = { JobId = 7; Amount = 85m; Status = "Transferred" }
    Assert.Equal(Some r, (up (PaymentSimulated r) Model.initial).PaymentResult)

[<Fact>]
let ``rating submitted resets to Home and clears payment`` () =
    let m0 = { Model.initial with Screen = Rating 7; History = [Payment 7; Tracking 7; Home]
                                  PaymentResult = Some { JobId = 7; Amount = 85m; Status = "Transferred" } }
    let m = up RatingSubmitted m0
    Assert.Equal(Home, m.Screen)
    Assert.Empty(m.History)
    Assert.Equal(None, m.PaymentResult)
    Assert.True(m.Toast.IsSome)

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
        { Id = id; JobId = 7; SenderId = 1; SenderName = "John"
          Text = ""; PhotoBase64 = "ZmFrZQ=="; SentAt = ""; Seen = false }
    let m0 =
        { Model.initial with
            Session = Some { Token = "t"; UserId = 1; DisplayName = "John" }
            Screen = Chat 7
            Messages = [ for i in 1 .. 5 -> photoMsg i ] }
    let m = up (PickAndSendPhoto 7) m0
    Assert.True(m.Error.IsSome)

[<Fact>]
let ``chat draft tracks input and clears on send`` () =
    let session = Some { Token = "t"; UserId = 1; DisplayName = "John" }
    let m1 = up (ChatDraftChanged "hello") { Model.initial with Session = session }
    Assert.Equal("hello", m1.ChatDraft)
    Assert.Equal("", (up (SendChatMessage (7, "hello", null)) m1).ChatDraft)

[<Fact>]
let ``stars and comment update`` () =
    let m = up (StarsChanged 3) Model.initial
    Assert.Equal(3, m.RatingStars)
    Assert.Equal("great", (up (RatingCommentChanged "great") m).RatingComment)
