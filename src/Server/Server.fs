module Server

open Fable.Remoting.Server
open Fable.Remoting.Giraffe
open Saturn
open CliWrap
open CliWrap.Buffered
open Newtonsoft.Json.Linq
open Shared
open System.Threading.Tasks

module Storage =
    let todos = ResizeArray()

    let addTodo (todo: Todo) =
        if Todo.isValid todo.Description then
            todos.Add todo
            Ok()
        else
            Error "Invalid todo"

    do
        addTodo (Todo.create "Create new SAFE project")
        |> ignore

        addTodo (Todo.create "Write your app") |> ignore
        addTodo (Todo.create "Ship it !!!") |> ignore


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

let todosApi =
    { getTodos = fun () -> async { return Storage.todos |> List.ofSeq }
      getLocalPlugins = getLocalPlugins >> Async.AwaitTask
      getSchemaByPlugin = getSchemaByPlugin >> Async.AwaitTask
      addTodo =
        fun todo ->
            async {
                return
                    match Storage.addTodo todo with
                    | Ok () -> todo
                    | Error e -> failwith e
            } }

let webApp =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.withErrorHandler (fun error routeInfo ->
        printfn "%A" error
        Ignore
    )
    |> Remoting.fromValue todosApi
    |> Remoting.buildHttpHandler

let app =
    application {
        use_router webApp
        memory_cache
        use_static "public"
        use_gzip
    }

[<EntryPoint>]
let main _ =
    run app
    0