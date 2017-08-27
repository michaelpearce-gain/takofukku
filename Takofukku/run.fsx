#if INTERACTIVE

open System

#I @"C:/users/stopt/appdata/roaming/npm/node_modules/azure-functions-core-tools/bin"
#r "System.Web.Http.dll"
#r "System.Net.Http.Formatting.dll"
#r "Microsoft.Azure.WebJobs.Logging.dll"
#r "Microsoft.Extensions.Logging.dll"
#r "Microsoft.Azure.WebJobs.Host.dll"

open Microsoft.Azure.WebJobs
open Microsoft.Azure.WebJobs.Host
open Microsoft.Azure.WebJobs.Logging

#endif


#r "System.Net.Http"
#r "System.Net"
#r "Newtonsoft.Json"
#r "bin/FSharp.data.dll"
#r "bin/FSharp.data.DesignTime.dll"
#r "bin/Octopus.Client.dll"
#r "bin/SharpYaml.dll"
#r "bin/FSharp.Configuration.dll"
#r "System.Configuration"

open SharpYaml
open FSharp.Data 
open FSharp.Data.JsonExtensions
open Octopus.Client
open System.Configuration
open System.Net
open System.Net.Http
open System.Net.Http.Headers
open System.IO
open Newtonsoft.Json
open FSharp.Core
open FSharp.Configuration


[<Literal>]
let ModelPath =  __SOURCE_DIRECTORY__ + "/models/pushevent.json"
type PushEvent = JsonProvider<ModelPath>

[<Literal>]
let TakoFilePath = __SOURCE_DIRECTORY__ + "/models/takofile.yml"
type TakoFile = YamlConfig<FilePath = TakoFilePath>

let GetTakoFile(repo: String, token: String, log: TraceWriter) = 
    let targetfile = "https://raw.githubusercontent.com/"+repo+"/master/takofile"

    log.Info(sprintf "Requesting takofile from " + targetfile)

    let header = 
        match token with
        | "" -> 
            log.Info(sprintf "Takofukku found an empty token") |> ignore
            ["X-Tako-Client", "Takofukku/1.0"] 
        | _ ->   
            log.Info(sprintf "Takofukku found a non-empty token") |> ignore
            ["Authorization", "token " + token; "X-Tako-Client", "Takofukku/1.0"]  

    let takofile = Http.RequestString(   
                        targetfile,
                        headers = header
                        ) 
    log.Info(sprintf "Takofile retrieved from github") |> ignore
    takofile // a string containing YAML

let Run(req: System.Net.Http.HttpRequestMessage, log: TraceWriter) =
    async {
        log.Info(sprintf 
            "Takofukku started")        

        let qs = 
            HttpRequestMessageExtensions.GetQueryNameValuePairs(req)
        // Set name to query string
        let octopusAPIKey =
            qs |> Seq.tryFind (fun q -> q.Key = "apikey") 

        let gittoken =
            qs |> Seq.tryFind (fun q -> q.Key = "patoken")

        // make our octopus key exception-safe
        let ok =
            match octopusAPIKey with
            | None -> 
                log.Info(sprintf "I don't have an octopus API Key") |> ignore 
                ""
            | Some x ->
                log.Info(sprintf "I have an octopus API key " + x.Value) |> ignore
                x.Value
        
        // make our token exception-safe
        let gt = 
            match gittoken with
            | None -> 
                log.Info(sprintf "I don't have a git token") |> ignore
                ""
            | Some x ->
                log.Info(sprintf "I have a git token " + x.Value) |> ignore
                x.Value

        log.Info(sprintf "Reading async from post body")
        let! data = req.Content.ReadAsStringAsync() |> Async.AwaitTask
        log.Info(sprintf "Post body read") 

        if not (String.IsNullOrEmpty(data)) then
                log.Info(sprintf "We have a post body : " + data)
                let EventData = PushEvent.Parse(data)

                // split out the ref 
                let targetbranch = 
                    let refsplit = EventData.Ref.Split [|'/'|]
                    refsplit.[2]



                log.Info(sprintf 
                    "We have parsed our post body. Push event arrived from repo" + 
                    EventData.Repository.FullName + 
                    "on branch " +
                    EventData.Ref)  // we need to split that ref
                let tako = GetTakoFile(EventData.Repository.FullName, gt, log) 
                // make that string into an object using the YAML type provider
                let tk = TakoFile()
                tk.LoadText(tako)

                let srv = tk.Server




                let branchmapping = 
                    tk.Mappings |> Seq.tryFind (fun q -> q.Branch = targetbranch)
                
                // find the branch mapping
                let targetenv = branchmapping.Value.Environment
                

                log.Info(sprintf "We've pushed the branch "+EventData.Ref+" and found env" + 
                            targetenv)

                printfn ""  
                return req.CreateResponse(HttpStatusCode.OK, """{"result" : "ok"}}""")
        else
            // no data posted. 
            log.Info(sprintf
                "No data was posted. Invalid request") // print usage at this point

            let usagebody = File.ReadAllText(__SOURCE_DIRECTORY__ + "/usage.txt")
            return req.CreateResponse(HttpStatusCode.OK, usagebody) 

    } |> Async.RunSynchronously  