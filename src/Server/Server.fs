module Server

open System
open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open CliWrap
open CliWrap.Buffered
open Newtonsoft.Json.Linq
open Shared
open System.Threading.Tasks
open Octokit

let github = new GitHubClient(ProductHeaderValue "PulumiBot")

let rec searchGithub (term: string) = 
    task {
        try 
            let request = SearchRepositoriesRequest term
            let! searchResults = github.Search.SearchRepo(request)
            let bestResults = 
                searchResults.Items
                |> Seq.map (fun repo -> repo.FullName)

            return List.ofSeq bestResults
        with 
            | :? RateLimitExceededException as error -> 
                do! Task.Delay 5000
                return! searchGithub term
    }

let version (release: Release) = 
    if not (String.IsNullOrWhiteSpace(release.Name)) then
        Some (release.Name.Substring(1, release.Name.Length - 1))
    elif not (String.IsNullOrWhiteSpace(release.TagName)) then 
        Some (release.TagName.Substring(1, release.TagName.Length - 1))
    else 
        None

let findGithubReleases (repo: string) = 
    task {
        match repo.Split "/" with 
        | [| owner; repoName |] -> 
            let! releases = github.Repository.Release.GetAll(owner, repoName)
            return List.choose id [ for release in releases -> version release ]
        | _ -> 
            return []
    }

let getLocalPlugins() : Task<PluginReference list> =
    task {
        let! command = Cli.Wrap("pulumi").WithArguments("plugin ls --json").ExecuteBufferedAsync()
        if command.ExitCode = 0 then 
            let pluginsJson = JArray.Parse(command.StandardOutput)
            return 
                pluginsJson
                |> Seq.cast<JObject>
                |> Seq.filter (fun plugin -> plugin["kind"].ToObject<string>() = "resource")
                |> Seq.map (fun plugin -> 
                    let name = plugin["name"].ToObject<string>()
                    let version = plugin["version"].ToObject<string>()
                    { Name = name; Version = version })
                |> List.ofSeq
        else
            return []
    }

let getSchemaByPlugin(plugin: PluginReference) =
    task {
        return PulumiSchema.SchemaLoader.FromPulumi(plugin.Name, plugin.Version)
    }

let rec getReleaseNotes (req: GetReleaseNotesRequest) = 
    task {
        try
            let! repository = github.Repository.Get(req.Owner, req.Repository)
            let! releases = github.Repository.Release.GetAll(repository.Id)
            return
                releases
                |> Seq.tryFind (fun release -> version release = Some req.Version)
                |> Option.map (fun release -> release.Body)
                |> Option.defaultValue ""
        with
            | :? RateLimitExceededException -> 
                do! Task.Delay 5000
                return! getReleaseNotes req
    }

let installThirdPartyPlugin (req: InstallThirdPartyPluginRequest) = 
    task {
        let! command = 
            Cli.Wrap("pulumi")
               .WithArguments($"plugin install resource {req.PluginName} {req.Version} --server github://api.github.com/{req.Owner}")
               .WithValidation(CommandResultValidation.None)
               .ExecuteBufferedAsync()

        if command.ExitCode = 0 then
            return Ok()
        else    
            return Error(command.StandardError)
    }

let rec getSchemaVersionsFromGithub (req: GetSchemaVersionsRequest) = 
    task {
        try 
            let! repository = github.Repository.Get(req.Owner, req.Repository)
            let! releases = github.Repository.Release.GetAll(repository.Id)
            return
                releases
                |> Seq.choose (fun release -> version release)
                |> List.ofSeq
        with
            | :? RateLimitExceededException -> 
                do! Task.Delay 5000
                return! getSchemaVersionsFromGithub req
    }

