module FPropose.Tests

open Xunit
open FPropose
open FPropose.Operators

type Person = { Name: string; Age: int }

type Widget = { Tags: string list }

[<Fact>]
let ``leafMsg' formats without name prefix`` () =
    let p =
        Pred.leafMsg' (fun (s: string) -> s.Length > 0) (fun _ -> "non-empty") (fun _ -> "empty string")

    let text = Pred.explainText ExplainMode.Lazy p ""

    let lines =
        text.Split([| '\r'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries)

    Assert.Equal("[✗] empty string", lines[0])

[<Fact>]
let ``forAll' uses empty quantifier label in tree and format`` () =
    let nonEmpty =
        Pred.leafMsg' (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.forAll' (fun w -> w.Tags) nonEmpty
    let w = { Tags = [ "a"; "" ] }
    let r = Pred.explain p w
    Assert.False r.Passed

    match r.Tree with
    | ExplainTree.ForAll("", false, _) -> ()
    | other -> Assert.Fail(sprintf "unexpected tree: %A" other)

    let text = Pred.explainText ExplainMode.Lazy p w

    let lines =
        text.Split([| '\r'; '\n' |], System.StringSplitOptions.RemoveEmptyEntries)

    Assert.Equal("[✗] FOR ALL", lines[0])

[<Fact>]
let ``eval matches plain semantics for and or not`` () =
    let p =
        Pred.conj (Pred.leafMsg "nonempty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty"))
            (Pred.leafMsg "short" (fun s -> s.Length <= 3) (fun _ -> "ok") (fun _ -> "too long"))

    Assert.True(Pred.eval p "ab")
    Assert.False(Pred.eval p "")
    Assert.False(Pred.eval p "abcd")

[<Fact>]
let ``lazy explain skips right branch of failing and`` () =
    let left = Pred.leafMsg "left" (fun (n: int) -> n > 0) (fun _ -> "positive") (fun _ -> "non-positive")

    let right =
        Pred.leafMsg "right" (fun _ -> failwith "should not run") (fun _ -> "") (fun _ -> "")

    let p = Pred.conj left right
    let r = Pred.explain p 0
    Assert.False r.Passed

    match r.Tree with
    | ExplainTree.All(false, [ _; ExplainTree.Skipped(_, _) ]) -> ()
    | other -> Assert.Fail(sprintf "unexpected tree: %A" other)

[<Fact>]
let ``eager explain evaluates both sides`` () =
    let seen = System.Collections.Generic.List<string>()

    let mark name pred =
        Pred.leafMsg name
            (fun (n: int) ->
                seen.Add name |> ignore
                pred n)
            (fun _ -> $"{name} ok")
            (fun _ -> $"{name} fail")

    let p = Pred.conj (mark "a" (fun n -> n > 0)) (mark "b" (fun n -> n < 10))
    let r = Pred.explainWith ExplainMode.Eager p 0
    Assert.False r.Passed
    Assert.Equal(2, seen.Count)
    Assert.Contains("a", seen)
    Assert.Contains("b", seen)

[<Fact>]
let ``contramap focuses predicate`` () =
    let adult =
        Pred.leafMsg "adult" (fun (a: int) -> a >= 18) (fun _ -> "adult") (fun _ -> "minor")

    let p = Pred.contramap (fun person -> person.Age) adult
    Assert.True(Pred.eval p { Name = "x"; Age = 20 })

[<Fact>]
let ``operators compose`` () =
    let even =
        Pred.leafMsg "even" (fun (n: int) -> n % 2 = 0) (fun _ -> "even") (fun _ -> "odd")

    let nonzero = ~~~(Pred.leafMsg "zero" ((=) 0) (fun _ -> "zero") (fun _ -> "nonzero"))
    let p = even .&&. nonzero
    Assert.True(Pred.eval p 2)

[<Fact>]
let ``all and any`` () =
    let nums =
        [ Pred.leafMsg "pos" (fun n -> n > 0) (fun _ -> "ok") (fun _ -> "fail")
          Pred.leafMsg "lt10" (fun n -> n < 10) (fun _ -> "ok") (fun _ -> "fail") ]

    Assert.True(Pred.eval (Pred.all nums) 5)
    Assert.False(Pred.eval (Pred.all nums) -1)
    Assert.False(Pred.eval (Pred.any []) 42)
    Assert.True(Pred.eval (Pred.all []) 42)

