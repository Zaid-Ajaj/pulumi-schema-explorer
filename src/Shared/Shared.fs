namespace Shared

open System
open PulumiSchema.Types

type Todo = { Id: Guid; Description: string }

module Todo =
    let isValid (description: string) =
        String.IsNullOrWhiteSpace description |> not

    let create (description: string) =
        { Id = Guid.NewGuid()
          Description = description }

module Route =
    let builder typeName methodName =
        sprintf "/api/%s/%s" typeName methodName

type PluginReference = { Name: string; Version: string }

type ITodosApi =
    { getTodos: unit -> Async<Todo list>
      addTodo: Todo -> Async<Todo>
      getLocalPlugins : unit -> Async<PluginReference list>
      getSchemaByPlugin: PluginReference -> Async<Result<Schema, string>> }