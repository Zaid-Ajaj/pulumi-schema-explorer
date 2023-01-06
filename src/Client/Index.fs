module Index

open Elmish
open Fable.Remoting.Client
open Shared
open Feliz.UseDeferred
open Feliz.Router

let todosApi =
    Remoting.createApi ()
    |> Remoting.withRouteBuilder Route.builder
    |> Remoting.buildProxy<ITodosApi>

open Feliz

[<ReactComponent>]
let LocalPlugins() = 
    let plugins = React.useDeferred(todosApi.getLocalPlugins(), [| |])
    React.fragment [
        match plugins with
        | Deferred.HasNotStartedYet -> Html.none
        | Deferred.InProgress -> Html.p "Loading plugins..."
        | Deferred.Failed ex -> 
            Html.p [ 
                prop.style [ style.color "red" ]
                prop.text (ex.Message)
            ]

        | Deferred.Resolved plugins ->
            Html.ul [
                for plugin in plugins -> 
                Html.li [
                    Html.a [
                        prop.href (Router.format(plugin.Name, plugin.Version))
                        prop.text $"{plugin.Name} v{plugin.Version}"
                    ]
                ]
            ]
    ]

[<ReactComponent>]
let PluginSchemaExplorer(name: string, version: string) = 
    let schema = React.useDeferred(todosApi.getSchemaByPlugin { Name = name; Version = version }, [| name; version |])
    let resourceName (token: string) = Array.last(token.Split(':'))
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
            Html.p "Resources"
            Html.ul [
                for (token, resource) in Map.toList schema.resources do 
                Html.li [
                    Html.h3 [
                        prop.style [ style.fontWeight.bold ]
                        prop.text (resourceName token)
                    ]

                    Html.p token
                ]
            ]
    ]

[<ReactComponent>]
let View() =     
    let (currentUrl, setCurrentUrl) = React.useState(Router.currentUrl())
    Html.div [
        prop.style [ style.margin 20 ]
        prop.children [
            Html.h1 "Pulumi Schema Explorer"
            Html.hr [ ]
            React.router [
                router.onUrlChanged setCurrentUrl
                router.children [
                    match currentUrl with
                    | [ ] -> LocalPlugins()
                    | [ name; version ] -> PluginSchemaExplorer(name, version)
                    | _ -> Html.p "Not found :/"
                ]
            ]
        ]
    ]