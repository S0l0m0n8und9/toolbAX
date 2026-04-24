# Ralph Prompt: continue-progress (cliExec)

A prior Ralph iteration made partial progress. Resume from that durable state and finish the next coherent slice without redoing settled work.

Assume some useful work already landed in the repository. Build on that durable state and avoid redoing completed investigation unless the current files contradict the prior summary.

## Prompt Strategy
- Target: Codex CLI execution via `codex exec`.
- Operate autonomously inside the repository. Do not rely on interactive clarification to make forward progress.
- Keep command usage deterministic and concise because Ralph will persist transcripts, verifier output, and stop signals.
- End with a compact change summary Ralph can pair with verifier evidence.

## Operating Rules
- Read AGENTS.md plus the durable Ralph files before making non-trivial changes.
- Do not invent unsupported IDE APIs or hidden handoff channels.
- Keep architecture thin, deterministic, and file-backed.
- Make the smallest coherent change that materially advances the selected Ralph task.
- Prefer the repository's real validation commands when they exist.
- For normal CLI task execution, do not edit `.ralph/tasks.json` or `.ralph/progress.md` directly; return the structured completion report instead.
- Update durable Ralph progress/tasks only when the prompt explicitly targets backlog replenishment.

## Execution Contract
1. Inspect the workspace facts and selected Ralph task before editing.
2. Execute only the selected task, or explain deterministically why no safe task is available.
3. Implement the smallest coherent improvement that advances the task.
4. Do not edit `.ralph/tasks.json` or `.ralph/progress.md` for normal task execution; Ralph will reconcile selected-task state from your completion report.
5. Run the selected validation command when available and report the concrete result.
6. End with a fenced `json` completion report block for the selected task using `selectedTaskId`, `requestedStatus`, optional `progressNote`, optional `blocker`, optional `validationRan`, and optional `needsHumanReview`.

## Final Response Contract
- Changed files.
- Validation results.
- Assumptions or blockers.
- Known limitations or follow-up work.
- End with a fenced `json` completion report block for the selected task.

## Template Selection
The previous iteration recorded partial progress, so the next prompt should continue from that durable state.

## Preflight Snapshot
- Ready: yes
- Summary: Preflight ready: Selected task T1. Validation dotnet build .\FoToolbox.sln -c Release. Executable token confirmed. Active claims default: T1 - Testify configuration settings panel @ 2026-04-24T18:06:50.930Z (fresh). Task graph: ok | Claim graph: ok | Workspace/runtime: 1 info | Codex adapter: 1 warning, 1 info | Validation/verifier: 1 info | Agent Health: 2 info
- codexAdapter warning: Configured IDE command strategy is unavailable. Missing VS Code commands: claude.openSidebar, claude.newChat. Clipboard handoff can still fall back.

## Objective Snapshot
# toolbAX — Product Requirements Document

## Overview

toolbAX is a Windows desktop application for Microsoft Dynamics 365 Finance & Operations (F&O) administrators and developers, inspired by XrmToolBox. It provides a plugin-based host that surfaces operational tools — OData query building, dual-write map browsing, entity inspection, and automated integration validation — against live F&O and Dataverse environments. The application targets .NET 8 Desktop Runtime on Windows 10/11, authenticates via MSAL (Entra ID / service principal), and stores environment profiles locally using SQLite with DPAPI-encrypted secrets.

## Goals

- Deliver a reliable, self-updating tool that reduces manual effort for F&O data engineers and integration specialists.
[trimmed for size]

## Repo Context
- Workspace: toolbAX
- Workspace root: c:\Users\ben.jones\Repos\toolbAX
- Inspected root: c:\Users\ben.jones\Repos\toolbAX
- Execution root: c:\Users\ben.jones\Repos\toolbAX
- Verifier root: c:\Users\ben.jones\Repos\toolbAX
- Root selection: Using the workspace root because it already exposes shallow repo markers.
- Root policy: Inspect, execute, and verify at the workspace root while storing Ralph artifacts under .ralph there.
- Manifests: global.json, FoToolbox.sln
- Package managers: dotnet
- Package manager indicators: global.json, FoToolbox.sln
- Test roots: tests
- Validation commands: dotnet test, dotnet test .\FoToolbox.sln -c Release --no-build --collect:"XPlat Code Coverage"
- Test signals: dotnet test is likely available., README.md may define the canonical build/test commands., Detected test roots: tests.

