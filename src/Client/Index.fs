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
open Fable.Core

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
let Row(cells: ReactElement list) =
    Html.tr [
        for (i, cell) in List.indexed cells do
        Html.td [
            prop.key i
            prop.children cell
        ]
    ]

[<ReactComponent>]
let Table (rows: ReactElement list) =
    Html.table [
        prop.className "table"
        prop.children [
            Html.tbody [
                prop.children rows
            ]
        ]
    ]

[<ReactComponent>]
let Subtitle(text: string) =
    Html.p [
        prop.className "subtitle"
        prop.text text
    ]

[<ReactComponent>]
let Div(className: string, children: ReactElement list) =
    Html.div [
        prop.className className
        prop.children children
    ]

[<ReactComponent(import="default", from="react-highlight")>]
let Highlight(className: string, children: ReactElement array) : ReactElement = jsNative

[<ReactComponent>]
let MarkdownContent(sourceMarkdown: string) =
    Div("content", [
        Markdown.markdown [
            markdown.children sourceMarkdown
            markdown.components [
                markdown.components.pre (fun props -> React.fragment props.children)
                markdown.components.code (fun props ->
                    if props.isInline
                    then Html.code props.children
                    else Highlight(props.className, props.children)
                )
            ]
        ]
    ])

[<ReactComponent>]
let ExamplesDropdown(docs: Examples.Documentation) =
    let selectedExample, setSelectedExample = React.useState<string option>(None)
    let inline resetSelection() = setSelectedExample(None)
    React.useEffect(resetSelection, [| box docs |])

    if docs.examples.Length = 0 then
        Html.p "No examples found"
    else
    React.fragment [
        SelectSearch.selectSearch [
            selectSearch.id "examples"
            selectSearch.placeholder "Select an example"
            selectSearch.search true
            selectSearch.value (defaultArg selectedExample "")
            selectSearch.onChange (fun (selectedExample: string) -> setSelectedExample(Some selectedExample))
            selectSearch.options [
                for example in docs.examples -> {
                    value = example.Id()
                    name = example.title
                    disabled = false
                }
            ]
        ]

        match selectedExample with
        | None -> Html.none
        | Some exampleId ->
            let example = docs.examples |> List.tryFind (fun e -> e.Id() = exampleId)
            match example with
            | None -> Html.none
            | Some example ->
                Html.br [ ]
                match example.description with
                | "" -> MarkdownContent $"```{example.language}{example.code}```"
                | description ->
                    MarkdownContent description
                    Html.br [ ]
                    MarkdownContent $"```{example.language}{example.code}```"
    ]



[<ReactComponent>]
let rec RenderType(schemaType) : ReactElement =

    let canonicalToken (token: string) = 
        match token.Split("::") with
        | [| package; typeName |] -> Some (sprintf "%s:index:%s" package typeName)
        | _ ->
            match token.Split(":") with
            | [| package; moduleName; typeName |] -> 
                match moduleName.Split "/" with
                | [| firstPart; secondPart |] -> Some (sprintf "%s:%s:%s" package firstPart typeName)
                | _ -> Some (sprintf "%s:%s:%s" package moduleName typeName)
            | _ ->
                None
    
    match schemaType with
    | SchemaType.String -> Html.text "string"
    | SchemaType.Number -> Html.text "number"
    | SchemaType.Boolean -> Html.text "boolean"
    | SchemaType.Integer -> Html.text "integer"
    | SchemaType.Archive -> Html.text "archive"
    | SchemaType.Asset -> Html.text "asset"
    | SchemaType.Any -> Html.text "any"
    | SchemaType.Json -> Html.text "json"
    | SchemaType.Array elementType ->
        let renderedElementType = RenderType elementType
        Html.span [ Html.text "array<"; renderedElementType; Html.text">" ]

    | SchemaType.Ref reference ->
        let memberName (token: string) = 
            match token.Split ':' with
            | [| pkg; moduleName; memberName |] -> memberName
            | _ -> token

        match List.ofArray(reference.Split "/") |> List.filter (fun part -> part <> "") with
        | "#" :: _ :: rest -> 
            let token = (String.concat "" rest).Replace("%2F", "/")
            let currentUrl = Router.currentUrl()
            match canonicalToken token, currentUrl with
            | Some canonicalToken, (schema :: version :: rest) when schema <> "diff" -> 
                Html.a [
                    prop.href (Router.format(schema, version, [ "type", canonicalToken ]))
                    prop.text (memberName canonicalToken)
                ]
            | _ ->
                Html.text (memberName token)

        | package :: version :: "schema.json#" :: ("types" | "resources") :: rest ->
            let token = (String.concat "" rest).Replace("%2F", "/")
            match canonicalToken token with
            | Some canonicalToken -> 
                Html.a [
                    prop.href (Router.format(package, version, [ "type", canonicalToken ]))
                    prop.text (memberName canonicalToken)
                ]
            | _ ->
                Html.text (memberName token)

        | _ -> 
            Html.text $"Ref: {reference}"

    | SchemaType.Output elementType -> RenderType elementType
    | SchemaType.Map elementType ->
        let renderedElementType = RenderType elementType
        Html.span [ Html.text "map<string, "; renderedElementType; Html.text">" ]

    | anythingElse -> Html.text "<empty>"

