module FixItHere.Provider.Tests.ApiTests

open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Xunit
open FixItHere.Provider

type StubHandler(status: HttpStatusCode, json: string) =
    inherit HttpMessageHandler()
    override _.SendAsync(_req, _ct) =
        let resp = new HttpResponseMessage(status)
        resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
        Task.FromResult resp

let depsWith status json =
    Api.createDepsWith
        (fun () -> Task.FromResult(Error "no photo"))
        (fun () -> Task.FromResult(Ok (43.70, -79.45)))
        (fun _ _ _ -> ()) (fun _ _ _ -> ())
        (new StubHandler(status, json)) "http://stub"

[<Fact>]
let ``accept maps success envelope to Ok JobDto`` () =
    let deps = depsWith HttpStatusCode.OK
                 """{"success":true,"data":{"id":7,"customerId":1,"customerName":"John","providerId":4,"providerName":"Elite HVAC","serviceId":7,"serviceName":"HVAC","state":"Scheduled","price":85,"scheduledFor":"Now","lat":43.7,"lng":-79.4,"address":"1 Demo St"},"error":null}"""
    match (deps.Accept 7).Result with
    | Ok j -> Assert.Equal(7, j.Id)
    | Error e -> failwith e

[<Fact>]
let ``invalid transition envelope maps to Error`` () =
    let deps = depsWith HttpStatusCode.Conflict
                 """{"success":false,"data":null,"error":"Invalid transition"}"""
    match (deps.Complete 7).Result with
    | Error e -> Assert.Contains("Invalid transition", e)
    | Ok _ -> failwith "expected Error"
