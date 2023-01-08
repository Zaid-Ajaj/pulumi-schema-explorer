module Index

open System
open Fable.Remoting.Client
open Shared
open Feliz.UseDeferred
open Feliz.Router
open Feliz.SelectSearch

let todosApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ITodosApi>

open Feliz
open PulumiSchema.Types

[<ReactComponent>]
let LocalPlugins() = 
    let plugins = React.useDeferred(todosApi.getLocalPlugins(), [| |])
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
let PluginSchemaExplorer(name: string, version: string, tab: string) = 
    let schema = React.useDeferred(todosApi.getSchemaByPlugin { Name = name; Version = version }, [| name; version |])
    let selectedTab, setSelectedTab = React.useState(tab)

    React.fragment [
        match schema with
        | Deferred.HasNotStartedYet -> Html.none
        | Deferred.InProgress -> Html.p "Loading schema information..."
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
                    ]
                ]
            ]

            match selectedTab with
            | "general" -> GeneralSchemaInfo(name, version, schema)
            | "resources" -> SchemaResources(name, version, schema)
            | "functions" -> SchemaFunctions(name, version, schema)
            | otherwise -> Html.p $"Unknown tab: {otherwise}"
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