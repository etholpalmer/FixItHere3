module FixItHere.Provider.Api

open System
open System.Net.Http
open System.Threading.Tasks
open FixItHere.ClientShared
open FixItHere.Shared.Dtos
open FixItHere.Provider

let createDepsWith
    (pickPhoto: unit -> Task<Result<string, string>>)
    (gpsLocation: unit -> Task<Result<float * float, string>>)
    (sendTyping: int -> int -> unit)
    (sendSeen: int -> int -> unit)
    (handler: HttpMessageHandler)
    (baseUrl: string) : ProviderApiDeps =
    let http = new HttpClient(handler, BaseAddress = Uri(baseUrl))
    let transition (path: string) (jobId: int) : Task<Result<JobDto, string>> =
        Http.putEnv http (sprintf "/jobs/%d/%s" jobId path)
    { Login = fun name -> Http.postEnv http "/login" { Role = "Provider"; Name = name }
      SetOnline = fun id online ->
          Http.putBodyEnv http (sprintf "/providers/%d/online" id) {| online = online |}
      GetMyJobs = fun providerId -> Http.getEnv http (sprintf "/jobs?providerId=%d" providerId)
      Accept = transition "accept"
      Enroute = transition "enroute"
      Arrive = transition "arrive"
      Start = transition "start"
      Complete = transition "complete"
      UpdateLocation = fun id lat lng ->
          Http.putBodyEnv http "/location" { ProviderId = id; Lat = lat; Lng = lng }
      GetMessages = fun jobId -> Http.getEnv http (sprintf "/messages?jobId=%d" jobId)
      SendMessage = fun req -> Http.postEnv http "/messages" req
      SimulatePayment = fun jobId -> Http.postEnv http "/payment/simulate" { JobId = jobId }
      SubmitRating = fun req -> Http.postEnv http "/ratings" req
      StartDemo = fun customerId providerId ->
          Http.postEnv http "/dev/demo/start" {| customerId = customerId; providerId = providerId |}
      PickPhoto = pickPhoto
      GetGpsLocation = gpsLocation
      SendTyping = sendTyping
      SendSeen = sendSeen }
