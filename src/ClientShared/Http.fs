module FixItHere.ClientShared.Http

open System.Net.Http
open System.Net.Http.Json
open System.Text.Json
open System.Threading.Tasks
open FixItHere.Shared.Dtos

let private jsonOpts = JsonSerializerOptions(PropertyNameCaseInsensitive = true)

let private readEnv<'t> (resp: HttpResponseMessage) : Task<Result<'t, string>> =
    task {
        try
            let! env = resp.Content.ReadFromJsonAsync<Envelope<'t>>(jsonOpts)
            if env.Success then return Ok env.Data
            else return Error (if isNull env.Error then "Request failed" else env.Error)
        with ex -> return Error ex.Message
    }

let getEnv<'t> (http: HttpClient) (path: string) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.GetAsync(path: string)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let postEnv<'req, 't> (http: HttpClient) (path: string) (body: 'req) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PostAsJsonAsync(path, body, jsonOpts)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let putEnv<'t> (http: HttpClient) (path: string) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PutAsync(path, null)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }

let putBodyEnv<'req, 't> (http: HttpClient) (path: string) (body: 'req) : Task<Result<'t, string>> =
    task {
        try
            let! resp = http.PutAsJsonAsync(path, body, jsonOpts)
            return! readEnv<'t> resp
        with ex -> return Error ex.Message
    }
