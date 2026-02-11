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
