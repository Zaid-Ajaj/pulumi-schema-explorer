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

let githubClient() =
    let githubToken = Environment.GetEnvironmentVariable "GITHUB_TOKEN"
    if String.IsNullOrWhiteSpace(githubToken) then
        github
    else
        github.Credentials <- Credentials(githubToken)
        github

let rec searchGithub (term: string) =
    task {
        try
            let request = SearchRepositoriesRequest term
            let! searchResults = githubClient().Search.SearchRepo(request)
            let bestResults =
                searchResults.Items
                |> Seq.map (fun repo -> repo.FullName)

            return RateLimited.Response(List.ofSeq bestResults)
        with
            | :? RateLimitExceededException as error ->
                return RateLimited.RateLimitReached
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
        try
            match repo.Split "/" with
            | [| owner; repoName |] ->
                let! releases = githubClient().Repository.Release.GetAll(owner, repoName)
                return
                    List.choose id [ for release in releases -> version release ]
                    |> RateLimited.Response
            | _ ->
                return
                    RateLimited.Response []
        with
        | :? RateLimitExceededException as error ->
               return RateLimited.RateLimitReached
    }

let pulumiCliBinary() : Task<string> = task {
    try
        // try to get the version of pulumi installed on the system
        let! version =
            Cli.Wrap("pulumi")
                .WithArguments("version")
                .WithValidation(CommandResultValidation.ZeroExitCode)
                .ExecuteAsync()

        return "pulumi"
    with
    | _ ->
        // when pulumi is not installed, try to get the version of of the dev build
        // installed on the system using `make install` in the pulumi repo
        let homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        let pulumiPath = System.IO.Path.Combine(homeDir, ".pulumi-dev", "bin", "pulumi")
        if System.IO.File.Exists pulumiPath then
            return pulumiPath
        elif System.IO.File.Exists $"{pulumiPath}.exe" then
            return $"{pulumiPath}.exe"
        else
            return "pulumi"
}

let getLocalPlugins() : Task<PluginReference list> =
    task {
        let! binary = pulumiCliBinary()
        let! command = Cli.Wrap(binary).WithArguments("plugin ls --json").ExecuteBufferedAsync()
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

let schemaFromPulumi(pluginName: string, version: string) = task {
    let packageName = $"{pluginName}@{version}"
    let! binary = pulumiCliBinary()
    let! output =
         Cli.Wrap(binary)
            .WithArguments($"package get-schema {packageName}")
            .WithValidation(CommandResultValidation.None)
            .ExecuteBufferedAsync()

    if output.ExitCode <> 0 then
        return Error output.StandardError
    else
        return Ok (PulumiSchema.Parser.parseSchema output.StandardOutput)
}

let getSchemaByPlugin(plugin: PluginReference) = schemaFromPulumi(plugin.Name, plugin.Version)

let rec getReleaseNotes (req: GetReleaseNotesRequest) =
    task {
        try
            let client = githubClient()
            let! repository = client.Repository.Get(req.Owner, req.Repository)
            let! releases = client.Repository.Release.GetAll(repository.Id)
            return
                releases
                |> Seq.tryFind (fun release -> version release = Some req.Version)
                |> Option.map (fun release -> release.Body)
                |> Option.defaultValue ""
                |> RateLimited.Response
        with
            | :? RateLimitExceededException ->
                return RateLimited.RateLimitReached
    }

let installThirdPartyPlugin (req: InstallThirdPartyPluginRequest) =
    task {
        let! binary = pulumiCliBinary()
        let! command =
            Cli.Wrap(binary)
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
            let client = githubClient()
            let! repository = client.Repository.Get(req.Owner, req.Repository)
            let! releases = client.Repository.Release.GetAll(repository.Id)
            return
                releases
                |> Seq.choose (fun release -> version release)
                |> List.ofSeq
                |> RateLimited.Response
        with
            | :? RateLimitExceededException ->
                return RateLimited.RateLimitReached
    }

let diffSchema (req: DiffSchemaRequest) =
    task {
        let! schemaA = schemaFromPulumi(req.Plugin, req.VersionA)
        let! schemaB = schemaFromPulumi(req.Plugin, req.VersionB)

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

let getPulumiVersion() = task {
    let! binary = pulumiCliBinary()
    let! output = Cli.Wrap(binary).WithArguments("version").ExecuteBufferedAsync()
    return output.StandardOutput
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
    getPulumiVersion = getPulumiVersion >> Async.AwaitTask
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