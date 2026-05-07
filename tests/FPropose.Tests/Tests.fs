module FPropose.Tests

open Xunit
open FPropose
open FPropose.Operators

type Person = { Name: string; Age: int }

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
