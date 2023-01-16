module Server.Tests

open Expecto

open Shared
open Server

let server = testList "Server" [

]

let all =
    testList "All"
        [
            Shared.Tests.shared
            server
        ]

[<EntryPoint>]
let main _ = runTestsWithCLIArgs [] [||] all