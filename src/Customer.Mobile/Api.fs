module FixItHere.Customer.Api

open System
open System.Net.Http
open System.Threading.Tasks
open FixItHere.Shared.Dtos
open FixItHere.Customer
open FixItHere.ClientShared

let createDepsWith
    (pickPhoto: unit -> Task<Result<string, string>>)
    (gpsLocation: unit -> Task<Result<float * float, string>>)
    (sendTyping: int -> int -> unit)
    (sendSeen: int -> int -> unit)
    (handler: HttpMessageHandler)
    (baseUrl: string) : ApiDeps =
    let http = new HttpClient(handler, BaseAddress = Uri(baseUrl))
    { Login = fun name -> Http.postEnv http "/login" { Role = "Customer"; Name = name }
      GetServices = fun () -> Http.getEnv http "/services"
      GetProviders = fun serviceId lat lng ->
          Http.getEnv http (sprintf "/providers?serviceId=%d&lat=%f&lng=%f" serviceId lat lng)
      GetRatings = fun providerId -> Http.getEnv http (sprintf "/ratings?providerId=%d" providerId)
      GetJobs = fun customerId -> Http.getEnv http (sprintf "/jobs?customerId=%d" customerId)
      CreateJob = fun req -> Http.postEnv http "/jobs" req
      CancelJob = fun jobId -> Http.putEnv http (sprintf "/jobs/%d/cancel" jobId)
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
