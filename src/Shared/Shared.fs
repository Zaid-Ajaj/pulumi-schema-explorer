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
    | MarkedDeprecated of string * Property

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

[<RequireQualifiedAccess>]
type RateLimited<'T> =
    | Response of 'T
    | RateLimitReached

type GetRawSchemaRequest = {
    Plugin: string
    Version: string
}

type DownloadSchemaRequest = {
    Plugin: string
    Version: string
}

type DownloadTerraformMappingRequest = {
    Plugin: string
    Version: string
}

type ISchemaExplorerApi = {
    getLocalPlugins : unit -> Async<PluginReference list>
    getPulumiVersion : unit -> Async<string>
    getSchemaByPlugin: PluginReference -> Async<Result<Schema, string>>
    searchGithub: string -> Async<RateLimited<string list>>
    findGithubReleases : string -> Async<RateLimited<string list>>
    getReleaseNotes : GetReleaseNotesRequest -> Async<RateLimited<string>>
    installThirdPartyPlugin : InstallThirdPartyPluginRequest -> Async<Result<unit, string>>
    getSchemaVersionsFromGithub : GetSchemaVersionsRequest -> Async<RateLimited<string list>>
    getRawSchemaJson : GetRawSchemaRequest -> Async<Result<string, string>>
    downloadRawSchema : DownloadSchemaRequest -> Async<byte[]>
    downloadTerraformMapping : DownloadTerraformMappingRequest -> Async<Result<byte[], string>>
    diffSchema : DiffSchemaRequest -> Async<Result<DiffResult, string>>
}