# FPropose

Composable predicates for F# that return a boolean **and** explain why that result holds for a specific value. Explanations can follow normal short-circuiting (`Lazy`) or evaluate every sub-predicate for auditing (`Eager`).

## Install

```bash
dotnet add package FPropose
```

## Quick example

```fsharp
open FPropose
open FPropose.Operators

let canVote =
    Pred.leafMsg "age" (fun p -> p.Age >= 18)
        (fun _ -> "Meets minimum age.")
        (fun p -> $"Age {p.Age} is below 18.")

let registered =
    Pred.leafMsg "registered" (fun p -> p.Registered)
        (fun _ -> "Account is registered.")
        (fun _ -> "Account is not registered.")

let p = canVote .&&. registered
let person = {| Name = "Ada"; Age = 17; Registered = false |}

if Pred.eval p person then
    printfn "OK"
else
    printfn "%s" (Pred.explainText ExplainMode.Lazy p person)
```

`ExplainTree` captures structure; call `.Format()` or `ExplainTree.format` for plain text.

## API overview

| Construct | Use |
|-----------|-----|
| `Pred.leaf name test explain` | Atomic test; `explain` receives the value and the pass/fail flag. |
| `Pred.leafMsg name test onTrue onFalse` | Convenience when messages differ only by outcome. |
| `Pred.conj` / `Pred.disj` / `Pred.neg` | AND, OR, NOT. |
| `Pred.all` / `Pred.any` | Fold over lists (`all []` is always true; `any []` always false). |
| `Pred.contramap` | Focus a predicate on part of a larger value. |
| `Pred.eval` | Boolean result with standard short-circuiting for AND and OR. |
| `Pred.explain` | `PropositionResult` using lazy explanation. |
| `Pred.explainWith ExplainMode.Eager` | Full sub-tree regardless of short-circuit. |
| `Pred.explainText` | Same as explain, rendered as a string. |
| `FPropose.Operators` | `.&&.`, `.||.`, `~~~` |

## Developing locally

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
git clone https://github.com/icalvo/FPropose.git
cd FPropose
dotnet restore
dotnet build
dotnet test
```

### Open as a workspace

In Cursor or VS Code, use **File → Open Folder** and select the `FPropose` directory (the folder that contains `FPropose.sln`).

### Connect this repo to GitHub

After you create an empty GitHub repository:

```bash
git remote add origin https://github.com/icalvo/FPropose.git
git branch -M main   # optional: rename default branch to main
git push -u origin main
```

Update `PackageProjectUrl` and `RepositoryUrl` in `Directory.Build.props` to match your real URL before publishing.

### Project layout

- `src/FPropose` — library (`Explain.fs`, `Pred.fs`)
- `tests/FPropose.Tests` — xUnit tests

## Packaging locally

```bash
dotnet pack src/FPropose/FPropose.fsproj -c Release -o ./artifacts -p:PackageVersion=0.1.0-local
```

Outputs `.nupkg` (and symbols) under `./artifacts`.

## Publishing to NuGet.org (CI)

### One-time setup

1. Use this repository (`icalvo/FPropose`); `Directory.Build.props` and this README already point at it.
2. [Create an API key](https://www.nuget.org/account/apikeys) on NuGet.org with **Push** scope for package id `FPropose`.
3. In the GitHub repo, add a secret named **`NUGET_API_KEY`** containing that key (**Settings → Secrets and variables → Actions**).

### Publish when you cut a GitHub Release

1. Update `Version` / release notes as needed.
2. Create a **Release** in GitHub and publish it. Use a tag like `v0.2.0` (the workflow strips a leading `v` for the package version).
3. The **Publish to NuGet** workflow packs `FPropose` and runs `dotnet nuget push` to `https://api.nuget.org/v3/index.json`.

### Publish manually from Actions

1. Go to **Actions → Publish to NuGet → Run workflow**.
2. Enter a **SemVer** version (e.g. `0.2.0`).

### After the first publish

Confirm the package appears on [NuGet Gallery](https://www.nuget.org/packages/FPropose/). If the id is taken, change `<PackageId>` in `src/FPropose/FPropose.fsproj` and the secret scope to match.

## License

MIT — see [LICENSE](LICENSE).
