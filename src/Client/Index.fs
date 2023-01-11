module Index

open Feliz
open Feliz.UseDeferred
open Feliz.Router
open Feliz.SelectSearch
open Feliz.Markdown

open Shared
open PulumiSchema.Types
open System
open Fable.Remoting.Client

let schemaExplorerApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ISchemaExplorerApi>


[<ReactComponent>]
let LocalPlugins() = 
    let plugins = React.useDeferred(schemaExplorerApi.getLocalPlugins(), [| |])
    let selectedPlugin, setSelectedPlugin = React.useState<string option>(None)
    React.fragment [
        match plugins with
        | Deferred.HasNotStartedYet -> Html.none
        | Deferred.InProgress -> Html.p "Loading plugins..."
        | Deferred.Failed ex -> 
            Html.p [ 
                prop.style [ style.color "red" ]
                prop.text ex.Message
            ]

        | Deferred.Resolved plugins ->
            SelectSearch.selectSearch [
                selectSearch.id "local-plugins"
                selectSearch.placeholder "Select a local plugin"
                selectSearch.search true
                selectSearch.value (defaultArg selectedPlugin "")
                selectSearch.onChange (fun (selectedPlugin: string) -> 
                    match selectedPlugin.Split "@" with 
                    | [| name; version |] -> 
                        setSelectedPlugin(Some selectedPlugin)
                        Router.navigate(name, version)
                    | _ -> ignore()
                )
                selectSearch.options [
                    for plugin in plugins -> {
                        value = $"{plugin.Name}@{plugin.Version}"
                        name = $"{plugin.Name} v{plugin.Version}"
                        disabled = false
                    }
                ]
            ]
    ]

let inline moduleName (token: string) = token.Split ':' |> Array.skip 1 |> Array.head 
let inline memberName (token: string) = Array.last(token.Split(':'))

let private findModules(schema: Schema) = 
    let allModules = [
        for resource in schema.resources.Values -> moduleName resource.token
        for func in schema.functions.Values -> moduleName func.token
    ] 

    allModules
    |> List.distinct


[<ReactComponent>]
let SchemaResources(name: string, version: string, schema: Schema) = 
    let selectedModule, setSelectedModule = React.useState<string option>(None)
    let selectedResource, setSelectedResource = React.useState<Resource option>(None)
    let selectedResourceValue = React.useMemo((fun () -> 
        match selectedResource with
        | Some resource -> resource.token
        | None -> ""), [| selectedResource |])
    
    let inline modules() = 
        [
            { value = "_all_"; name = "All"; disabled = false }
            for modName in findModules schema do
               { value = modName; name = modName; disabled = false }
        ]

    let modules = React.useMemo(modules, [| schema |])

    let inline hasManyModules() = findModules schema <> [ "index" ]

    let hasManyModules = React.useMemo(hasManyModules, [| schema |])

    let inline resources() = 
        schema.resources.Values
        |> Seq.filter (fun resource -> 
            match selectedModule with
            | Some "_all_" -> true
            | Some modName -> modName = moduleName resource.token
            | None -> true
        )
        |> Seq.map (fun resource -> {
            value = resource.token
            name = memberName resource.token
            disabled = false
        })
        |> Seq.toList

    let resources = React.useMemo(resources, [| schema; selectedModule |])

    React.fragment [

        if hasManyModules then
            Html.p $"Modules in {name} v{version}"
            SelectSearch.selectSearch [
                selectSearch.id "modules"
                selectSearch.placeholder $"Filter by module"
                selectSearch.onChange (fun token -> setSelectedModule(Some token))
                selectSearch.value (defaultArg selectedModule "")
                selectSearch.options modules
                selectSearch.search true
            ]

            Html.br [ ]
    
        Html.p $"Resources in {name} v{version}"
        SelectSearch.selectSearch [
            selectSearch.id "resources"
            selectSearch.placeholder $"Find resources"
            selectSearch.onChange (fun token -> setSelectedResource(Some schema.resources.[token]))
            selectSearch.value selectedResourceValue
            selectSearch.options resources
            selectSearch.search true
        ]
        
        Html.br [ ]
    ]

