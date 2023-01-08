module App

open Feliz
open Browser

let root = ReactDOM.createRoot(document.getElementById "elmish-app")
root.render(Index.View())