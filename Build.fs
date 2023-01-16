open Fake.Core
open Fake.IO
open Farmer
open Helpers
open System.IO
open System.Linq
open System.IO.Compression

initializeContext()

let sharedPath = Path.getFullName "src/Shared"
let serverPath = Path.getFullName "src/Server"
let clientPath = Path.getFullName "src/Client"
let deployPath = Path.getFullName "deploy"
let sharedTestsPath = Path.getFullName "tests/Shared"
let serverTestsPath = Path.getFullName "tests/Server"
let clientTestsPath = Path.getFullName "tests/Client"

Target.create "Clean" (fun _ ->
    Shell.cleanDir deployPath
    run dotnet "fable clean --yes" clientPath // Delete *.fs.js files created by Fable
)

Target.create "InstallClient" (fun _ -> run npm "install" ".")

Target.create "Bundle" (fun _ ->
    run dotnet "fable -o output -s --run npm run build" clientPath
    run dotnet $"pack -c Release -o \"{deployPath}\"" serverPath
)

Target.create "LocalNugetBundle" (fun _ -> 
    let outputPath = deployPath
    let nugetPackage = Directory.EnumerateFiles(outputPath, "PulumiSchemaExplorer.*.nupkg", SearchOption.AllDirectories).First()
    printfn "Installing %s locally" nugetPackage
    let nugetParent = DirectoryInfo(nugetPackage).Parent.FullName
    let nugetFileName = Path.GetFileNameWithoutExtension(nugetPackage)
    // Unzip the nuget
    ZipFile.ExtractToDirectory(nugetPackage, Path.Combine(nugetParent, nugetFileName))
    // delete the initial nuget package
    File.Delete nugetPackage
    let serverDll = Directory.EnumerateFiles(outputPath, "PulumiSchemaExplorer.dll", SearchOption.AllDirectories).First()
    let serverDllParent = DirectoryInfo(serverDll).Parent.FullName
    // copy web assets into the server dll parent
    Directory.ensure (Path.Combine(serverDllParent, "public"))
    Shell.copyDir (Path.Combine(serverDllParent, "public")) (Path.Combine(deployPath, "public"))  (fun _ -> true)
    // re-create the nuget package
    ZipFile.CreateFromDirectory(Path.Combine(nugetParent, nugetFileName), nugetPackage)
    // delete intermediate directory
    Shell.deleteDir(Path.Combine(nugetParent, nugetFileName))
)

Target.create "LocalInstall" (fun _ -> 
    if Shell.Exec("dotnet", sprintf "tool install -g PulumiSchemaExplorer --add-source %s" deployPath) <> 0
    then failwith "Local install failed"
)

Target.create "PublishNuget" (fun _ ->
    let nugetKey =
        match Environment.environVarOrNone "NUGET_KEY" with
        | Some nugetKey -> nugetKey
        | None -> failwith "NUGET_KEY environment variable not set"

    let nugetPackage = Directory.EnumerateFiles(deployPath, "PulumiSchemaExplorer.*.nupkg", SearchOption.AllDirectories).First()

    if Shell.Exec("dotnet", sprintf "nuget push %s -k %s -s https://api.nuget.org/v3/index.json" nugetPackage nugetKey) <> 0
    then failwith "Nuget publish failed"
)

Target.create "LocalUninstall" (fun _ -> 
    if Shell.Exec("dotnet", "tool uninstall -g PulumiSchemaExplorer") <> 0
    then failwith "Local install failed"
)

Target.create "Run" (fun _ ->
    run dotnet "build" sharedPath
    [ "server", dotnet "watch run" serverPath
      "client", dotnet "fable watch -o output -s --run npm run start" clientPath ]
    |> runParallel
)

Target.create "RunTests" (fun _ ->
    run dotnet "build" sharedTestsPath
    [ "server", dotnet "watch run" serverTestsPath
      "client", dotnet "fable watch -o output -s --run npm run test:live" clientTestsPath ]
    |> runParallel
)

Target.create "Format" (fun _ ->
    run dotnet "fantomas . -r" "src"
)

open Fake.Core.TargetOperators

let dependencies = [
    "Clean"
        ==> "InstallClient"
        ==> "Bundle"
        ==> "LocalNugetBundle"
        ==> "LocalInstall"
    
    "Clean"
        ==> "InstallClient"
        ==> "Bundle"
        ==> "LocalNugetBundle"
        ==> "PublishNuget" 

    "Clean"
        ==> "InstallClient"
        ==> "Run"

    "InstallClient"
        ==> "RunTests"
]

[<EntryPoint>]
let main args = runOrDefault args