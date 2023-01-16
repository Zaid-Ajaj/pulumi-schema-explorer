namespace Shared

open PulumiSchema.Types

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type PluginReference = { Name: string; Version: string }

type GetReleaseNotesRequest = { 
    Owner: string
    Repository: string
    Version: string 
}

type GetSchemaVersionsRequest = { 
    Owner: string
    Repository: string
}

type InstallThirdPartyPluginRequest = {
    Owner: string
    PluginName: string
    Version: string
}

type DiffSchemaRequest = {
    Plugin: string 
    VersionA: string
    VersionB: string
}

[<RequireQualifiedAccess>]
type ResourceChange = 
    | AddedProperty of string * Property
    | RemovedProperty of string * Property

type ChangedResource = {
    Resource: Resource
    Inputs: ResourceChange list
    Outputs: ResourceChange list
}

type DiffResult = {
    AddedResources: Resource list
    RemovedResources: Resource list
    AddedFunctions: Function list
    RemovedFunctions: Function list
    ChangedResources: ChangedResource list
}

type ISchemaExplorerApi = { 
    getLocalPlugins : unit -> Async<PluginReference list>
    getSchemaByPlugin: PluginReference -> Async<Result<Schema, string>> 
    searchGithub: string -> Async<string list>
    findGithubReleases : string -> Async<string list> 
    getReleaseNotes : GetReleaseNotesRequest -> Async<string>
    installThirdPartyPlugin : InstallThirdPartyPluginRequest -> Async<Result<unit, string>>
    getSchemaVersionsFromGithub : GetSchemaVersionsRequest -> Async<string list>
    diffSchema : DiffSchemaRequest -> Async<Result<DiffResult, string>>
}