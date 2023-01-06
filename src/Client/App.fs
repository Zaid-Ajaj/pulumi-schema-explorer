module App

open Feliz
open Browser

let root = document.getElementById "elmish-app"
ReactDOM.render(Index.View(), root)