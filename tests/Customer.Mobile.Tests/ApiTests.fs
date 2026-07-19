module FixItHere.Customer.Tests.ApiTests

open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open Xunit
open FixItHere.Customer

type StubHandler(status: HttpStatusCode, json: string) =
    inherit HttpMessageHandler()
    override _.SendAsync(_req, _ct) =
        let resp = new HttpResponseMessage(status)
        resp.Content <- new StringContent(json, Encoding.UTF8, "application/json")
        Task.FromResult resp

let stubPhoto () = Task.FromResult(Error "no photo in tests")
let stubGps () = Task.FromResult(Ok (43.65, -79.38))

let depsWith status json =
    Api.createDepsWith stubPhoto stubGps (fun _ _ _ -> ()) (fun _ _ _ -> ()) (new StubHandler(status, json)) "http://stub"

[<Fact>]
let ``success envelope maps to Ok`` () =
    let deps = depsWith HttpStatusCode.OK
                 """{"success":true,"data":[{"id":1,"name":"Plumbing"}],"error":null}"""
    match (deps.GetServices ()).Result with
    | Ok [s] -> Assert.Equal("Plumbing", s.Name)
    | other -> failwithf "unexpected: %A" other

[<Fact>]
let ``failure envelope maps to Error with message`` () =
    let deps = depsWith HttpStatusCode.Conflict
                 """{"success":false,"data":null,"error":"Invalid transition"}"""
    match (deps.CancelJob 5).Result with
    | Error e -> Assert.Contains("Invalid transition", e)
    | Ok _ -> failwith "expected Error"

[<Fact>]
let ``non-json response maps to Error not exception`` () =
    let deps = depsWith HttpStatusCode.InternalServerError "<html>boom</html>"
    match (deps.GetServices ()).Result with
    | Error _ -> ()
    | Ok _ -> failwith "expected Error"