[<ReactComponent>]
let SchemaFunctions(name: string, version: string, schema: Schema) = 
    let selectedModule, setSelectedModule = React.useState<string option>(None)
    let selectedFunction, setSelectedFunction = React.useState<Function option>(None)
    let selectedFunctionValue = React.useMemo((fun () ->
        match selectedFunction with
        | Some func -> func.token
        | None -> ""), [| selectedFunction |])

    let inline modules() = 
        [
            { value = "_all_"; name = "All"; disabled = false }
            for modName in findModules schema do
               { value = modName; name = modName; disabled = false }
        ]

    let modules = React.useMemo(modules, [| schema |])

    let inline functions() = 
        schema.functions.Values
        |> Seq.filter (fun func -> 
            match selectedModule with
            | Some "_all_" -> true
            | Some modName -> modName = moduleName func.token
            | None -> true
        )
        |> Seq.map (fun func -> {
            value = func.token
            name = memberName func.token
            disabled = false
        })
        |> Seq.toList

    let inline hasManyModules() = findModules schema <> [ "index" ]

    let hasManyModules = React.useMemo(hasManyModules, [| schema |])

    let functions = React.useMemo(functions, [| schema; selectedModule |])

    if Map.isEmpty schema.functions then
        Html.p "No functions found in the schema"
    else
    React.fragment [
        if hasManyModules then
            Html.p $"Modules in {name} v{version}"
            SelectSearch.selectSearch [
                selectSearch.id "modules"
                selectSearch.placeholder $"Filter by module"
                selectSearch.onChange (fun token -> setSelectedModule(Some token))
                selectSearch.value (defaultArg selectedModule "")
                selectSearch.options modules
                selectSearch.search true
            ]

            Html.br [ ]

        if List.isEmpty functions then
            Html.p "No functions found in the selected module"
        else
            Html.p $"Functions in {name} v{version}"
            SelectSearch.selectSearch [
                selectSearch.id "functions"
                selectSearch.placeholder $"Find functions"
                selectSearch.onChange (fun token -> setSelectedFunction(Some schema.functions.[token]))
                selectSearch.value selectedFunctionValue
                selectSearch.options functions
                selectSearch.search true
            ]
            
            Html.br [ ]
    ]

let inline capitalize(input: string) = 
    if String.IsNullOrWhiteSpace input then
        ""
    else 
      input.[0].ToString().ToUpper() + input.[1..]

[<ReactComponent>]
let GeneralSchemaInfo(name: string, version: string, schema: Schema) = 
    React.fragment [
        Html.h1 [
            prop.style [ style.fontSize 24 ]
            prop.text $"{capitalize(defaultArg schema.displayName name)} v{version}"
        ]

        match schema.description with
        | Some description -> Html.p description
        | None -> Html.none

        match schema.publisher with 
        | Some publisher ->  Html.p $"Publisher {publisher}"
        | None -> Html.none

        match schema.repository with
        | Some repository -> Html.p $"Repository {repository}"
        | None -> Html.none

        match schema.homepage with 
        | Some homepage -> Html.p $"Homepage {homepage}"
        | None -> Html.none

        match schema.license with 
        | Some license -> Html.p $"License {license}"
        | None -> Html.none
    ]

[<ReactComponent>]
let ReleaseNotes(repository: string, version: string) = 
    let inline getReleaseNotes() = async {
        let fullRepoName = repository.Replace("https://", "").Replace("http://", "").Replace("github.com/", "")
        match fullRepoName.Split "/" with
        | [| owner; repo |] -> 
            let request = { Owner = owner; Repository = repo; Version = version }
            return! schemaExplorerApi.getReleaseNotes request
        | _ -> 
            return ""
    }

    let releaseNotes = React.useDeferred(getReleaseNotes(), [| repository; version |])
    match releaseNotes with
    | Deferred.HasNotStartedYet -> Html.none
    | Deferred.InProgress -> Html.p "Loading release notes"
    | Deferred.Failed ex -> 
        Html.p [ 
            prop.style [ style.color "red" ]
            prop.text (ex.Message)
        ]

    | Deferred.Resolved releaseNotesBody -> 
        Html.div [
            prop.className "content"
            prop.children [
                Markdown.markdown releaseNotesBody
            ]
        ]

[<ReactComponent>]
let PluginSchemaExplorer(name: string, version: string, tab: string) = 
    let schema = React.useDeferred(schemaExplorerApi.getSchemaByPlugin { Name = name; Version = version }, [| name; version |])
    let selectedTab, setSelectedTab = React.useState(tab)

    React.fragment [
        match schema with
        | Deferred.HasNotStartedYet -> Html.none
        | Deferred.InProgress -> Html.p "Loading schema information, this may take a moment..."
        | Deferred.Failed ex -> 
            Html.p [ 
                prop.style [ style.color "red" ]
                prop.text (ex.Message)
            ]

        | Deferred.Resolved (Error schemaError) -> 
            Html.p [ 
                prop.style [ style.color "red" ]
                prop.text schemaError
            ]

        | Deferred.Resolved (Ok schema) ->

            Html.div [
                prop.className "tabs"
                prop.children [
                    Html.ul [
                        Html.li [
                            prop.className [ if selectedTab = "general" then "is-active" ]
                            prop.children [
                                Html.a [
                                    prop.onClick (fun _ -> setSelectedTab "general")
                                    prop.text "General"
                                ]
                            ]
                        ]
                        Html.li [
                            prop.className [ if selectedTab = "resources" then "is-active" ]
                            prop.children [
                                Html.a [
                                    prop.onClick (fun _ -> setSelectedTab "resources")
                                    prop.text $"Resources ({Map.count schema.resources})"
                                ]
                            ]
                        ]
                        Html.li [
                            prop.className [ if selectedTab = "functions" then "is-active" else "" ]
                            prop.children [
                                Html.a [
                                    prop.onClick (fun _ -> setSelectedTab "functions")
                                    prop.text $"Functions ({Map.count schema.functions})"
                                ]
                            ]
                        ]

                        match schema.repository with 
                        | Some repo -> 
                            Html.li [
                                prop.className [ if selectedTab = "release-notes" then "is-active" else "" ]
                                prop.children [
                                    Html.a [
                                        prop.onClick (fun _ -> setSelectedTab "release-notes")
                                        prop.text "Release Notes"
                                    ]
                                ]
                            ]

                        | None -> Html.none
                    ]
                ]
            ]

            match selectedTab with
            | "general" -> GeneralSchemaInfo(name, version, schema)
            | "resources" -> SchemaResources(name, version, schema)
            | "functions" -> SchemaFunctions(name, version, schema)
            | "release-notes" -> ReleaseNotes(defaultArg schema.repository "", version)
            | otherwise -> Html.p $"Unknown tab: {otherwise}"
        ]

