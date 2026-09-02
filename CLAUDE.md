# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Current state

This repository is an empty scaffold. It contains only `LICENSE` (MIT, HexMaster), a one-line `README.md`, GitHub's `VisualStudio.gitignore`, and an empty `src/` directory. There is no solution, project file, build script, test suite, or CI configuration yet, and the single commit is the initial commit.

There are no Cursor rules, Copilot instructions, or other agent config files in the repo.

## Implications for working here

- The `VisualStudio.gitignore` and the `src/` layout indicate a .NET solution is intended; place projects under `src/` and keep the solution file at the repository root.
- Build, test, and lint commands cannot be documented until a project exists. Once a `.sln`/`.csproj` lands, the usual entry points are `dotnet build`, `dotnet test`, and `dotnet test --filter FullyQualifiedName~<TestName>` for a single test — verify against the actual project before relying on them.
- **Update this file** with the real commands and architecture as soon as code is added; everything above the "Current state" heading is the only part expected to survive unchanged.
