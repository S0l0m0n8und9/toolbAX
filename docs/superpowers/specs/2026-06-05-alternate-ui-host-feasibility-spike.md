# Alternate UI host — feasibility findings (#35)

Status: **feasibility analysis / spike scoping** (no production code). Phase 4 of the modernization
track; depends on the #33 SDK-decoupling design. WPF stays the default host regardless of outcome.

## Question

Is it practical to host toolbAX's plugins under a non-WPF UI framework (e.g. to reach the web, or
cross-platform desktop)? What would a "small separate host project" actually need, and is the cost
justified?

## What we have (three reuse tiers)

Measured against the current `main`:

| Tier | Contents | Portability |
|---|---|---|
| **Reusable as-is** | `FoToolbox.Core` (auth/MSAL, OData client + metadata, catalog, profiles, secret vault, dual-write gateway) | ✅ Confirmed **zero** WPF references. Moves to any .NET host unchanged. |
| **Reusable after abstraction** | Plugin + host **view-models** (the logic) | ⚠️ Mostly portable, but they call WPF UI services directly (see leakage below). |
| **Must be reimplemented per host** | **20 XAML views** (12 plugin + 8 host) + WPF themes/resource dictionaries + shell chrome (custom window, nav rail, tab bar, `ContentControl`) + the WebView2 dual-write sign-in | ❌ WPF-specific. No automatic port. |

### View-model WPF leakage (call sites to abstract)

The view-models are *not* UI-agnostic today — they reach into WPF directly:

| WPF API | Sites | Abstraction needed |
|---|---|---|
| `MessageBox` | 32 | `IDialogService` (confirm/alert) |
| `CollectionViewSource` / `ICollectionView` | 18 | a host-neutral filtered/sorted collection-view, or move filtering into the VM |
| `Application.Current` | 5 | injected app/services accessor |
| `Clipboard` | 4 | `IClipboardService` |
| `Dispatcher` | 1 | `IUiDispatcher` (post-to-UI-thread) |

≈ 60 call sites across the plugins. None are deep — they're leaf calls that map cleanly onto small
injected services — but every one must be routed through an abstraction before a VM compiles without
`PresentationFramework`.

## Candidate hosts

| Host | Distance from WPF | Notes |
|---|---|---|
| **Avalonia** (recommended) | Smallest | XAML + MVVM, very close to WPF; views port with find/replace-scale edits (namespaces, a few control names); reuses VMs (post-abstraction) + Core; cross-platform desktop; WebView2 → Avalonia `WebView`. Best effort/return for this codebase. |
| **Uno / WinUI** | Small–medium | WinUI XAML cross-platform; viable but heavier tooling; closer to UWP idioms. |
| **Blazor / web** | Largest | Full view rewrite in Razor; unlocks web/`claude.ai/code`-style delivery, but VMs need an even cleaner separation and async/streaming UI rethink. Highest cost, highest strategic upside. |
| **MAUI** | Medium | Desktop+mobile; XAML-ish but less proven for dense desktop tooling UIs. |

## The cost is in the views, not the contract

The #33 SDK decoupling (a UI-agnostic `IPluginView`) is **necessary but small** — it's the seam that
*lets* a non-WPF host load a plugin at all. The actual migration cost is dominated by:

1. reimplementing 20 XAML views (+ themes + shell) in the target framework, and
2. abstracting the ≈60 VM→WPF leak sites.

So "decouple the SDK" alone does **not** make the app portable; it's ~5–10% of the journey.

## Recommended spike (if pursued)

A minimal proof, not a migration:

1. Land #33 Phase 1 (UI-agnostic `IPluginView` + `FoToolbox.SDK.Wpf`).
2. Introduce the small VM UI-service abstractions (`IDialogService`, `IClipboardService`,
   `IUiDispatcher`) with WPF implementations in the current host (no behaviour change).
3. New `FoToolbox.Host.Avalonia` project that references `FoToolbox.Core` + the decoupled SDK, defines
   an Avalonia `IPluginView` adapter, and hosts **one** simple plugin (HelloPlugin or QueryBuilder)
   with its view reimplemented in Avalonia XAML, against the **same** view-model.

**What it proves:** that Core + a VM + the plugin-load model run unmodified under a non-WPF host, and
gives a real per-plugin view-port effort number to extrapolate the full migration.

## Verdict

**Feasible, but not cheap, and not urgent.** The architecture (Core/VM/View with a plugin contract) is
sound for it, and Avalonia is the natural target. The blocker is volume (20 views + ~60 VM leak sites),
not any hard impossibility. Recommendation: keep WPF as the default; do #33 Phase 1 and the VM
UI-service abstractions opportunistically (they improve testability regardless), and only build the
Avalonia one-plugin spike when an alternate host becomes a real product goal.

## Risks

- Don't start view porting before #33 + VM abstractions — porting views that still reference WPF
  services just moves the coupling.
- A second host doubles UI maintenance unless one is clearly primary; keep WPF authoritative until a
  spike proves the economics.
