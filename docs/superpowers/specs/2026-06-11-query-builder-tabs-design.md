# Query Builder — tabbed workspace

**Date:** 2026-06-11
**Status:** Approved (design)
**Scope:** Avalonia app only (`avalonia/toolBax.App`). Not the WPF host.

## Problem

The Avalonia Query Builder crams every concern into one fixed-row vertical stack in the
right-hand content pane. The field picker (`$select`) is hard-capped at `MaxHeight=170`
(≈4–5 visible rows) and the filter builder at `MaxHeight=240`, while the results `DataGrid`
takes the remaining `*` row even when empty. On a data entity with 300+ fields the field
list shows only a handful at a time, and the layout wastes vertical space (an empty results
grid dominating before any run). The experience is squished and hard to navigate.

## Goal

Reorganize the workspace into tabs so each concern (Fields, Filter, Joins, Results) gets the
full workspace height, removing the arbitrary height caps. This is a **view-layer refactor**
with small ViewModel additions — no behavioral change to querying, auth, OData, metadata, or
the filter-tree model.

## Non-goals

- No changes to `FoToolbox.Core` / `ToolBax.Core` services, the OData client, or metadata.
- No changes to the filter-tree model (`QueryFilterGroup` / `QueryFilterCondition` / context).
- No new query capabilities — same `$select` / `$filter` / `$orderby` / `$expand` / paging.

## Design

The left entity column (`Grid.Column=0`) is unchanged. The right content pane
(`Grid.Column=1`) changes from `RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,*,28"` to
`RowDefinitions="Auto,Auto,Auto,*,Auto"`:

| Row | Content | Pinned |
|---|---|---|
| 0 | Title — entity name, `company-aware` badge, `pk:` | pinned |
| 1 | Query URL + actions (`Copy URL`, `Run`, `Copy CSV`, `Save CSV…`, `Export all…`) | pinned |
| 2 | Query options (`$orderby`, `top`, `skip`, `$count`, `cross-company`, `company`) | pinned |
| 3 | **TabControl** (fills `*`) | — |
| 4 | Status bar (status text, success badge, `Load more`) | pinned |

Pinning the URL/actions and options rows means the user can tweak fields/filter on any tab
and `Run` without leaving it.

### Tabs

Order and zero-based index: **Fields (0) · Filter (1) · Joins (2) · Results (3)**. `Fields`
is selected on load. Each tab's content stretches to fill the tab, so its lists are bounded
by the tab (≈ window height) and scroll internally. The `MaxHeight=170` (field list) and
`MaxHeight=240` (filter scroller) caps are removed.

- **Fields** — the field search box + `Select all` / `Clear` + `FieldSelectionLabel`, over a
  full-height virtualized `FieldList` (`x:Name="FieldList"` retained). When `!HasFields`, the
  existing `NotCachedMessage` hint is shown instead.
- **Filter** — the `Builder` / `Raw $filter` segmented toggle, the recursive builder tree (or
  the raw `$filter` box + its override warning), and the `EFFECTIVE` filter preview.
- **Joins** — the `$expand` search box + a full-height virtualized navigation checklist,
  promoted out of the old `Expander`. When `!HasNavigations`, an empty-state hint is shown
  ("This entity has no navigation properties to expand.").
- **Results** — the dynamic `DataGrid` (`x:Name="ResultsGrid"` retained) + an empty-state hint
  ("Run a query to preview rows.") shown when `!HasRun`.

Tab headers use a custom header (a `TextBlock` bound to a derived string) to show live counts:

- `Fields · {selected}/{total}` — plain `Fields` when `!HasFields`.
- `Filter · {N}` (builder, N conditions) / `Filter · raw` (raw mode) / `Filter` (no conditions).
- `Joins · {selected}/{total}` — plain `Joins` when the entity has no navigations.
- `Results · {rowCount}` — plain `Results` before the first run.

### ViewModel additions

Additions only; no existing members change semantics.

