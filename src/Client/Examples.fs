module Examples

type Example = {
    description: string 
    code: string 
    language: string
}

type Documentation = {
    description: string
    examples: Example list
}

let parseExample (example: string) (language: string) : Example option = 
    let description = 
        match example.IndexOf "```" with
        | -1 -> example
        | index -> example.Substring(0, index)

    match example.IndexOf $"```{language}" with
    | -1 -> None
    | index -> 
        let code = example.Substring(index + 3 + language.Length)
        match code.IndexOf "```" with
        | -1 -> None
        | index -> 
            let code = code.Substring(0, index)
            Some { 
                description = description
                code = code
                language = language
            }

let rec findExamples (docs: string) examples = 
    match docs.IndexOf "{{% example %}}", docs.IndexOf "{{% /example %}}" with
    | -1, _ -> examples
    | _, -1 -> examples
    | start, finish -> 
        let startWithoutExample = start + "{{% example %}}".Length
        let example = docs.Substring(startWithoutExample, finish - startWithoutExample)
        findExamples (docs.Substring(finish + 1)) (example :: examples)


let parseDocumentation (documentation: string) : Documentation = 
    let description =
        match documentation.IndexOf "{{% examples %}}" with
        | -1 -> documentation
        | index -> documentation.Substring(0, index) 

    let languages = [ "typescript"; "csharp"; "python"; "java"; "go"; "yaml"]
    let matches = findExamples documentation  []
    let examples = [
        for foundMatch in matches do 
        for language in languages do
        match parseExample foundMatch language with
        | Some example -> example
        | None -> ()
    ]

    { description = description
      examples = examples }