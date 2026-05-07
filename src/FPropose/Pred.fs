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

    /// Convenience when you want separate messages for success and failure.
    let leafMsg (name: string) (test: 'a -> bool) (onTrue: 'a -> string) (onFalse: 'a -> string) : Pred<'a> =
        Leaf(name, test, (fun x ok -> if ok then onTrue x else onFalse x))

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

        go p

    /// Evaluate using the same short-circuit semantics as plain F# `&&` and `||`.
    let rec eval (p: Pred<'a>) (x: 'a) : bool =
        match p with
        | Leaf(_, test, _) -> test x
        | And(l, r) -> eval l x && eval r x
        | Or(l, r) -> eval l x || eval r x
        | Not inner -> not (eval inner x)
        | ForAll(_, evalForAll, _, _) -> evalForAll x

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

    /// Every element returned by `getItems` must satisfy `inner`.
    /// Empty sequence is true (vacuous), matching `all []` on the empty conjunction.
    /// Explanations nest: each element yields the same `ExplainTree` shape as `explain inner` on that element.
    let forAll (name: string) (getItems: 'ctx -> 'item list) (inner: Pred<'item>) : Pred<'ctx> =
        let evalForAll (ctx: 'ctx) =
            getItems ctx |> List.forall (fun item -> eval inner item)

        let explainLazyForAll (ctx: 'ctx) =
            match getItems ctx with
            | [] -> true, ExplainTree.ForAll(name, true, [])
            | items ->
                let rec loop (itemIndex: int) (successRev: ExplainTree list) (remaining: 'item list) =
                    match remaining with
                    | [] -> true, ExplainTree.ForAll(name, true, List.rev successRev)
                    | item :: rest ->
                        let itemOk, itemTree = explainLazy inner item

                        if not itemOk then
                            let skipped =
                                rest
                                |> List.mapi (fun j _ ->
                                    ExplainTree.Skipped(
                                        $"item {itemIndex + j + 2}",
                                        "Not evaluated because a previous item failed."
                                    ))

                            false,
                            ExplainTree.ForAll(name, false, List.rev successRev @ [ itemTree ] @ skipped)
                        else
                            loop (itemIndex + 1) (itemTree :: successRev) rest

                loop 0 [] items

        let explainEagerForAll (ctx: 'ctx) =
            match getItems ctx with
            | [] -> true, ExplainTree.ForAll(name, true, [])
            | items ->
                let pairs = items |> List.map (fun item -> explainEager inner item)
                let oks = pairs |> List.map fst
                let trees = pairs |> List.map snd
                List.forall id oks, ExplainTree.ForAll(name, List.forall id oks, trees)

        ForAll(name, evalForAll, explainLazyForAll, explainEagerForAll)

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
