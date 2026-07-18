module FixItHere.Customer.Api

open System
open System.Net.Http
open System.Net.Http.Json
open System.Text.Json
open System.Threading.Tasks
open FixItHere.Shared.Dtos
open FixItHere.Customer

let private jsonOpts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

let private readEnv<'t> (resp: HttpResponseMessage) : Task<Result<'t, string>> =
    task {
        try
            let! env = resp.Content.ReadFromJsonAsync<Envelope<'t>>(jsonOpts)
            if env.Success then return Ok env.Data
            else return Error (if isNull env.Error then "Request failed" else env.Error)
        with ex -> return Error ex.Message
    }

let private getEnv<'t> (http: HttpClient) (path: string) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.GetAsync(path: string)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let private postEnv<'req, 't> (http: HttpClient) (path: string) (body: 'req) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PostAsJsonAsync(path, body, jsonOpts)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let private putEnv<'t> (http: HttpClient) (path: string) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PutAsync(path, null)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let createDepsWith
    (pickPhoto: unit -> Task<Result<string, string>>)
    (gpsLocation: unit -> Task<Result<float * float, string>>)
    (handler: HttpMessageHandler)
    (baseUrl: string) : ApiDeps =
    let http = new HttpClient(handler, BaseAddress = Uri(baseUrl))
    { Login = fun name -> postEnv http "/login" { Role = "Customer"; Name = name }
      GetServices = fun () -> getEnv http "/services"
      GetProviders = fun serviceId lat lng ->
          getEnv http (sprintf "/providers?serviceId=%d&lat=%f&lng=%f" serviceId lat lng)
      GetRatings = fun providerId -> getEnv http (sprintf "/ratings?providerId=%d" providerId)
      GetJobs = fun customerId -> getEnv http (sprintf "/jobs?customerId=%d" customerId)
      CreateJob = fun req -> postEnv http "/jobs" req
      CancelJob = fun jobId -> putEnv http (sprintf "/jobs/%d/cancel" jobId)
      GetMessages = fun jobId -> getEnv http (sprintf "/messages?jobId=%d" jobId)
      SendMessage = fun req -> postEnv http "/messages" req
      SimulatePayment = fun jobId -> postEnv http "/payment/simulate" { JobId = jobId }
      SubmitRating = fun req -> postEnv http "/ratings" req
      PickPhoto = pickPhoto
      GetGpsLocation = gpsLocation }