[<ReactComponent>]
let RenderProperties(properties: Map<string, Property>) =
    Table [
        for (name, property) in Map.toList properties do
            Row [
                Html.div [
                    Html.strong name
                    Html.br [ ]
                    RenderType property.schemaType
                ]
                MarkdownContent (defaultArg property.description "")
            ]
    ]

[<ReactComponent>]
let Tab(title: string, value: string, selectedTab, onClick: string -> unit) =
    Html.li [
        prop.className (if selectedTab = value then "is-active" else "")
        prop.children [
            Html.a [
                prop.onClick (fun _ -> onClick value)
                prop.text title
            ]
        ]
    ]

let Tabs(children: ReactElement list) =
    Div("tabs", [
        Html.ul children
    ])

[<ReactComponent>]
let ResourceInfo(resource: Resource) =
    let selectedTab, setSelectedTab = React.useState "docs"
    let docs = Examples.parseDocumentation (defaultArg resource.description "")
    React.fragment [
        Tabs [
            Tab("Docs", "docs", selectedTab, setSelectedTab)
            Tab("Inputs", "inputs", selectedTab, setSelectedTab)
            Tab("Outputs", "outputs", selectedTab, setSelectedTab)
            Tab("Examples", "examples", selectedTab, setSelectedTab)
        ]

        match selectedTab with
        | "docs" -> MarkdownContent docs.description
        | "inputs" -> RenderProperties resource.inputProperties
        | "outputs" -> RenderProperties resource.properties
        | "examples" -> ExamplesDropdown docs
        | _ -> Html.none
    ]

