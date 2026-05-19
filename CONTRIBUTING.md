# Contributing

Thanks for contributing to FO Toolbox.

## Development Setup

1. Install the .NET SDK required by `global.json`.
2. Restore/build/test:
   ```powershell
   dotnet restore .\FoToolbox.sln
   dotnet build .\FoToolbox.sln -c Release
   dotnet test .\FoToolbox.sln -c Release
   ```

## Ralph Task Authoring

When creating or editing `.ralph/tasks.json`:

- Set each task `validation` to a repo-local wrapper script with no arguments, for example:
  - `.ralph\validate-build.cmd`
  - `.ralph\validate-test-testifyconfiguration.cmd`
- Put `dotnet` arguments, filters, and working-directory setup inside the wrapper script.
- Do not use `cd <path> && <command>` in `validation`.
- Do not use `%USERPROFILE%`, `$env:USERPROFILE`, or literal `C:\...` paths in `validation`.

Rationale: the verifier executes validation as a command token and can mis-handle spaces, shell syntax, drive-letter colons, and environment-variable expansion. Single-token wrappers keep task metadata stable.

## Pull Requests

Please keep PRs focused and include:
- Problem statement
- What changed
- How you validated it (tests/manual steps)

If behavior changes, include tests where practical.

## Coding Notes

- Follow existing project conventions and nullable annotations.
- Do not commit secrets, local databases, installer payloads, or IDE state.
- Keep `.gitignore` updated for new generated outputs.

## Commit Hygiene

- Prefer small, reviewable commits.
- Use clear commit messages that describe user impact.

## Conduct

By participating, you agree to follow `CODE_OF_CONDUCT.md`.