let diffSchema (req: DiffSchemaRequest) = 
    task {
        let schemaA = PulumiSchema.SchemaLoader.FromPulumi(req.Plugin, req.VersionA)
        let schemaB = PulumiSchema.SchemaLoader.FromPulumi(req.Plugin, req.VersionB)

        let emptyDiff = {
            AddedResources = [ ]
            RemovedResources = [ ]
            AddedFunctions = [ ]
            RemovedFunctions = [ ]
            ChangedResources = [ ]
        }

        match schemaA, schemaB with
        | Error error, _ -> return Error error
        | _, Error error -> return Error error
        | Ok schemaA, Ok schemaB -> 
            let addedResources = [
                for resource in schemaB.resources do
                    if not (Map.containsKey resource.Key schemaA.resources) then
                        resource.Value
            ]

            let removedResources = [
                for resource in schemaA.resources do
                    if not (Map.containsKey resource.Key schemaB.resources) then
                        resource.Value
            ]

            let changedResources = [
                for resource in schemaA.resources do
                    let resourceA = resource.Value
                    if Map.containsKey resourceA.token schemaB.resources then
                        let resourceB = schemaB.resources.[resourceA.token]
                        let outputsChanges = [ 
                            for property in resourceB.properties do
                                let propertyName = property.Key
                                let propertyB = property.Value
                                if not (Map.containsKey property.Key resourceA.properties) then
                                    ResourceChange.AddedProperty(propertyName, propertyB)
                                else 
                                    let propertyA = resourceA.properties.[property.Key]
                                    if propertyA.deprecationMessage.IsNone && propertyB.deprecationMessage.IsSome then
                                        ResourceChange.MarkedDeprecated(propertyName, propertyB)

                            for property in resourceA.properties do
                                let propertyName = property.Key
                                let propertyA = property.Value
                                if not (Map.containsKey property.Key resourceB.properties) then
                                    ResourceChange.RemovedProperty(propertyName, propertyA)
                        ]

                        let inputsChanges = [
                            for property in resourceB.inputProperties do
                                let propertyName = property.Key
                                let propertyB = property.Value
                                if not (Map.containsKey property.Key resourceA.inputProperties) then
                                    ResourceChange.AddedProperty(propertyName, propertyB)
                                else 
                                    let propertyA = resourceA.inputProperties.[property.Key]
                                    if propertyA.deprecationMessage.IsNone && propertyB.deprecationMessage.IsSome then
                                        ResourceChange.MarkedDeprecated(propertyName, propertyB)

                            for property in resourceA.inputProperties do
                                let propertyName = property.Key
                                let propertyA = property.Value
                                if not (Map.containsKey property.Key resourceB.inputProperties) then
                                    ResourceChange.RemovedProperty(propertyName, propertyA)
                        ]
    
                        if inputsChanges.Length > 0 || outputsChanges.Length > 0 then 
                            yield { Resource = resourceA; Inputs = inputsChanges; Outputs = outputsChanges }
            ]

            return Ok { 
                emptyDiff with 
                    AddedResources = addedResources
                    RemovedResources = removedResources
                    ChangedResources = changedResources }
    }

let schemaExplorerApi = { 
    getLocalPlugins = getLocalPlugins >> Async.AwaitTask
    getSchemaByPlugin = getSchemaByPlugin >> Async.AwaitTask
    searchGithub = searchGithub >> Async.AwaitTask
    findGithubReleases = findGithubReleases >> Async.AwaitTask
    getReleaseNotes = getReleaseNotes >> Async.AwaitTask
    installThirdPartyPlugin = installThirdPartyPlugin >> Async.AwaitTask
    getSchemaVersionsFromGithub = getSchemaVersionsFromGithub >> Async.AwaitTask
    diffSchema = diffSchema >> Async.AwaitTask
}

let pulumiSchemaDocs = Remoting.documentation "Pulumi Schema Explorer" [ ]

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withErrorHandler (fun error routeInfo ->
        printfn "%A" error
        Ignore
    )
    |> Remoting.fromValue schemaExplorerApi
    |> Remoting.withDocs "/api/docs" pulumiSchemaDocs
    |> Remoting.buildHttpHandler

let getExecutingAssembly() =
    let assembly = System.Reflection.Assembly.GetExecutingAssembly()
    System.IO.DirectoryInfo(assembly.Location).Parent.FullName

let app = application {
    use_router webApp
    memory_cache
    use_static (System.IO.Path.Combine(getExecutingAssembly(), "public"))
    use_gzip
}

[<EntryPoint>]
let main _ =
    run app
    0