[<ReactComponent>]
let FunctionInfo(functionDefinition: Function) =
    let selectedTab, setSelectedTab = React.useState "docs"
    let docs = Examples.parseDocumentation (defaultArg functionDefinition.description "")
    React.fragment [
        Tabs [
            Tab("Docs", "docs", selectedTab, setSelectedTab)
            Tab("Inputs", "inputs", selectedTab, setSelectedTab)
            Tab("Outputs", "outputs", selectedTab, setSelectedTab)
            Tab("Examples", "examples", selectedTab, setSelectedTab)
        ]

        match selectedTab with
        | "docs" -> MarkdownContent docs.description
        | "inputs" ->
            match functionDefinition.inputs with
            | None -> Html.p "No inputs"
            | Some inputs ->
                MarkdownContent (defaultArg inputs.description "")
                RenderProperties inputs.properties
        | "outputs" ->
            match functionDefinition.returnType with
            | SchemaType.Object properties -> RenderProperties properties
            | _ -> RenderType functionDefinition.returnType
        | "examples" ->
            ExamplesDropdown docs
        | _ ->
            Html.p [ prop.text $"Unknown tab: {selectedTab}" ]
    ]


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

        match selectedResource with
        | None -> Html.none
        | Some resource -> ResourceInfo(resource)
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

            match selectedFunction with
            | None -> Html.none
            | Some functionDefinition -> FunctionInfo(functionDefinition)
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
let SchemaVersions(repository: string, schemaVersion: string, schema: Schema) = 
    let fullRepoName = repository.Replace("https://", "").Replace("http://", "").Replace("github.com/", "")
    let inline getVersions() = async {
        match fullRepoName.Split "/" with
        | [| owner; repo |] ->
            let request = { Owner = owner; Repository = repo }
            return! schemaExplorerApi.getSchemaVersionsFromGithub request
        | _ ->
            return [ ]
    }

    let versions = React.useDeferred(getVersions(), [| repository |])
    
    match versions with 
    | Deferred.HasNotStartedYet -> 
        Html.none

    | Deferred.InProgress -> 
        Div("block", [
            Html.p "Loading versions"
            Html.progress [
                prop.className "progress is-small is-primary"
                prop.max 100
            ]
        ])

    | Deferred.Failed ex ->
        Html.p [
            prop.style [ style.color "red" ]
            prop.text (ex.Message)
        ]

    | Deferred.Resolved versions ->
        Html.ul [
            for version in versions do
            let isCurrentVersion = version = schemaVersion
            Html.li [
                prop.style [ style.marginBottom 20 ]
                prop.children [
                    Html.p [
                        prop.style [ style.fontSize 18 ]
                        prop.text $"{capitalize(schema.name)} v{version}"
                    ]
                    Html.button [
                        prop.className [ "button"; if not isCurrentVersion then "is-primary" ]
                        prop.style [ style.marginRight 10 ]
                        prop.text "Explore"
                        prop.disabled isCurrentVersion
                        prop.onClick (fun _ -> 
                            match fullRepoName.Split "/" with
                            | [| "pulumi"; repoName |] when repoName.StartsWith "pulumi-" ->
                                let firstPartyPulumiPlugin = repoName.Replace("pulumi-", "")
                                Router.navigate(firstPartyPulumiPlugin, version)

                            | [| owner; repoName |] when repoName.StartsWith "pulumi-" ->
                                let thirdPartyPulumiPlugin = repoName.Replace("pulumi-", "")
                                Router.navigate("third-party-plugin", owner, thirdPartyPulumiPlugin, version)

                            | otherwise ->
                                Router.navigate("unknown-pulumi-plugin")
                        )
                    ]

                    Html.button [
                        prop.className [ "button"; if not isCurrentVersion then "is-primary" ]
                        prop.disabled isCurrentVersion
                        prop.text "Diff"
                        prop.onClick (fun _ -> 
                            match fullRepoName.Split "/" with
                            | [| "pulumi"; repoName |] when repoName.StartsWith "pulumi-" ->
                                let firstPartyPulumiPlugin = repoName.Replace("pulumi-", "")
                                Router.navigate("diff", firstPartyPulumiPlugin, schemaVersion, version)

                            | [| owner; repoName |] when repoName.StartsWith "pulumi-" ->
                                let thirdPartyPulumiPlugin = repoName.Replace("pulumi-", "")
                                Router.navigate("diff-third-party-plugin", owner, thirdPartyPulumiPlugin, schemaVersion, version)
                            
                            | otherwise ->
                                Router.navigate("unknown-pulumi-plugin")
                        )
                    ]
                ]
            ]
        ]


