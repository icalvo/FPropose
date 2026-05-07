---
name: deploy-release
description: >-
  Cuts a SemVer release for this repo: discovers the latest published version,
  reviews changes since then, proposes the next version (or validates a
  user-supplied one), updates Keep a Changelog–style CHANGELOG.md and
  FPropose.fsproj, tags v-prefix, pushes, publishes a GitHub Release to
  trigger NuGet. Use when shipping a release, cutting a version, publishing to
  NuGet, or choosing the next SemVer.
---

# Deploy release (FPropose)

Follow this order so the tag points at the versioned sources and **Publish to NuGet** runs from `release: published` (see `.github/workflows/publish.yml`).

## Preconditions

- Default branch is green (CI passes).
- `NUGET_API_KEY` is set in GitHub repo secrets if NuGet publish should succeed.
- **SemVer** `X.Y.Z` only for this package (no `v` in `<Version>`; use `vX.Y.Z` for git tag and GitHub Release tag). Prerelease labels (e.g. `-rc.1`) are optional; if unused, stick to numeric triples.

## Checklist

```text
- [ ] Latest published version identified (below)
- [ ] Changes since that version reviewed; next version suggested (or user override validated)
- [ ] User confirmed X.Y.Z
- [ ] CHANGELOG: move Unreleased → X.Y.Z (YYYY-MM-DD)
- [ ] FPropose.fsproj: <Version>X.Y.Z</Version>
- [ ] Commit: chore(release): vX.Y.Z (or release: X.Y.Z)
- [ ] Tag: vX.Y.Z on that commit
- [ ] Push branch + tag
- [ ] GitHub Release: tag vX.Y.Z, title + body from changelog (then Publish)
```

## 0. Latest published version, changes, and version choice

**Baseline (latest published)** — use the first that exists:

1. **GitHub Releases (published only)** — canonical for what already shipped through this repo’s pipeline. Run `gh release list --exclude-drafts -L 1 --json tagName` (or GitHub API/MCP equivalent). Use that row’s `tagName`; it must look like `vX.Y.Z`. Strip a leading `v` to get `latest`. If the list is empty, fall back to step 2.
2. If there are **no published releases**: from `git fetch origin --tags`, take the newest **remote** tag matching `v*` by SemVer ordering (see below), strip `v`, call that `latest`.
3. If there are **no such tags**: treat `latest` as `0.0.0` for comparison only and note that this may be a first release; still align with `CHANGELOG.md` / `<Version>` on the branch if they document an existing line.

**Changes since baseline** — after `git fetch origin` and identifying ref `v{latest}` (or the release tag commit if you need the exact SHA):

- `git log v{latest}..HEAD --oneline` (if ref missing, use `git log --oneline` for a bounded window or compare to merge-base with default branch).
- Optionally `git diff v{latest}..HEAD --stat` (or scope to `src/FPropose` for API-focused judgment).

Summarize for the user: themes (fixes vs features vs breaking / public API changes).

**Suggest next version** `next` (SemVer bump from `latest`):

| Signal | Bump |
|--------|------|
| Breaking change to public API or behavior users rely on | **MAJOR** `latest+1.0.0` |
| New backward-compatible capability | **MINOR** `latest+0.1.0` |
| Fixes, docs, internal-only, CI, tests only | **PATCH** `latest+0.0.1` |

If multiple apply, choose the **most severe** bump. If `latest` was `0.0.0` / first release, align `next` with the first real version (often `0.1.0` or `1.0.0`) and match `CHANGELOG` / product expectations.

Present: **latest**, **summary of changes**, **suggested `next`**, and one-line **rationale**. Ask the user to **confirm** the suggested `X.Y.Z` or to **provide** another.

**User-supplied version** `candidate`:

- Normalize: trim; strip a leading `v` if present for `<Version>` / comparison.
- **Reject** (with explanation) if `candidate` is **not strictly greater** than `latest` under SemVer 2 ordering of the `X.Y.Z` core (numeric segments). Examples: `0.1.0` is not greater than `0.1.0`; `0.1.0` is less than `0.2.0`; `1.0.0` is greater than `0.9.9`.
- If the project later adopts prereleases, `1.0.0-rc.2` vs `1.0.0-rc.1` follows SemVer pre-release rules; if only release tags are `X.Y.Z`, keep guidance simple.

**Comparison helper** (when unsure): among two numeric triples `A` and `B`, compare major, then minor, then patch. Shell with GNU coreutils: `printf '%s\n' latest candidate | sort -V | tail -1` must print `candidate` for `candidate` to be strictly newer.

After the user confirms `X.Y.Z`, continue with sections 1–7 using that value.

## 1. Changelog ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/))

- File: `CHANGELOG.md` at repo root.
- If missing, create it with `## [Unreleased]`, first heading `## [0.1.0] - YYYY-MM-DD`, and sections `### Added` / `### Changed` / `### Fixed` / `### Removed` as needed (empty sections may be omitted).
- For the new release: under `## [Unreleased]`, ensure bullet items describe user-facing changes. Rename the section to `## [X.Y.Z] - YYYY-MM-DD` (ISO date).
- Open a new `## [Unreleased]` at the top (empty or with placeholder) after publishing.

Release notes for GitHub must be the **same markdown** as the `## [X.Y.Z]` section body (from the first `###` through the end of that section), not the entire file.

## 2. Version bump

- Edit `src/FPropose/FPropose.fsproj`: set `<Version>X.Y.Z</Version>` (and thus `PackageVersion` unless overridden) to match the changelog heading.

## 3. Commit and tag

- Single commit containing `CHANGELOG.md` + `FPropose.fsproj` (and any other versioned release artifacts).
- Create an **annotated** tag: `git tag -a vX.Y.Z -m "vX.Y.Z"` (lightweight tags are acceptable if the team prefers; stay consistent).
- Do not retag an existing version; if the tag exists remotely, stop and reconcile.

## 4. Push

- `git push origin <default-branch>` then `git push origin vX.Y.Z`.
- Default branch is `master` or `main` per the repo; CI listens to both.

## 5. GitHub Release (triggers NuGet workflow)

Publishing the release fires `release: published` and the workflow strips a leading `v` from the tag for `dotnet pack`.

- **Preferred (CLI)**: after push, from repo root with `gh` authenticated:

  ```bash
  gh release create "vX.Y.Z" --title "FPropose vX.Y.Z" --notes-file /path/to/extracted-notes.md
  ```

  Build `extracted-notes.md` from the changelog section for `X.Y.Z` only.

- **Alternative**: GitHub web UI → Releases → Draft a new release → choose tag `vX.Y.Z` → paste the same notes → **Publish release**.

Do **not** rely on `gh release create` with `--generate-notes` as the sole source of truth if the team treats `CHANGELOG.md` as canonical—either mirror the changelog section or edit the generated notes to match.

## 6. Publish workflow without a Release (optional)

If NuGet must run without a GitHub Release (rare): Actions → **Publish to NuGet** → Run workflow → set input `version` to `X.Y.Z` (no `v`). This does not update the changelog or tags; use only for operational exceptions.

## 7. Verify

- Actions: **Publish to NuGet** completed for the release.
- GitHub Release shows the intended notes and tag.
- NuGet package version matches `X.Y.Z` (when configured).

## Project-specific reference

| Item | Location |
|------|----------|
| Package / assembly version | `src/FPropose/FPropose.fsproj` → `<Version>` |
| Changelog | `CHANGELOG.md` |
| CI | `.github/workflows/ci.yml` |
| NuGet publish | `.github/workflows/publish.yml` (`release` + `workflow_dispatch`) |