[<ReactComponent>]
let GithubReleases(repo: string) = 
    let githubReleases = React.useDeferred(schemaExplorerApi.findGithubReleases repo, [| repo |])
    let selectedRelease, setSelectedRelease = React.useState<string option>(None)
    let inline searchResults() = 
        match githubReleases with 
        | Deferred.Resolved releases -> 
            [
                for release in releases -> {
                    value = release
                    name = release
                    disabled = false
                }
            ] 

        | _ -> [ ]

    let options = React.useMemo(searchResults, [| githubReleases |])
    SelectSearch.selectSearch [
        selectSearch.id "github-releases"
        selectSearch.placeholder (
            if Deferred.inProgress githubReleases 
            then "Loading releases" 
            elif List.isEmpty options
            then "No releases found"
            else "Pick a release to explore"
        )
        selectSearch.onChange (fun version -> 
            setSelectedRelease(Some version)
            Router.navigate(repo.Replace("pulumi/pulumi-", ""), version)
        )
        selectSearch.value (defaultArg selectedRelease "")
        selectSearch.options options
        if not (List.isEmpty options) && selectedRelease.IsNone then
            selectSearch.printOptions.always
    ]

[<ReactComponent>]
let SearchGithub() = 
    let inputRef = React.useInputRef()
    let selectedRepo, setSelectedRepo = React.useState<string option>(None)
    let searchResults, setSearchResults = React.useState(Deferred.HasNotStartedYet)
    let search = React.useDeferredCallback(schemaExplorerApi.searchGithub, setSearchResults)

    let inline searchResultOptions() = 
        match searchResults with 
        | Deferred.Resolved results -> 
            [ for result in results -> { value = result; name = result; disabled = false } ]
        
        | _ -> 
            [ ]

    let options = React.useMemo(searchResultOptions, [| searchResults |])

    React.fragment [
        Html.input [
            prop.className "input"
            prop.placeholder "Type a repository's name"
            prop.style [ style.marginBottom 10 ]
            prop.ref inputRef
            prop.onKeyUp(key.enter, fun ev -> 
                setSelectedRepo None
                inputRef.current
                |> Option.iter (fun element -> search element.value)
            )
        ]

        SelectSearch.selectSearch [
            selectSearch.placeholder (
                if Deferred.inProgress searchResults 
                then "Loading" 
                elif List.isEmpty options
                then "Search results"
                else "Pick a repository from search results"
            )
            selectSearch.search true
            selectSearch.onChange (fun repo -> setSelectedRepo(Some repo))
            selectSearch.value (defaultArg selectedRepo "")
            selectSearch.options options
            if not (List.isEmpty options) && selectedRepo.IsNone then
                selectSearch.printOptions.always
        ]

        match selectedRepo with 
        | Some repo -> 
            Html.div [ prop.style [ style.marginBottom 5 ] ]
            GithubReleases(repo)

        | None -> 
            Html.none
    ]

[<ReactComponent>]
let View() =
    let (currentUrl, setCurrentUrl) = React.useState(Router.currentUrl())
    Html.div [
        prop.style [ style.margin 20 ]
        prop.children [
            Html.h1 [
                prop.style [ style.fontSize 24 ]
                prop.text "Pulumi Schema Explorer"
            ]

            Html.hr [ ]

            Html.div [
                prop.style [
                    style.display.flex
                ]

                prop.children [
                    Html.div [
                        prop.style [
                            style.custom("flex", "20%")
                        ]

                        prop.children [
                            Html.h2 "Local resource plugins"
                            LocalPlugins()
                            Html.br [ ]
                            Html.h2 "Search Github"
                            SearchGithub()
                        ]
                    ]

                    Html.div [
                        prop.style [ 
                            style.custom("flex", "80%") 
                            style.paddingRight 30
                            style.paddingLeft 30
                        ]
                        prop.children [
                            React.router [
                                router.onUrlChanged setCurrentUrl
                                router.children [
                                    match currentUrl with
                                    | [ name; version ] -> PluginSchemaExplorer(name, version, "general")
                                    | _ -> Html.p "Select a plugin to explore"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]