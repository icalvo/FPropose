namespace FPropose

/// A single node in an explanation tree produced by evaluating a predicate on a value.
[<RequireQualifiedAccess>]
type ExplainTree =
    | Leaf of name: string * passed: bool * detail: string
    | All of passed: bool * items: ExplainTree list
    | Any of passed: bool * items: ExplainTree list
    /// Every element of a sequence satisfied the inner predicate (`Pred.forAll`).
    | ForAll of name: string * passed: bool * items: ExplainTree list
    | Not of passed: bool * inner: ExplainTree
    | Skipped of name: string * reason: string

    /// Renders the tree with two-space indentation per depth level.
    member this.Format() : string =
        let rec write depth (sb: System.Text.StringBuilder) (node: ExplainTree) =
            let pad = System.String(' ', depth * 2)

            match node with
            | Leaf(name, passed, detail) ->
                let mark = if passed then "✓" else "✗"
                sb.AppendLine($"{pad}[{mark}] {name}: {detail}") |> ignore
            | All(passed, items) ->
                let mark = if passed then "✓" else "✗"
                sb.AppendLine($"{pad}[{mark}] ALL") |> ignore

                for item in items do
                    write (depth + 1) sb item
            | Any(passed, items) ->
                let mark = if passed then "✓" else "✗"
                sb.AppendLine($"{pad}[{mark}] ANY") |> ignore

                for item in items do
                    write (depth + 1) sb item
            | ForAll(name, passed, items) ->
                let mark = if passed then "✓" else "✗"
                sb.AppendLine($"{pad}[{mark}] FOR ALL {name}") |> ignore

                if List.isEmpty items && passed then
                    sb.AppendLine($"{pad}  (no elements — vacuously true.)") |> ignore

                for item in items do
                    write (depth + 1) sb item
            | Not(passed, inner) ->
                let mark = if passed then "✓" else "✗"
                sb.AppendLine($"{pad}[{mark}] NOT") |> ignore
                write (depth + 1) sb inner
            | Skipped(name, reason) ->
                sb.AppendLine($"{pad}[…] {name}: {reason}") |> ignore

        let sb = System.Text.StringBuilder()
        write 0 sb this
        sb.ToString().TrimEnd()

/// The outcome of evaluating a predicate with explanations.
type PropositionResult =
    { Passed: bool
      Tree: ExplainTree }

/// Helpers for working with explanation trees.
[<RequireQualifiedAccess>]
module ExplainTree =
    /// Renders the tree with two-space indentation per depth level.
    let format (node: ExplainTree) : string = node.Format()