[<ReactComponent>]
let PluginSchemaExplorer(name: string, version: string, tab: string) =
    let schema = React.useDeferred(
        schemaExplorerApi.getSchemaByPlugin { Name = name; Version = version }, 
        [| name; version |])

    let selectedTab, setSelectedTab = React.useState(tab)

    React.fragment [
        match schema with
        | Deferred.HasNotStartedYet -> Html.none
        | Deferred.InProgress -> 
            Div("block", [
                Html.p "Loading schema information, this may take a moment..."
                Html.progress [
                    prop.className "progress is-small is-primary"
                    prop.max 100
                ]
            ])

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
            Tabs [
                Tab("General", "general", selectedTab, setSelectedTab)
                Tab($"Resources ({Map.count schema.resources})", "resources", selectedTab, setSelectedTab)
                Tab($"Functions ({Map.count schema.functions})", "functions", selectedTab, setSelectedTab)
                match schema.repository with
                | Some repo -> 
                    Tab("Release Notes", "release-notes", selectedTab, setSelectedTab)
                    Tab("Versions", "versions", selectedTab, setSelectedTab)
                | None -> Html.none
            ]

            match selectedTab with
            | "general" -> GeneralSchemaInfo(name, version, schema)
            | "resources" -> SchemaResources(name, version, schema)
            | "functions" -> SchemaFunctions(name, version, schema)
            | "release-notes" ->
                match schema.repository with
                | Some repoUrl -> ReleaseNotes(repoUrl, version)
                | None -> Html.none

            | "versions" -> 
                match schema.repository with
                | Some repoUrl -> SchemaVersions(repoUrl, version, schema)
                | None -> Html.none


            | otherwise -> Html.none
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
            match repo.Split "/" with
            | [| "pulumi"; repoName |] when repoName.StartsWith "pulumi-" ->
                let firstPartyPulumiPlugin = repoName.Replace("pulumi-", "")
                Router.navigate(firstPartyPulumiPlugin, version)

            | [| owner; repoName |] when repoName.StartsWith "pulumi-" ->
                let thirdPartyPulumiPlugin = repoName.Replace("pulumi-", "")
                Router.navigate("third-party-plugin", owner, thirdPartyPulumiPlugin, version)

            | otherwise ->
                Router.navigate("unknown-pulumi-plugin")
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
let InstallThirdPartyPlugin(owner: string, plugin: string, version: string, onInstalled: unit -> unit) =
    let inline requestInput() = {
        Owner = owner
        PluginName = plugin
        Version = version
    }

    let installation = React.useDeferred(
        schemaExplorerApi.installThirdPartyPlugin(requestInput()), 
        [| owner; plugin; version |])

    let inline redirectWhenInstalled() =
        match installation with
        | Deferred.Resolved (Ok ()) ->
            onInstalled()
        | _ ->
            ignore()

    React.useEffect(redirectWhenInstalled, [| box installation |])

    match installation with
    | Deferred.HasNotStartedYet -> Html.none
    | Deferred.InProgress ->
        Html.div [
            prop.className "block"
            prop.children [
                Html.p [
                    prop.style [ style.marginBottom 5 ]
                    prop.text $"Installing third-party plugin {plugin} v{version}..."
                ]

                Html.progress [
                    prop.className "progress is-small is-primary"
                    prop.max 100
                ]
            ]
        ]

    | Deferred.Failed error ->
        Html.p [
            prop.style [ style.color.red ]
            prop.text error.Message
        ]

    | Deferred.Resolved (Error errorMessage) ->
        Html.p [
            prop.style [ style.color.darkOrchid ]
            prop.text errorMessage
        ]

    | Deferred.Resolved (Ok ()) ->
        Html.none

[<ReactComponent>]
let RenderResourceChanges(changes: ResourceChange list) = 
    Html.ul [
        prop.style [ style.marginLeft 20 ]
        prop.children [
            for change in changes do
            Html.li [
                prop.style [ style.marginBottom 10 ]
                prop.children [
                    match change with 
                    | ResourceChange.AddedProperty (name, property) -> 
                        Html.i [ 
                            prop.className "fas fa-plus"; 
                            prop.style [ style.marginRight 10; style.color.green ]
                        ]
                    
                        Html.span [ 
                            prop.style [ style.fontSize 18; style.marginRight 10; style.color.green ]
                            prop.children [
                                Html.text $"{name} "
                                Html.span [
                                    prop.style [ style.color.black; style.fontSize 12 ]
                                    prop.children [
                                        Html.text "("
                                        RenderType property.schemaType
                                        Html.text ")"
                                    ]
                                ]
                            ]
                        ]

                        Html.span [ 
                            prop.style [ style.fontSize 14; style.marginLeft 5; style.color.black ]
                            prop.children [
                                MarkdownContent (defaultArg property.description "")
                            ]
                        ]

                    | ResourceChange.RemovedProperty (name, property) -> 
                        Html.i [ 
                            prop.className "fas fa-minus"; 
                            prop.style [ style.marginRight 10; style.color.red ]
                        ]
                    
                        Html.span [ 
                            prop.style [ style.fontSize 18; style.marginRight 10; style.color.red ]
                            prop.children [
                                Html.text $"{name} "
                                Html.span [
                                    prop.style [ style.color.black; style.fontSize 12 ]
                                    prop.children [
                                        Html.text "("
                                        RenderType property.schemaType
                                        Html.text ", output)"
                                    ]
                                ]
                            ]
                        ]

                        Html.span [ 
                            prop.style [ style.fontSize 14; style.marginLeft 5; style.color.black ]
                            prop.children [
                                MarkdownContent (defaultArg property.description "")
                            ]
                        ]

                    | ResourceChange.MarkedDeprecated (name, property) -> 
                        Html.i [ 
                            prop.className "fas fa-exclamation-triangle"; 
                            prop.style [ style.marginRight 10; style.color.darkGoldenRod ]
                        ]

                        Html.span [ 
                            prop.style [ style.fontSize 18; style.marginRight 10; style.color.darkGoldenRod ]
                            prop.children [
                                Html.span [ 
                                    prop.style [ style.color.darkGoldenRod; style.fontWeight.bold ]
                                    prop.text "DEPRECATED" 
                                ]
                                Html.text $" {name} "
                                Html.span [
                                    prop.style [ style.color.black; style.fontSize 12 ]
                                    prop.children [
                                        Html.text "("
                                        RenderType property.schemaType
                                        Html.text ")"
                                    ]
                                ]
                            ]
                        ]

                        Html.span [ 
                            prop.style [ style.fontSize 14; style.marginLeft 5; style.color.black ]
                            prop.children [
                                MarkdownContent ("Deprecation message: " + defaultArg property.deprecationMessage "")
                            ]
                        ]
                ]
            ]
        ]
    ]


[<ReactComponent>]
let PluginSchemaDiff(name, versionA, versionB) = 
    let selectedTab, setSelectedTab = React.useState "added-resources"
    let diffResults = React.useDeferred(
        schemaExplorerApi.diffSchema { Plugin = name; VersionA = versionA; VersionB = versionB }, 
        [| name; versionA |])
    
    React.fragment [
        Html.h1 [
            prop.className "subtitle"
            prop.children [
                Html.text $"Diff of {capitalize(name)} v{versionA} "
                Html.i [ prop.className "fas fa-arrow-right" ]
                Html.text $" v{versionB}"
            ]
        ]

        match diffResults with 
        | Deferred.HasNotStartedYet -> Html.none
        | Deferred.InProgress -> 
            Html.div [
                prop.className "block"
                prop.children [
                    Html.p [
                        prop.style [ style.marginBottom 5 ]
                        prop.text $"Loading diff of {capitalize(name)} v{versionA} and v{versionB}..."
                    ]

                    Html.progress [
                        prop.className "progress is-small is-primary"
                        prop.max 100
                    ]
                ]
            ]

        | Deferred.Failed error ->
            Html.p [
                prop.style [ style.color.red ]
                prop.text error.Message
            ]

        | Deferred.Resolved (Error errorMessage) ->
            Html.p [
                prop.style [ style.color.darkOrchid ]
                prop.text errorMessage
            ]

        | Deferred.Resolved (Ok diff) ->
            Tabs [
                Tab($"Added resources ({List.length diff.AddedResources})", "added-resources", selectedTab, setSelectedTab)
                Tab($"Removed resources ({List.length diff.RemovedResources})", "removed-resources", selectedTab, setSelectedTab)
                Tab($"Changed resources ({List.length diff.ChangedResources})", "changed-resources", selectedTab, setSelectedTab)
            ]
            
            match selectedTab with
            | "added-resources" -> 
                Html.ul [
                    for resource in diff.AddedResources do
                    Html.li [
                        prop.style [ style.color.green ]
                        prop.children [
                            Html.i [ 
                                prop.className "fas fa-plus"; 
                                prop.style [ style.marginRight 10 ]
                            ]
                        
                            Html.span [ 
                                prop.style [ style.fontSize 18; style.marginRight 10 ]
                                prop.text (memberName resource.token)
                            ]

                            Html.span [ 
                                prop.style [ style.fontSize 12; style.color.black ]
                                prop.text (resource.token)
                            ]
                        ]
                    ] 
                ]

            | "removed-resources" -> 
                Html.ul [
                    for resource in diff.RemovedResources do
                    Html.li [
                        prop.style [ style.color.red ]
                        prop.children [
                            Html.i [ 
                                prop.className "fas fa-minus"; 
                                prop.style [ style.marginRight 10 ]
                            ]
                        
                            Html.span [ 
                                prop.style [ style.fontSize 18; style.marginRight 10 ]
                                prop.text (memberName resource.token)
                            ]

                            Html.span [ 
                                prop.style [ style.fontSize 14; style.color.black ]
                                prop.text (resource.token)
                            ]
                        ]
                    ] 
                ]

            | "changed-resources" ->
                Html.ul [
                    for changedResource in diff.ChangedResources do
                    Html.li [
                        prop.style [ style.color.darkGoldenRod; style.marginBottom 20 ]
                        prop.children [
                            Html.i [ 
                                prop.className "fas fa-exchange-alt"; 
                                prop.style [ style.marginRight 10 ]
                            ]

                            Html.span [ 
                                prop.style [ style.fontSize 18; style.marginRight 10 ]
                                prop.text (memberName changedResource.Resource.token)
                            ]

                            Html.span [ 
                                prop.style [ style.fontSize 12; style.color.black ]
                                prop.text changedResource.Resource.token
                            ]

                            if not changedResource.Inputs.IsEmpty then
                                Html.p [
                                    prop.style [ style.color.black ]
                                    prop.text "Inputs"
                                ]
                                RenderResourceChanges changedResource.Inputs

                            if not changedResource.Outputs.IsEmpty then
                                Html.p [
                                    prop.style [ style.color.black ]
                                    prop.text "Outputs"
                                ]
                                RenderResourceChanges changedResource.Outputs
                        ]
                    ]
                ]

            | _ -> 
                Html.p "Unknown tab"
    ]

[<ReactComponent>]
let SchemaTypeExplorer(name, version, token) = 
    let schema = React.useDeferred(
        schemaExplorerApi.getSchemaByPlugin { Name = name; Version = version }, 
        [| name; version |])

    match schema with 
    | Deferred.HasNotStartedYet -> Html.none
    | Deferred.InProgress -> 
        Html.progress [
            prop.className "progress is-small is-primary"
            prop.max 100
        ]

    | Deferred.Failed error ->
        Html.p [
            prop.style [ style.color.red ]
            prop.text error.Message
        ]

    | Deferred.Resolved (Error errorMessage) ->
        Html.p [
            prop.style [ style.color.darkOrchid ]
            prop.text errorMessage
        ]
    
    | Deferred.Resolved (Ok schema) ->
        match schema.types |> Map.tryFind token with
        | None -> 
            match schema.resources |> Map.tryFind token with
            | None -> 
                Html.p [
                    prop.style [ style.color.red ]
                    prop.text $"Could not find type {token}"
                ]

            | Some resource -> 
                ResourceInfo resource

        | Some schemaTypeDef ->
            React.fragment [
                Html.p [
                    prop.style [ style.color.black ]
                    prop.className "title"
                    prop.text (memberName token)
                ]

                MarkdownContent (defaultArg schemaTypeDef.description "")
                match schemaTypeDef.schemaType with
                | SchemaType.Object properties -> 
                    RenderProperties properties
                | _ -> 
                    RenderType schemaTypeDef.schemaType
            ]

[<ReactComponent>]
let View() =
    let (currentUrl, setCurrentUrl) = React.useState(Router.currentUrl())
    Html.div [
        prop.style [ style.margin 20 ]
        prop.children [
        
            Html.span [
                prop.style [ style.fontSize 24; style.display.flex; style.justifyContent.left; style.alignContent.center ]
                prop.children [
                    Html.img [
                        prop.src "https://www.pulumi.com/logos/brand/avatar-on-white.png"
                        prop.style [ style.height 50; style.marginRight 20 ]
                    ]

                    Html.span [
                        prop.style [ style.marginTop 6 ]
                        prop.text "Pulumi Schema Explorer"
                    ]
                ]
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
                                    | [ "unknown-pulumi-plugin" ] ->
                                        Html.p "Selected repository is not a Pulumi plugin"
                                    | [ "diff"; name; versionA; verionB ] -> 
                                        PluginSchemaDiff(name, versionA, verionB)
                                    | [ "diff-third-party-plugin"; owner; name; versionA; versionB ] ->
                                        let onInstall() = Router.navigate("diff", name, versionA, versionB)
                                        InstallThirdPartyPlugin(owner, name, versionB, onInstall)
                                    | [ "third-party-plugin"; owner; name; version ] ->
                                        let onInstalled() = Router.navigate(name, version)
                                        InstallThirdPartyPlugin(owner, name, version, onInstalled)
                                    | [ name; version ] ->
                                        PluginSchemaExplorer(name, version, "general")
                                    | [ name; version; Route.Query [ "type", token ] ] ->
                                        SchemaTypeExplorer(name, version, token)
                                    | _ ->
                                        Html.p "Select a plugin to explore"
                                ]
                            ]
                        ]
                    ]
                ]
            ]
        ]
    ]