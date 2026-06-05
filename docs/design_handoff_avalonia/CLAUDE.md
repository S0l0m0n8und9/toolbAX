# CLAUDE.md — toolBax Avalonia build brief

Keep this in context for the whole build. These are hard constraints, not suggestions.

## Stack

- **UI framework:** Avalonia **12** (`Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`).
  Verify the latest 12.x on first scaffold — pin it.
- **Target framework:** **.NET 10** (`net10.0` for the core/VM libs; `net10.0-windows` only if a
  view-model genuinely needs Windows-only APIs like DPAPI — keep that isolated behind an interface).
- **MVVM:** `CommunityToolkit.Mvvm` (`ObservableObject`, `[ObservableProperty]`, `[RelayCommand]`).
  No code-behind logic beyond view wiring.
- **DataGrid:** `Avalonia.Controls.DataGrid` (separate NuGet package — add it).
- **DI:** `Microsoft.Extensions.DependencyInjection`. Register services + VMs in a composition root.
- **Theme:** `FluentTheme` (dark) with an accent + token `ResourceDictionary` override
  (see `design-tokens.md`). Do **not** restyle controls from scratch — override resources.
- **Testing:** `Avalonia.Headless.XUnit` (+ `xUnit`). Headless UI tests are required and must run
  in CI with **no display server**. This is the reason Avalonia was chosen; do not regress it.

## Platform posture

Windows-first (it's an F&O tool), but **keep the core cross-platform**. Anything Windows-only
(DPAPI secret protection, WebView2 for interactive Data Integrator sign-in) lives behind an
interface (`ISecretProtector`, `IInteractiveAuthBroker`) with a Windows implementation, so the
ViewModels and tests stay platform-neutral and headless-testable.

## Architecture

```
toolBax.sln
├─ toolBax.Core            (net10.0)          models, service interfaces, DTOs — no Avalonia ref
├─ toolBax.App             (net10.0)          Avalonia UI: Views (.axaml) + ViewModels
│   ├─ Views/              one .axaml + .axaml.cs per screen
│   ├─ ViewModels/         one VM per screen/dialog
│   ├─ Controls/           shared templated controls (StatusBadge, etc.)
│   ├─ Themes/             tokens.axaml (ResourceDictionary), accent override
│   └─ Services/           concrete services (gateway client, profile store, auth)
└─ toolBax.App.Tests       (net10.0)          Avalonia.Headless.XUnit tests
```

ViewModels depend only on `toolBax.Core` interfaces. Views bind to ViewModels. Services are the
only place that touch HTTP / MSAL / DPAPI / WebView2.

## Non-negotiables / behaviors that must survive

1. **Confirm-on-mutation.** Every mutating Dual-Write action (start/stop/pause/resume/initial-sync)
   opens a confirm dialog naming the environment + affected maps before any gateway call. No
   silent mutations. Stop and Initial-sync are styled destructive.
2. **Live-environment banner is permanent** on the Operations screen (Fluent `InfoBar`, severity
   Warning, not closable).
3. **Action eligibility.** An action's button is enabled only when ≥1 selected map is in a
   compatible state (start: stopped/idle; stop: running/paused; pause: running; resume: paused;
   initial: any). The button shows the eligible count.
4. **Gateway action codes are fixed by the API:** start=1, stop=4, pause=5, resume=6, initial-sync=8.
5. **Data Integrator token is delegated** (ROPC *or* interactive) — never app-only. The DI tab
   must warn that ROPC fails under MFA (AADSTS50076).
6. **Secrets** are protected at rest (DPAPI on Windows via `ISecretProtector`); never logged.
7. **Keyboard:** Ctrl+K command palette; Alt+<letter> tool shortcuts (Q/O/D/C/M/P/E/H).
8. **Shortcut labels are Windows** (Ctrl/Alt), never macOS glyphs.

## Definition of done per screen

- View renders in the headless harness with no exceptions.
- All interactive state lives in the ViewModel and is unit-testable headless.
- Matches the prototype within Fluent's idiom (use `control-map.md`).
- New behavior (commands, eligibility, polling) has at least one headless test.

## What NOT to do

- Don't hand-roll control templates that `FluentTheme` already provides.
- Don't put async/HTTP/auth in code-behind or VMs directly — go through service interfaces.
- Don't block the UI thread on gateway polling — use `PeriodicTimer`/async, marshal to UI via Dispatcher.
- Don't ship the command palette as a bespoke window if a styled `Popup`/`Flyout` over an overlay suffices.
