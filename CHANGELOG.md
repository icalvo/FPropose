# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- `Pred.forAll` / `ExplainTree.ForAll`: universal quantification over runtime-sized lists with nested inner explanation trees (lazy short-circuit matches `And`-style skipping).
- `Pred.exists` / `ExplainTree.Exists`: existential quantification (lazy short-circuit matches `Or`-style skipping on first success; empty list is false).

## [0.1.0] - 2026-05-07

### Added

- Initial public release: composable `Pred` types with lazy and eager explanations, operators, and tests.
- `CHANGELOG.md` ([Keep a Changelog](https://keepachangelog.com/en/1.1.0/)) and Cursor **deploy-release** skill (`.cursor/skills/deploy-release/SKILL.md`).
- Sample `.cursor/mcp.json` for the GitHub MCP server (tokens stay in gitignored `mcp.local.env`).

### Changed

- README: canonical GitHub remote, project layout, operator row formatting in the API table.
- `Directory.Build.props`: package and repository URLs point at `icalvo/FPropose`.
- `.gitignore`: ignore `mcp.local.env` for local MCP secrets.