1. `SelectedTabIndex` (`int`, `[ObservableProperty]`, two-way bound to `TabControl.SelectedIndex`).
   - `Run` and `Load more` set it to `ResultsTabIndex` (a `const int = 3`) so the user lands on
     Results as rows populate.
   - **Export all does NOT switch tabs** — it writes a CSV file and leaves the in-grid results
     unchanged; switching to Results would show an unrelated/stale grid. (This is a deliberate
     deviation from a literal "Export-all switches too" reading; it is the correct behavior.)
2. Four computed header properties — `FieldsTabHeader`, `FilterTabHeader`, `JoinsTabHeader`,
   `ResultsTabHeader` — each raising `PropertyChanged` at the *existing* refresh points:
   - `FieldsTabHeader`: alongside `FieldSelectionLabel` (chip change, bulk op, `LoadFields`).
   - `JoinsTabHeader`: alongside `JoinsHeader` (chip change, `LoadNavigations`).
   - `FilterTabHeader`: alongside `FilterSummary` (in `OnFilterTreeChanged`).
   - `ResultsTabHeader`: via `[NotifyPropertyChangedFor]` on `RowCount` and `HasRun`.

### Code-behind: lazy tab realization vs. dynamic grid columns

`QueryBuilderView.axaml.cs` builds the Results columns dynamically via
`FindControl<DataGrid>("ResultsGrid")`, triggered on `ResultColumns` change and on `Loaded`.
A `TabControl` realizes only the *selected* tab's content, so the `ResultsGrid` does not exist
in the visual tree until the Results tab is shown — `FindControl` returns null and columns are
never built if the rebuild fires while another tab is active.

**Fix:** in addition to the existing `ResultColumns`-change trigger, rebuild columns from the
grid's `AttachedToVisualTree` event (declared in XAML on the `ResultsGrid`). `RebuildColumns`
stays null-safe (no-op when the grid isn't realized). With auto-switch-to-Results on `Run`, the
grid attaches when the tab is first shown and columns build correctly. `RebuildColumns` is
idempotent (`Columns.Clear()` first), so the extra trigger is safe.

## Testing

### Existing tests that must change (they encode the old squished layout)

- `QueryBuilderViewRenderTests.Field_list_stays_height_bounded_so_large_entities_cannot_balloon_it`
  asserts `FieldList.MaxHeight` is *finite* — the opposite of the new design, where the field
  list fills its tab and scrolls. Repoint it to the new invariant: the field list virtualizes
  and stays constrained to its container (e.g. `fieldList.Bounds.Height <= window.Height`)
  rather than ballooning. The regression it guards (a many-field entity pushing other sections
  off-screen) is now structurally impossible because the list is alone in its tab.
- `QueryBuilderViewRenderTests.Adding_a_condition_materialises_the_builder_row_editors`
  counts `ComboBox`es in the visual tree, but the filter builder now lives in a non-default
  tab (Filter, index 1). Update it to set `SelectedTabIndex = 1`, `RunJobs()`, then add the
  condition and count — so the condition's editors are realized.

### Existing tests expected to keep passing

- `Renders_entity_list_run_button_and_query_url` — entity `ListBox` and pinned `Run` button
  remain present.
- `Running_a_query_builds_result_grid_columns` — with auto-switch to Results, the grid is
  realized after `RunCommand` + `RunJobs()`; assert `grid.Columns.Count == ResultColumns.Count`
  still holds (this test now also implicitly covers the `AttachedToVisualTree` rebuild path).

### New tests

- VM (`QueryBuilderViewModelTests`):
  - `SelectedTabIndex == ResultsTabIndex` after `Run`.
  - `SelectedTabIndex` unchanged by `Export all`.
  - Tab-header strings format correctly across states: not-cached / N-selected fields;
    no-conditions / N-conditions / raw filter; no-navigations / N-selected joins;
    before-run / after-run Results.
- Render (`QueryBuilderViewRenderTests`):
  - The four `TabItem`s render with no binding errors.
  - Switching to the Results tab after a run builds the grid columns (covers lazy realization).

## Rollout

One focused PR off `main` (branch `feat/avalonia-query-builder-tabs`), mirroring the existing
`TabControl` usage in `ProfilesView.axaml` and `DualWriteMapView.axaml`.