[<Fact>]
let ``forAll empty list is vacuously true`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.forAll "tags" (fun w -> w.Tags) nonEmpty
    let w = { Tags = [] }
    Assert.True(Pred.eval p w)

    let r = Pred.explain p w

    match r.Tree with
    | ExplainTree.ForAll("tags", true, []) -> ()
    | other -> Assert.Fail(sprintf "unexpected tree: %A" other)

[<Fact>]
let ``forAll eval matches forall on items`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.forAll "tags" (fun w -> w.Tags) nonEmpty
    Assert.True(Pred.eval p { Tags = [ "a"; "b" ] })
    Assert.False(Pred.eval p { Tags = [ "a"; "" ] })

[<Fact>]
let ``forAll lazy explain skips inner after first failure`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.forAll "tags" (fun w -> w.Tags) nonEmpty
    let w = { Tags = [ "ok"; ""; "never" ] }
    let r = Pred.explain p w
    Assert.False r.Passed

    match r.Tree with
    | ExplainTree.ForAll("tags", false, items) ->
        Assert.Equal(3, items.Length)

        match items[2] with
        | ExplainTree.Skipped _ -> ()
        | other -> Assert.Fail(sprintf "expected third item skipped (inner not run on 'never'), got %A" other)
    | other ->
        Assert.Fail(sprintf "unexpected tree: %A" other)

[<Fact>]
let ``forAll eager explain evaluates every item`` () =
    let seen = System.Collections.Generic.List<string>()

    let mark s =
        Pred.leafMsg "m"
            (fun (x: string) ->
                seen.Add x |> ignore
                x.Length > 0)
            (fun _ -> "ok")
            (fun _ -> "empty")

    let p = Pred.forAll "tags" (fun w -> w.Tags) (mark "inner")
    let w = { Tags = [ "a"; ""; "c" ] }
    let r = Pred.explainWith ExplainMode.Eager p w
    Assert.False r.Passed
    Assert.Equal(3, seen.Count)

[<Fact>]
let ``forAll composes with contramap`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p =
        Pred.contramap (fun (pair: string list * int) -> fst pair) (Pred.forAll "tags" id nonEmpty)

    Assert.True(Pred.eval p ([ "x" ], 0))
    Assert.False(Pred.eval p ([ "" ], 0))

[<Fact>]
let ``exists empty list is vacuously false`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.exists "tags" (fun w -> w.Tags) nonEmpty
    let w = { Tags = [] }
    Assert.False(Pred.eval p w)

    let r = Pred.explain p w

    match r.Tree with
    | ExplainTree.Exists("tags", false, []) -> ()
    | other -> Assert.Fail(sprintf "unexpected tree: %A" other)

[<Fact>]
let ``exists eval matches exists on items`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.exists "tags" (fun w -> w.Tags) nonEmpty
    Assert.True(Pred.eval p { Tags = [ ""; "a" ] })
    Assert.False(Pred.eval p { Tags = [ ""; "" ] })

[<Fact>]
let ``exists lazy explain skips inner after first success`` () =
    let nonEmpty =
        Pred.leafMsg "nonEmpty" (fun (s: string) -> s.Length > 0) (fun _ -> "ok") (fun _ -> "empty")

    let p = Pred.exists "tags" (fun w -> w.Tags) nonEmpty
    let w = { Tags = [ ""; "ok"; "never" ] }
    let r = Pred.explain p w
    Assert.True r.Passed

    match r.Tree with
    | ExplainTree.Exists("tags", true, items) ->
        Assert.Equal(3, items.Length)

        match items[2] with
        | ExplainTree.Skipped _ -> ()
        | other -> Assert.Fail(sprintf "expected third item skipped, got %A" other)
    | other ->
        Assert.Fail(sprintf "unexpected tree: %A" other)

[<Fact>]
let ``exists eager explain evaluates every item`` () =
    let seen = System.Collections.Generic.List<string>()

    let mark =
        Pred.leafMsg "m"
            (fun (x: string) ->
                seen.Add x |> ignore
                x.Length > 0)
            (fun _ -> "ok")
            (fun _ -> "empty")

    let p = Pred.exists "tags" (fun w -> w.Tags) mark
    let w = { Tags = [ ""; ""; "c" ] }
    let r = Pred.explainWith ExplainMode.Eager p w
    Assert.True r.Passed
    Assert.Equal(3, seen.Count)