## Ralph Runtime Context
- Prompt target: cliExec
- Current iteration number: 2
- Next iteration recorded in state: 2
- Last prompt kind: bootstrap
- Last prompt path: .ralph/prompts/bootstrap-001.prompt.md
- Last run: succeeded at iteration 1
- Last iteration outcome: partial_progress at iteration 1
- Last iteration summary: Selected T1: Testify configuration settings panel | Execution: succeeded | Verification: passed | Outcome: partial_progress | Backlog remaining: 6

## Task Plan
- Reasoning: This task matters because it removes a manual JSON-edit workflow from Testify and makes per-map configuration a first-class part of the DualWriteMapBrowser UI, which improves usability and reduces configuration errors. The key challenge is wiring a new WPF settings surface into the existing toolbar and map-selection flow while keeping edits correctly bound to the currently selected map and persisted through the existing TestifyConfigurationStore without introducing schema or reload regressions.
- Approach: Add a toolbar entry that opens a WPF per-map settings panel bound to the selected map’s Testify configuration model, then save and reload those values through the existing configuration store path.
- Steps: Inspect DualWriteMapBrowser, the existing Testify toolbar commands, and TestifyConfigurationStore to find the current entry points for toolbar actions, selected-map context, and config load/save. → Identify the in-memory model that represents per-map settings and confirm where omitCreateFields, preferredCreateValues, cePollTimeoutMinutes, and allowPartialEnumCoverage are currently read from configuration. → Design and add a WPF panel or dialog within DualWriteMapBrowser that exposes editable controls for those four per-map settings and binds them to the currently selected map. → Wire the panel launch into the existing Testify toolbar so users can reach it directly from the browser without opening the JSON file. → Implement save/update logic so panel edits write back to the selected map configuration and persist through TestifyConfigurationStore using the same serialization path as normal configuration saves.
- Risks: The selected-map context may not be cleanly exposed to the toolbar command, making it easy to save settings against the wrong map.; Existing configuration serialization may treat missing versus empty values differently, especially for omitCreateFields and preferredCreateValues.; If the current UI layer is not MVVM-friendly, adding a panel quickly can lead to brittle code-behind and harder-to-test persistence behavior.
- Suggested validation: dotnet test

## Task Focus
- Backlog counts: todo 5, in_progress 1, blocked 0, done 0
- Next actionable task: T1 (in_progress)
- Selected task id: T1
- Title: Testify configuration settings panel
- Status: in_progress
- Parent task: none
- Dependencies: none
- Direct children: none
- Remaining descendants: none
- Task validation hint: dotnet build .\FoToolbox.sln -c Release
- Effective validation command: dotnet build .\FoToolbox.sln -c Release
- Validation command normalized from: none
- Notes: none
- Blocker: none
- Acceptance criteria: (1) Per-map settings (omitCreateFields, preferredCreateValues, cePollTimeoutMinutes, allowPartialEnumCoverage) are editable in a WPF panel inside DualWriteMapBrowser (2) Changes persist to TestifyConfigurationStore and reload correctly on next launch (3) Panel is reachable from the existing Testify toolbar without opening the JSON file
- Constraints: none
- Relevant files: plugins/DualWriteMapBrowser/TestifyConfigurationStore.cs, plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.Testify.cs
- Task-local code context: plugins/DualWriteMapBrowser/TestifyConfigurationStore.cs, plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.Testify.cs

## Recent Progress
# Progress
- Ralph workspace initialized.
- Use this file for durable progress notes between fresh Codex runs.

## Prior Iteration Evidence
- Prior iteration: 1
- Prior outcome classification: partial_progress
- Prior execution / verification: succeeded / passed
- Prior summary: Selected T1: Testify configuration settings panel | Execution: succeeded | Verification: passed | Outcome: partial_progress | Backlog remaining: 6
- Additional prior-context signals omitted: 3.
