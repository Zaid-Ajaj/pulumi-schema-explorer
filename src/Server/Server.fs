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

let getReleaseNotes (req: GetReleaseNotesRequest) = 
    task {
        let! repository = github.Repository.Get(req.Owner, req.Repository)
        let! releases = github.Repository.Release.GetAll(repository.Id)
        return
            releases
            |> Seq.tryFind (fun release -> version release = Some req.Version)
            |> Option.map (fun release -> release.Body)
            |> Option.defaultValue ""
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

let schemaExplorerApi = { 
    getLocalPlugins = getLocalPlugins >> Async.AwaitTask
    getSchemaByPlugin = getSchemaByPlugin >> Async.AwaitTask
    searchGithub = searchGithub >> Async.AwaitTask
    findGithubReleases = findGithubReleases >> Async.AwaitTask
    getReleaseNotes = getReleaseNotes >> Async.AwaitTask
    installThirdPartyPlugin = installThirdPartyPlugin >> Async.AwaitTask
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

let app = application {
    use_router webApp
    memory_cache
    use_static "public"
    use_gzip
}

[<EntryPoint>]
let main _ =
    run app
    0