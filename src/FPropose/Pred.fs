namespace FPropose

/// Composable predicate: evaluates to a boolean and can produce an explanation for a given input.
[<Struct>]
type ExplainMode =
    /// Match short-circuiting of `&&` and `||` when building the explanation tree.
    | Lazy
    /// Evaluate every sub-predicate regardless of short-circuiting (useful for audits).
    | Eager

type Pred<'a> =
    | Leaf of name: string * test: ('a -> bool) * explain: ('a -> bool -> string)
    | And of Pred<'a> * Pred<'a>
    | Or of Pred<'a> * Pred<'a>
    | Not of Pred<'a>
    /// Universal quantification with nested inner explanations (see `Pred.forAll`).
    /// Stored as closures so `Pred<'ctx>` can quantify over a distinct element type without GADTs.
    | ForAll of
        name: string *
        evalForAll: ('a -> bool) *
        explainLazyForAll: ('a -> bool * ExplainTree) *
        explainEagerForAll: ('a -> bool * ExplainTree)
    /// Existential quantification with nested inner explanations (see `Pred.exists`).
    /// Stored as closures so `Pred<'ctx>` can quantify over a distinct element type without GADTs.
    | Exists of
        name: string *
        evalExists: ('a -> bool) *
        explainLazyExists: ('a -> bool * ExplainTree) *
        explainEagerExists: ('a -> bool * ExplainTree)

[<RequireQualifiedAccess>]
module Pred =

    /// Always true.
    let true' : Pred<'a> =
        Leaf("true", (fun _ -> true), (fun _ _ -> "Trivially true."))

    /// Always false.
    let false' : Pred<'a> =
        Leaf("false", (fun _ -> false), (fun _ _ -> "Trivially false."))

    /// A named atomic test with a message that may depend on the input and on pass/fail.
    let leaf (name: string) (test: 'a -> bool) (explain: 'a -> bool -> string) : Pred<'a> =
        Leaf(name, test, explain)

    /// Atomic test like `leaf`, but with no separate display name (explanation text only in formatted output).
    let leaf' (test: 'a -> bool) (explain: 'a -> bool -> string) : Pred<'a> =
        Leaf("", test, explain)

    /// Convenience when you want separate messages for success and failure.
    let leafMsg (name: string) (test: 'a -> bool) (onTrue: 'a -> string) (onFalse: 'a -> string) : Pred<'a> =
        Leaf(name, test, (fun x ok -> if ok then onTrue x else onFalse x))

    /// Like `leafMsg`, but with no separate display name (explanation text only in formatted output).
    let leafMsg' (test: 'a -> bool) (onTrue: 'a -> string) (onFalse: 'a -> string) : Pred<'a> =
        Leaf("", test, (fun x ok -> if ok then onTrue x else onFalse x))

    let conj (left: Pred<'a>) (right: Pred<'a>) : Pred<'a> = And(left, right)
    let disj (left: Pred<'a>) (right: Pred<'a>) : Pred<'a> = Or(left, right)
    let neg (inner: Pred<'a>) : Pred<'a> = Not inner

    /// Logical conjunction of zero or more predicates. Empty list is `true'`.
    let all (items: Pred<'a> list) : Pred<'a> =
        match items with
        | [] -> true'
        | [ single ] -> single
        | h :: t -> List.fold conj h t

    /// Logical disjunction of zero or more predicates. Empty list is `false'`.
    let any (items: Pred<'a> list) : Pred<'a> =
        match items with
        | [] -> false'
        | [ single ] -> single
        | h :: t -> List.fold disj h t

    /// Contravariant map: focus a predicate on a larger value through a projection.
    let contramap (project: 'b -> 'a) (p: Pred<'a>) : Pred<'b> =
        let rec go pred =
            match pred with
            | Leaf(name, test, explain) ->
                Leaf(
                    name,
                    (fun b -> test (project b)),
                    (fun b ok -> explain (project b) ok)
                )
            | And(l, r) -> And(go l, go r)
            | Or(l, r) -> Or(go l, go r)
            | Not inner -> Not(go inner)
            | ForAll(n, ev, el, ee) ->
                ForAll(
                    n,
                    (fun b -> ev (project b)),
                    (fun b -> el (project b)),
                    (fun b -> ee (project b))
                )
            | Exists(n, ev, el, ee) ->
                Exists(
                    n,
                    (fun b -> ev (project b)),
                    (fun b -> el (project b)),
                    (fun b -> ee (project b))
                )

        go p

    /// Evaluate using the same short-circuit semantics as plain F# `&&` and `||`.
    let rec eval (p: Pred<'a>) (x: 'a) : bool =
        match p with
        | Leaf(_, test, _) -> test x
        | And(l, r) -> eval l x && eval r x
        | Or(l, r) -> eval l x || eval r x
        | Not inner -> not (eval inner x)
        | ForAll(_, evalForAll, _, _) -> evalForAll x
        | Exists(_, evalExists, _, _) -> evalExists x

    let private leafNode name passed detail = ExplainTree.Leaf(name, passed, detail)

    let rec private explainLazy (p: Pred<'a>) (x: 'a) : bool * ExplainTree =
        match p with
        | Leaf(name, test, explain) ->
            let ok = test x
            ok, leafNode name ok (explain x ok)
        | And(left, right) ->
            let lOk, lTree = explainLazy left x

            if not lOk then
                false,
                ExplainTree.All(
                    false,
                    [ lTree
                      ExplainTree.Skipped("right", "Not evaluated because the left side of AND failed.") ]
                )
            else
                let rOk, rTree = explainLazy right x
                lOk && rOk, ExplainTree.All(lOk && rOk, [ lTree; rTree ])
        | Or(left, right) ->
            let lOk, lTree = explainLazy left x

            if lOk then
                true,
                ExplainTree.Any(
                    true,
                    [ lTree
                      ExplainTree.Skipped("right", "Not evaluated because the left side of OR succeeded.") ]
                )
            else
                let rOk, rTree = explainLazy right x
                lOk || rOk, ExplainTree.Any(lOk || rOk, [ lTree; rTree ])
        | Not inner ->
            let innerOk, innerTree = explainLazy inner x
            let ok = not innerOk
            ok, ExplainTree.Not(ok, innerTree)
        | ForAll(_, _, explainLazyForAll, _) -> explainLazyForAll x
        | Exists(_, _, explainLazyExists, _) -> explainLazyExists x

    let rec private explainEager (p: Pred<'a>) (x: 'a) : bool * ExplainTree =
        match p with
        | Leaf(name, test, explain) ->
            let ok = test x
            ok, leafNode name ok (explain x ok)
        | And(left, right) ->
            let lOk, lTree = explainEager left x
            let rOk, rTree = explainEager right x
            lOk && rOk, ExplainTree.All(lOk && rOk, [ lTree; rTree ])
        | Or(left, right) ->
            let lOk, lTree = explainEager left x
            let rOk, rTree = explainEager right x
            lOk || rOk, ExplainTree.Any(lOk || rOk, [ lTree; rTree ])
        | Not inner ->
            let innerOk, innerTree = explainEager inner x
            let ok = not innerOk
            ok, ExplainTree.Not(ok, innerTree)
        | ForAll(_, _, _, explainEagerForAll) -> explainEagerForAll x
        | Exists(_, _, _, explainEagerExists) -> explainEagerExists x

    let private skippedItemsFromEnumerator (startItemNumber: int) (message: string) (e: System.Collections.Generic.IEnumerator<_>) =
        let skipped = ResizeArray()
        let mutable j = 0

        while e.MoveNext() do
            skipped.Add(ExplainTree.Skipped($"item {startItemNumber + j}", message))
            j <- j + 1

        Seq.toList skipped

    let private explainLazyForAllItems (name: string) (inner: Pred<'item>) (items: seq<'item>) : bool * ExplainTree =
        use e = items.GetEnumerator()

        let rec loop (itemIndex: int) (successRev: ExplainTree list) =
            if not (e.MoveNext()) then
                true, ExplainTree.ForAll(name, true, List.rev successRev)
            else
                let item = e.Current
                let itemOk, itemTree = explainLazy inner item

                if not itemOk then
                    let skipped =
                        skippedItemsFromEnumerator
                            (itemIndex + 2)
                            "Not evaluated because a previous item failed."
                            e

                    false,
                    ExplainTree.ForAll(name, false, List.rev successRev @ [ itemTree ] @ skipped)
                else
                    loop (itemIndex + 1) (itemTree :: successRev)

        loop 0 []

    let private explainLazyExistsItems (name: string) (inner: Pred<'item>) (items: seq<'item>) : bool * ExplainTree =
        use e = items.GetEnumerator()

        let rec loop (itemIndex: int) (failRev: ExplainTree list) =
            if not (e.MoveNext()) then
                false, ExplainTree.Exists(name, false, List.rev failRev)
            else
                let item = e.Current
                let itemOk, itemTree = explainLazy inner item

                if itemOk then
                    let skipped =
                        skippedItemsFromEnumerator
                            (itemIndex + 2)
                            "Not evaluated because a previous item succeeded."
                            e

                    true,
                    ExplainTree.Exists(name, true, List.rev failRev @ [ itemTree ] @ skipped)
                else
                    loop (itemIndex + 1) (itemTree :: failRev)

        loop 0 []

    /// Every element returned by `getItems` must satisfy `inner`.
    /// Empty sequence is true (vacuous), matching `all []` on the empty conjunction.
    /// Explanations nest: each element yields the same `ExplainTree` shape as `explain inner` on that element.
    let forAll (name: string) (getItems: 'ctx -> #seq<'item>) (inner: Pred<'item>) : Pred<'ctx> =
        let evalForAll (ctx: 'ctx) =
            getItems ctx |> Seq.forall (fun item -> eval inner item)

        let explainLazyForAll (ctx: 'ctx) =
            let items = getItems ctx

            if Seq.isEmpty items then
                true, ExplainTree.ForAll(name, true, [])
            else
                explainLazyForAllItems name inner items

        let explainEagerForAll (ctx: 'ctx) =
            let items = getItems ctx |> Seq.toList

            match items with
            | [] -> true, ExplainTree.ForAll(name, true, [])
            | _ ->
                let pairs = items |> List.map (fun item -> explainEager inner item)
                let oks = pairs |> List.map fst
                let trees = pairs |> List.map snd
                List.forall id oks, ExplainTree.ForAll(name, List.forall id oks, trees)

        ForAll(name, evalForAll, explainLazyForAll, explainEagerForAll)

    /// Like `forAll`, but with no separate quantifier label in formatted output.
    let forAll' (getItems: 'ctx -> #seq<'item>) (inner: Pred<'item>) : Pred<'ctx> =
        forAll "" getItems inner

    /// At least one element returned by `getItems` must satisfy `inner`.
    /// Empty sequence is false (none witness), matching `any []` on the empty disjunction.
    /// Explanations nest: each evaluated element yields the same `ExplainTree` shape as `explain inner` on that element.
    let exists (name: string) (getItems: 'ctx -> #seq<'item>) (inner: Pred<'item>) : Pred<'ctx> =
        let evalExists (ctx: 'ctx) =
            getItems ctx |> Seq.exists (fun item -> eval inner item)

        let explainLazyExists (ctx: 'ctx) =
            let items = getItems ctx

            if Seq.isEmpty items then
                false, ExplainTree.Exists(name, false, [])
            else
                explainLazyExistsItems name inner items

        let explainEagerExists (ctx: 'ctx) =
            let items = getItems ctx |> Seq.toList

            match items with
            | [] -> false, ExplainTree.Exists(name, false, [])
            | _ ->
                let pairs = items |> List.map (fun item -> explainEager inner item)
                let oks = pairs |> List.map fst
                let trees = pairs |> List.map snd
                List.exists id oks, ExplainTree.Exists(name, List.exists id oks, trees)

        Exists(name, evalExists, explainLazyExists, explainEagerExists)

    /// Like `exists`, but with no separate quantifier label in formatted output.
    let exists' (getItems: 'ctx -> #seq<'item>) (inner: Pred<'item>) : Pred<'ctx> =
        exists "" getItems inner

    /// Evaluate and return both the boolean result and an explanation tree.
    let explainWith (mode: ExplainMode) (p: Pred<'a>) (x: 'a) : PropositionResult =
        let ok, tree =
            match mode with
            | Lazy -> explainLazy p x
            | Eager -> explainEager p x

        { Passed = ok; Tree = tree }

    /// Shorthand for lazy explanations (mirrors runtime short-circuiting).
    let explain (p: Pred<'a>) (x: 'a) : PropositionResult = explainWith Lazy p x

    /// Render a human-readable explanation for the given mode.
    let explainText (mode: ExplainMode) (p: Pred<'a>) (x: 'a) : string =
        let r = explainWith mode p x
        r.Tree.Format()

/// Infix operators for composing predicates. Open `FPropose.Operators` to use them.
module Operators =
    let (.&&.) l r = Pred.conj l r
    let (.||.) l r = Pred.disj l r
    let (~~~) p = Pred.neg p
