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

type ISchemaExplorerApi = { 
    getLocalPlugins : unit -> Async<PluginReference list>
    getSchemaByPlugin: PluginReference -> Async<Result<Schema, string>> 
    searchGithub: string -> Async<string list>
    findGithubReleases : string -> Async<string list> 
    getReleaseNotes : GetReleaseNotesRequest -> Async<string>
}