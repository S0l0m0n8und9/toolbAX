# Query Builder Tabbed Workspace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reorganize the Avalonia Query Builder's right-hand pane into Fields / Filter / Joins / Results tabs so each concern gets full workspace height, fixing the squished 4-row field picker on 300+ field entities.

**Architecture:** View-layer refactor of `QueryBuilderView.axaml` (fixed-row stack → pinned header/footer + a `TabControl`) plus additive changes to `QueryBuilderViewModel.cs` (a `SelectedTabIndex` for auto-switch-to-Results, four computed tab-header strings with live counts). The code-behind's dynamic result-grid column builder is made robust to the `TabControl`'s lazy tab realization. No changes to Core, the OData client, metadata, or the filter-tree model.

**Tech Stack:** Avalonia 12 / .NET 10, CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`, `[NotifyPropertyChangedFor]`), xUnit + `Avalonia.Headless.XUnit` (`[AvaloniaFact]`) for offscreen render tests.

**Solution / projects:**
- App: `avalonia/toolBax.App` (view `Views/QueryBuilderView.axaml` + code-behind `.axaml.cs`, VM `ViewModels/QueryBuilderViewModel.cs`)
- Tests: `avalonia/toolBax.App.Tests` (`QueryBuilderViewModelTests.cs`, `QueryBuilderViewRenderTests.cs`)
- Solution file: `avalonia/toolBax.slnx`

**Commands (run from repo root):**
- Build: `dotnet build avalonia/toolBax.slnx -c Debug`
- All Query Builder VM tests: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests"`
- All Query Builder render tests: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewRenderTests"`
- A single test: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests.Run_switches_to_the_results_tab"`

---

## File Structure

- **Modify** `avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs` — add `SelectedTabIndex` + `ResultsTabIndex`, auto-switch in `Run`/`LoadMore`, four computed tab-header properties + their change notifications. (Tasks 1–2.)
- **Modify** `avalonia/toolBax.App/Views/QueryBuilderView.axaml` — replace the fixed-row content grid with pinned header/footer + a 4-tab `TabControl`; remove the `MaxHeight` caps; add empty-state hints; bind tab headers; wire `ResultsGrid` `AttachedToVisualTree`. (Task 3.)
- **Modify** `avalonia/toolBax.App/Views/QueryBuilderView.axaml.cs` — add the `OnResultsGridAttached` handler that rebuilds columns when the lazily-realized Results tab attaches. (Task 3.)
- **Modify** `avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs` — add VM tests for auto-switch + tab headers. (Tasks 1–2.)
- **Modify** `avalonia/toolBax.App.Tests/QueryBuilderViewRenderTests.cs` — repoint the two layout-coupled render tests; add a four-tabs render test. (Tasks 3–4.)

---

## Task 1: VM — `SelectedTabIndex` + auto-switch to Results on Run/Load more

**Files:**
- Modify: `avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs`
- Test: `avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these three tests to `QueryBuilderViewModelTests.cs` (anywhere inside the class; place them after `Run_is_disabled_while_a_run_is_in_flight`):

```csharp
[Fact]
public async Task Run_switches_to_the_results_tab()
{
    var vm = MakeVm();
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
    Assert.Equal(0, vm.SelectedTabIndex); // Fields is the default tab

    await vm.RunCommand.ExecuteAsync(null);

    Assert.Equal(QueryBuilderViewModel.ResultsTabIndex, vm.SelectedTabIndex);
}

[Fact]
public async Task Load_more_switches_to_the_results_tab()
{
    const string page1 = "{\"@odata.nextLink\":\"https://x/data/E?$skiptoken=p2\",\"value\":[{\"CustomerAccount\":\"US-1\"}]}";
    const string page2 = "{\"value\":[{\"CustomerAccount\":\"US-2\"}]}";
    var client = new PagingODataClient(
        new ODataResponse(200, "OK", page1, 5),
        new ODataResponse(200, "OK", page2, 5));
    var vm = new QueryBuilderViewModel(new FakeMetadataService(), client);
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
    await vm.RunCommand.ExecuteAsync(null);
    vm.SelectedTabIndex = 0; // pretend the user navigated back to Fields

    await vm.LoadMoreCommand.ExecuteAsync(null);

    Assert.Equal(QueryBuilderViewModel.ResultsTabIndex, vm.SelectedTabIndex);
}

[Fact]
public async Task Export_all_does_not_change_the_active_tab()
{
    var fileSave = new FakeFileSaveService("C:/tmp/x.csv");
    var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient(), fileSave: fileSave);
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
    Assert.Equal(0, vm.SelectedTabIndex);

    await vm.ExportAllCsvCommand.ExecuteAsync(null);

    Assert.Equal(0, vm.SelectedTabIndex); // export writes a file; it must not jump to Results
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests.Run_switches_to_the_results_tab|FullyQualifiedName~QueryBuilderViewModelTests.Load_more_switches_to_the_results_tab|FullyQualifiedName~QueryBuilderViewModelTests.Export_all_does_not_change_the_active_tab"`
Expected: FAIL to compile / fail — `SelectedTabIndex` and `ResultsTabIndex` don't exist yet.

- [ ] **Step 3: Add `SelectedTabIndex` + `ResultsTabIndex` to the VM**

In `QueryBuilderViewModel.cs`, add the constant next to the existing `MaxExportPages` constant (around line 31):

```csharp
    // Hard cap on pages an "export all" will follow, so a misbehaving nextLink can't loop forever.
    private const int MaxExportPages = 500;

    /// <summary>Zero-based index of the Results tab — Fields(0) · Filter(1) · Joins(2) · Results(3).</summary>
    public const int ResultsTabIndex = 3;
```

Add the observable property near the other UI-state `[ObservableProperty]` fields (e.g. just below `_hasRun` around line 192):

```csharp
    /// <summary>Active workspace tab (two-way bound to the TabControl). Run / Load more jump to Results.</summary>
    [ObservableProperty]
    private int _selectedTabIndex;
```

- [ ] **Step 4: Auto-switch in `Run` and `LoadMore`**

In `Run` (the `[RelayCommand]` method), set the tab right after the busy/status setup. Change:

```csharp
        IsBusy = true;
        StatusText = "Running…";
        try
```

to:

```csharp
        IsBusy = true;
        StatusText = "Running…";
        SelectedTabIndex = ResultsTabIndex; // land on Results so rows are visible as they load
        try
```

In `LoadMore`, change:

```csharp
        IsBusy = true;
        StatusText = "Loading more…";
        try
```

to:

```csharp
        IsBusy = true;
        StatusText = "Loading more…";
        SelectedTabIndex = ResultsTabIndex; // Load more can be triggered from any tab; show the grid
        try
```

Leave `ExportAllCsv` untouched — it writes a file and must not switch tabs.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests.Run_switches_to_the_results_tab|FullyQualifiedName~QueryBuilderViewModelTests.Load_more_switches_to_the_results_tab|FullyQualifiedName~QueryBuilderViewModelTests.Export_all_does_not_change_the_active_tab"`
Expected: PASS (3 passed).

- [ ] **Step 6: Commit**

```bash
git add avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs
git commit -m "feat(avalonia): query builder auto-switches to Results on Run/Load more"
```

---

## Task 2: VM — live tab-header strings with counts

**Files:**
- Modify: `avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs`
- Test: `avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add these tests to `QueryBuilderViewModelTests.cs`:

```csharp
[Fact]
public void Fields_tab_header_tracks_selection_and_falls_back_when_uncached()
{
    var vm = MakeVm();
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");

    // PK fields are selected by default (dataAreaId + CustomerAccount).
    Assert.Equal($"Fields · {vm.Fields.Count(f => f.IsSelected)}/{vm.Fields.Count}", vm.FieldsTabHeader);

    vm.ClearFieldsCommand.Execute(null);
    Assert.Equal($"Fields · 0/{vm.Fields.Count}", vm.FieldsTabHeader);

    vm.SelectAllFieldsCommand.Execute(null);
    Assert.Equal($"Fields · {vm.Fields.Count}/{vm.Fields.Count}", vm.FieldsTabHeader);

    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no cached fields
    Assert.False(vm.HasFields);
    Assert.Equal("Fields", vm.FieldsTabHeader);
}

[Fact]
public void Filter_tab_header_tracks_condition_count_and_raw_mode()
{
    var vm = MakeVm();
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
    Assert.Equal("Filter", vm.FilterTabHeader); // no conditions yet

    AddCondition(vm);
    Assert.Equal("Filter · 1", vm.FilterTabHeader);

    vm.IsRawFilterMode = true;
    Assert.Equal("Filter · raw", vm.FilterTabHeader);
}

[Fact]
public void Joins_tab_header_tracks_selection_and_falls_back_when_none()
{
    var vm = MakeVm();
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
    Assert.Equal($"Joins · 0/{vm.Navigations.Count}", vm.JoinsTabHeader);

    vm.Navigations.Single(n => n.Name == "PrimaryContact").IsSelected = true;
    Assert.Equal($"Joins · 1/{vm.Navigations.Count}", vm.JoinsTabHeader);

    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "VendorsV2"); // no navigations
    Assert.False(vm.HasNavigations);
    Assert.Equal("Joins", vm.JoinsTabHeader);
}

[Fact]
public async Task Results_tab_header_shows_row_count_after_a_run()
{
    var vm = MakeVm();
    vm.SelectedEntity = vm.Entities.Single(e => e.Name == "CustomersV3");
    Assert.Equal("Results", vm.ResultsTabHeader); // before any run

    await vm.RunCommand.ExecuteAsync(null);

    Assert.Equal($"Results · {vm.RowCount}", vm.ResultsTabHeader);
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests.Fields_tab_header|FullyQualifiedName~QueryBuilderViewModelTests.Filter_tab_header|FullyQualifiedName~QueryBuilderViewModelTests.Joins_tab_header|FullyQualifiedName~QueryBuilderViewModelTests.Results_tab_header"`
Expected: FAIL to compile — the four header properties don't exist yet.

- [ ] **Step 3: Add the four computed header properties**

In `QueryBuilderViewModel.cs`, add these near the existing `FieldSelectionLabel` / `EntityCountLabel` computed properties (around line 236):

```csharp
    /// <summary>Fields tab header: "Fields · {selected}/{total}" (plain "Fields" when not cached).</summary>
    public string FieldsTabHeader =>
        HasFields ? $"Fields · {Fields.Count(f => f.IsSelected)}/{Fields.Count}" : "Fields";

    /// <summary>Filter tab header: "Filter · {N}" (builder), "Filter · raw" (raw mode), or "Filter".</summary>
    public string FilterTabHeader => IsRawFilterMode
        ? "Filter · raw"
        : FilterRoot.ConditionCount > 0 ? $"Filter · {FilterRoot.ConditionCount}" : "Filter";

    /// <summary>Joins tab header: "Joins · {selected}/{total}" (plain "Joins" when the entity has none).</summary>
    public string JoinsTabHeader =>
        HasNavigations ? $"Joins · {Navigations.Count(n => n.IsSelected)}/{Navigations.Count}" : "Joins";

    /// <summary>Results tab header: "Results · {rowCount}" after a run, otherwise plain "Results".</summary>
    public string ResultsTabHeader => HasRun ? $"Results · {RowCount}" : "Results";
```

- [ ] **Step 4: Wire change notifications from `[ObservableProperty]` sources**

Add `[NotifyPropertyChangedFor(...)]` attributes to the four backing fields so the headers refresh when their inputs change.

`_hasFields` (around line 91) — add the notify:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FieldsTabHeader))]
    private bool _hasFields;
```

`_hasNavigations` (around line 57) — add the notify:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(JoinsTabHeader))]
    private bool _hasNavigations;
```

`_isRawFilterMode` (around line 143) — add the notify alongside the existing `IsBuilderMode`:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBuilderMode))]
    [NotifyPropertyChangedFor(nameof(FilterTabHeader))]
    private bool _isRawFilterMode;
```

`_hasRun` (around line 192) — add the notify:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsTabHeader))]
    private bool _hasRun;
```

`_rowCount` (around line 203) — add the notify:

```csharp
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ResultsTabHeader))]
    private int _rowCount;
```

- [ ] **Step 5: Wire change notifications from the count-driven (non-observable) sources**

These counts change via collection-item `PropertyChanged` and bulk operations, so raise the headers manually wherever the existing sibling labels are raised.

In `OnChipChanged` (the `IsSelected` branch, around lines 507–510), add the two tab headers next to the existing label raises:

```csharp
            UpdateQueryUrl();
            ExportAllCsvCommand.NotifyCanExecuteChanged(); // $select drives CanExportAllCsv
            // The label counters are independent (fields vs navigations); refresh both — a flip is
            // cheap and only one will actually change.
            OnPropertyChanged(nameof(FieldSelectionLabel));
            OnPropertyChanged(nameof(JoinsHeader));
            OnPropertyChanged(nameof(FieldsTabHeader));
            OnPropertyChanged(nameof(JoinsTabHeader));
```

In `SetFieldsSelection` (around line 548), add the field header after the label raise:

```csharp
        UpdateQueryUrl();
        ExportAllCsvCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(FieldSelectionLabel));
        OnPropertyChanged(nameof(FieldsTabHeader));
```

In `LoadFields` (around line 465), add the field header after the label raise:

```csharp
        RefreshFieldFilter();
        UpdateQueryUrl();
        OnPropertyChanged(nameof(FieldSelectionLabel));
        OnPropertyChanged(nameof(FieldsTabHeader));
```

In `LoadNavigations` (around line 491), add the joins header after the existing `JoinsHeader` raise:

```csharp
        RefreshNavigationFilter();
        OnPropertyChanged(nameof(JoinsHeader));
        OnPropertyChanged(nameof(JoinsTabHeader));
```

In `OnFilterTreeChanged` (around lines 382–386), add the filter header next to `FilterSummary`:

```csharp
        OnPropertyChanged(nameof(BuilderFilter));
        OnPropertyChanged(nameof(EffectiveFilter));
        OnPropertyChanged(nameof(HasEffectiveFilter));
        OnPropertyChanged(nameof(FilterSummary));
        OnPropertyChanged(nameof(FilterTabHeader));
        UpdateQueryUrl();
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests.Fields_tab_header|FullyQualifiedName~QueryBuilderViewModelTests.Filter_tab_header|FullyQualifiedName~QueryBuilderViewModelTests.Joins_tab_header|FullyQualifiedName~QueryBuilderViewModelTests.Results_tab_header"`
Expected: PASS (4 passed).

- [ ] **Step 7: Run the whole VM suite to confirm no regressions**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewModelTests"`
Expected: PASS (all existing + 7 new tests).

- [ ] **Step 8: Commit**

```bash
git add avalonia/toolBax.App/ViewModels/QueryBuilderViewModel.cs avalonia/toolBax.App.Tests/QueryBuilderViewModelTests.cs
git commit -m "feat(avalonia): query builder tab headers show live counts"
```

---

## Task 3: View — tabbed workspace layout + robust column rebuild

This task changes the view and, in the same commit, repoints the two existing render tests that encode the old single-stack layout (they would otherwise fail to compile/pass against the new structure).

**Files:**
- Modify: `avalonia/toolBax.App/Views/QueryBuilderView.axaml`
- Modify: `avalonia/toolBax.App/Views/QueryBuilderView.axaml.cs`
- Modify (existing render tests): `avalonia/toolBax.App.Tests/QueryBuilderViewRenderTests.cs`

- [ ] **Step 1: Replace the content pane with pinned header/footer + a TabControl**

In `QueryBuilderView.axaml`, replace the entire `<!-- Content -->` grid — the block that begins with `<!-- Content -->` / `<Grid Grid.Column="1" Margin="24,16,24,0" RowDefinitions="Auto,Auto,Auto,Auto,Auto,Auto,*,28">` (line 121) and ends at its matching `</Grid>` (line 308) — with the following. The `UserControl.Styles`, `UserControl.DataTemplates`, the outer `<Grid ColumnDefinitions="260,*">`, and the entity-list `Border` (Grid.Column 0) are unchanged.

```xml
    <!-- Content -->
    <Grid Grid.Column="1" Margin="24,16,24,0" RowDefinitions="Auto,Auto,Auto,*,Auto">

      <!-- Title row (pinned) -->
      <StackPanel Grid.Row="0" Orientation="Horizontal" Spacing="10" Margin="0,0,0,12">
        <TextBlock Text="{Binding SelectedEntity.Name}" FontSize="24" FontWeight="SemiBold"
                   FontFamily="{StaticResource MonoFontFamily}" Foreground="{StaticResource Text0Brush}"
                   VerticalAlignment="Center" />
        <Border IsVisible="{Binding SelectedEntity.CompanyAware}"
                Background="{StaticResource Layer3Brush}" CornerRadius="999" Padding="8,2"
                VerticalAlignment="Center">
          <TextBlock Text="company-aware" FontSize="11" Foreground="{StaticResource Text2Brush}" />
        </Border>
        <TextBlock Text="{Binding SelectedEntity.Pk, StringFormat='pk: {0}'}" FontSize="11"
                   FontFamily="{StaticResource MonoFontFamily}" Foreground="{StaticResource Text3Brush}"
                   VerticalAlignment="Center" />
      </StackPanel>

      <!-- Query URL + actions (pinned) -->
      <Grid Grid.Row="1" ColumnDefinitions="*,Auto" ColumnSpacing="8">
        <TextBox Grid.Column="0" Text="{Binding QueryUrl}" IsReadOnly="True"
                 FontFamily="{StaticResource MonoFontFamily}" Foreground="{StaticResource Text1Brush}" />
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8">
          <Button Content="Copy URL" Command="{Binding CopyUrlCommand}"
                  ToolTip.Tip="Copy the query URL to the clipboard" />
          <Button Content="Run" Command="{Binding RunCommand}"
                  Background="{StaticResource AccentBrush}" Foreground="{StaticResource OnAccentBrush}" />
          <Button Content="Copy CSV" Command="{Binding ExportCsvCommand}"
                  ToolTip.Tip="Copy the loaded rows as CSV to the clipboard" />
          <Button Content="Save CSV…" Command="{Binding ExportCsvFileCommand}"
                  ToolTip.Tip="Save the loaded rows to a .csv file" />
          <Button Content="Export all…" Command="{Binding ExportAllCsvCommand}"
                  ToolTip.Tip="Page through every matching row and save to a .csv file" />
        </StackPanel>
      </Grid>

      <!-- Query options ($orderby / paging / cross-company + company) (pinned) -->
      <Grid Grid.Row="2" ColumnDefinitions="*,Auto" ColumnSpacing="8" Margin="0,8,0,0">
        <TextBox Grid.Column="0" Text="{Binding OrderBy, Mode=TwoWay}"
                 PlaceholderText="$orderby — e.g. Name desc"
                 FontFamily="{StaticResource MonoFontFamily}" />
        <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="8" VerticalAlignment="Center">
          <TextBlock Text="top" FontSize="11" Foreground="{StaticResource Text3Brush}" VerticalAlignment="Center" />
          <TextBox Width="56" Text="{Binding Top, Mode=TwoWay}" ToolTip.Tip="$top" />
          <TextBlock Text="skip" FontSize="11" Foreground="{StaticResource Text3Brush}" VerticalAlignment="Center" />
          <TextBox Width="56" Text="{Binding Skip, Mode=TwoWay}" ToolTip.Tip="$skip" />
          <CheckBox Content="$count" IsChecked="{Binding Count, Mode=TwoWay}" />
          <CheckBox Content="cross-company" IsChecked="{Binding CrossCompany, Mode=TwoWay}" />
          <TextBlock Text="company" FontSize="11" Foreground="{StaticResource Text3Brush}" VerticalAlignment="Center" />
          <TextBox Width="72" Text="{Binding Company, Mode=TwoWay}" IsEnabled="{Binding !CrossCompany}"
                   FontFamily="{StaticResource MonoFontFamily}" ToolTip.Tip="dataAreaId used when cross-company is off" />
        </StackPanel>
      </Grid>

      <!-- Workspace tabs: Fields · Filter · Joins · Results -->
      <TabControl Grid.Row="3" Margin="0,14,0,0" SelectedIndex="{Binding SelectedTabIndex, Mode=TwoWay}">

        <!-- Fields ($select) -->
        <TabItem>
          <TabItem.Header><TextBlock Text="{Binding FieldsTabHeader}" /></TabItem.Header>
          <Grid Margin="0,8,0,0">
            <DockPanel IsVisible="{Binding HasFields}">
              <Grid DockPanel.Dock="Top" ColumnDefinitions="260,Auto,Auto,*,Auto" ColumnSpacing="8" Margin="0,0,0,8">
                <TextBox Grid.Column="0" Text="{Binding FieldSearch, Mode=TwoWay}" PlaceholderText="Search fields…" />
                <Button Grid.Column="1" Content="Select all" Command="{Binding SelectAllFieldsCommand}"
                        FontSize="11" Padding="8,3" ToolTip.Tip="Select all currently-visible fields" />
                <Button Grid.Column="2" Content="Clear" Command="{Binding ClearFieldsCommand}"
                        FontSize="11" Padding="8,3" ToolTip.Tip="Deselect all fields" />
                <TextBlock Grid.Column="4" Text="{Binding FieldSelectionLabel}" FontSize="11"
                           Foreground="{StaticResource Text3Brush}" VerticalAlignment="Center" />
              </Grid>
              <ListBox x:Name="FieldList" ItemsSource="{Binding FilteredFields}"
                       Background="Transparent" BorderThickness="0">
                <ListBox.ItemTemplate>
                  <DataTemplate x:DataType="vm:FieldChipViewModel">
                    <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" Padding="6,0"
                              HorizontalAlignment="Stretch" HorizontalContentAlignment="Left">
                      <StackPanel Orientation="Horizontal" Spacing="6">
                        <TextBlock Text="{Binding Name}" FontSize="12.5"
                                   FontFamily="{StaticResource MonoFontFamily}" VerticalAlignment="Center" />
                        <TextBlock Text="PK" IsVisible="{Binding IsKey}" FontSize="11"
                                   Foreground="{StaticResource WarnBrush}" VerticalAlignment="Center" />
                        <TextBlock Text="REQ" IsVisible="{Binding ShowReq}" FontSize="11"
                                   Foreground="{StaticResource InfoBrush}" VerticalAlignment="Center" />
                        <TextBlock Text="{Binding TypeDisplay}" IsVisible="{Binding HasTypeDisplay}" FontSize="11"
                                   FontFamily="{StaticResource MonoFontFamily}" Foreground="{StaticResource Text3Brush}"
                                   VerticalAlignment="Center" />
                      </StackPanel>
                    </CheckBox>
                  </DataTemplate>
                </ListBox.ItemTemplate>
              </ListBox>
            </DockPanel>
            <Border IsVisible="{Binding !HasFields}" VerticalAlignment="Top"
                    Background="{StaticResource InfoBgBrush}" BorderBrush="{StaticResource InfoBrush}"
                    BorderThickness="1" CornerRadius="8" Padding="12,8">
              <TextBlock Text="{Binding NotCachedMessage}" TextWrapping="Wrap"
                         Foreground="{StaticResource Text1Brush}" />
            </Border>
          </Grid>
        </TabItem>

        <!-- Filter (builder / raw $filter) -->
        <TabItem>
          <TabItem.Header><TextBlock Text="{Binding FilterTabHeader}" /></TabItem.Header>
          <DockPanel Margin="0,8,0,0">
            <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto" ColumnSpacing="10" Margin="0,0,0,8">
              <TextBlock Grid.Column="0" Text="{Binding FilterSummary}" FontSize="11.5"
                         Foreground="{StaticResource Text3Brush}" VerticalAlignment="Center" />
              <Border Grid.Column="2" BorderBrush="{StaticResource Stroke2Brush}" BorderThickness="1"
                      CornerRadius="6" ClipToBounds="True">
                <StackPanel Orientation="Horizontal">
                  <Button Classes="seg" Classes.active="{Binding IsBuilderMode}" Content="Builder"
                          Command="{Binding SetFilterModeCommand}" CommandParameter="builder" />
                  <Button Classes="seg" Classes.active="{Binding IsRawFilterMode}" Content="Raw $filter"
                          Command="{Binding SetFilterModeCommand}" CommandParameter="raw" />
                </StackPanel>
              </Border>
            </Grid>
            <Grid DockPanel.Dock="Bottom" ColumnDefinitions="Auto,*" ColumnSpacing="8" Margin="0,8,0,0">
              <TextBlock Grid.Column="0" Text="EFFECTIVE" FontSize="11" FontFamily="{StaticResource MonoFontFamily}"
                         Foreground="{StaticResource Text3Brush}" VerticalAlignment="Top" Margin="0,7,0,0" />
              <Border Grid.Column="1" Background="{StaticResource AppBackgroundBrush}"
                      BorderBrush="{StaticResource DividerBrush}" BorderThickness="1" CornerRadius="6" Padding="10,7">
                <Panel>
                  <TextBlock Text="{Binding EffectiveFilter}" IsVisible="{Binding HasEffectiveFilter}"
                             FontFamily="{StaticResource MonoFontFamily}" FontSize="12" TextWrapping="Wrap"
                             Foreground="{StaticResource OkBrush}" />
                  <TextBlock Text="no filter" IsVisible="{Binding !HasEffectiveFilter}"
                             FontFamily="{StaticResource MonoFontFamily}" FontSize="12"
                             Foreground="{StaticResource Text3Brush}" />
                </Panel>
              </Border>
            </Grid>
            <ScrollViewer>
              <StackPanel Spacing="8">
                <Border IsVisible="{Binding IsBuilderMode}" Background="{StaticResource Layer1Brush}"
                        BorderBrush="{StaticResource StrokeBrush}" BorderThickness="1" CornerRadius="8" Padding="12">
                  <ContentControl Content="{Binding FilterRoot}" />
                </Border>
                <StackPanel IsVisible="{Binding IsRawFilterMode}" Spacing="8">
                  <TextBox Text="{Binding Filter, Mode=TwoWay}" AcceptsReturn="True" MinHeight="76" TextWrapping="Wrap"
                           FontFamily="{StaticResource MonoFontFamily}" FontSize="12.5"
                           PlaceholderText="e.g. BlockedForInvoice eq 'No' and CreditLimit gt 10000" />
                  <Border Background="{StaticResource WarnBgBrush}" BorderBrush="{StaticResource WarnBrush}"
                          BorderThickness="1" CornerRadius="8" Padding="12,8">
                    <TextBlock TextWrapping="Wrap" Foreground="{StaticResource Text1Brush}" FontSize="12"
                               Text="Raw $filter overrides the builder — builder conditions are ignored while this box has text." />
                  </Border>
                </StackPanel>
              </StackPanel>
            </ScrollViewer>
          </DockPanel>
        </TabItem>

        <!-- Joins ($expand) -->
        <TabItem>
          <TabItem.Header><TextBlock Text="{Binding JoinsTabHeader}" /></TabItem.Header>
          <Grid Margin="0,8,0,0">
            <DockPanel IsVisible="{Binding HasNavigations}">
              <TextBox DockPanel.Dock="Top" Margin="0,0,0,8" Text="{Binding JoinSearch, Mode=TwoWay}"
                       PlaceholderText="Search joins…" />
              <ListBox ItemsSource="{Binding FilteredNavigations}"
                       Background="Transparent" BorderThickness="0">
                <ListBox.ItemTemplate>
                  <DataTemplate x:DataType="vm:FieldChipViewModel">
                    <CheckBox IsChecked="{Binding IsSelected, Mode=TwoWay}" Padding="6,0"
                              HorizontalAlignment="Stretch" HorizontalContentAlignment="Left">
                      <TextBlock Text="{Binding Name}" FontSize="{StaticResource FontSizeCaption}"
                                 FontFamily="{StaticResource MonoFontFamily}" VerticalAlignment="Center" />
                    </CheckBox>
                  </DataTemplate>
                </ListBox.ItemTemplate>
              </ListBox>
            </DockPanel>
            <TextBlock IsVisible="{Binding !HasNavigations}" VerticalAlignment="Top" TextWrapping="Wrap"
                       Text="This entity has no navigation properties to expand."
                       Foreground="{StaticResource Text3Brush}" FontSize="12" />
          </Grid>
        </TabItem>

        <!-- Results (columns built in code-behind from ResultColumns) -->
        <TabItem>
          <TabItem.Header><TextBlock Text="{Binding ResultsTabHeader}" /></TabItem.Header>
          <Grid Margin="0,8,0,0">
            <DataGrid x:Name="ResultsGrid" IsVisible="{Binding HasRun}"
                      AttachedToVisualTree="OnResultsGridAttached"
                      ItemsSource="{Binding ResultRows}" IsReadOnly="True"
                      AutoGenerateColumns="False" GridLinesVisibility="Horizontal"
                      Background="{StaticResource Layer1Brush}" />
            <TextBlock IsVisible="{Binding !HasRun}" VerticalAlignment="Center" HorizontalAlignment="Center"
                       Text="Run a query to preview rows." FontSize="13"
                       Foreground="{StaticResource Text3Brush}" />
          </Grid>
        </TabItem>

      </TabControl>

      <!-- Status bar (pinned) -->
      <Border Grid.Row="4" BorderBrush="{StaticResource StrokeBrush}" BorderThickness="0,1,0,0">
        <StackPanel Orientation="Horizontal" Spacing="12" VerticalAlignment="Center" Margin="0,6,0,6">
          <TextBlock Text="{Binding StatusText}" FontSize="11.5"
                     Foreground="{StaticResource Text2Brush}" VerticalAlignment="Center" />
          <Border IsVisible="{Binding RunSucceeded}" Background="{StaticResource OkBgBrush}"
                  CornerRadius="999" Padding="8,1" VerticalAlignment="Center">
            <TextBlock Text="{Binding StatusBadge}" FontSize="11" Foreground="{StaticResource OkBrush}" />
          </Border>
          <Button Content="Load more" FontSize="11" Padding="8,2" VerticalAlignment="Center"
                  Command="{Binding LoadMoreCommand}" IsVisible="{Binding HasMore}" />
        </StackPanel>
      </Border>
    </Grid>
```

Note: the `MaxHeight="240"` filter scroller, the `MaxHeight="170"` field list, the `MaxHeight="150"` joins list, and the `Expander` wrapper are all gone — each list now fills its tab and scrolls via its built-in `ScrollViewer`.

- [ ] **Step 2: Add the `OnResultsGridAttached` handler in code-behind**

In `QueryBuilderView.axaml.cs`, add `using Avalonia;` to the using block (for `VisualTreeAttachmentEventArgs`). The existing usings are:

```csharp
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;
```

Change to:

```csharp
using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using ToolBax.App.ViewModels;
```

Then add this handler method to the `QueryBuilderView` class (e.g. just below `RebuildColumns`):

```csharp
    // The Results DataGrid lives inside a TabItem, so the TabControl only realizes it when the Results
    // tab is first shown. Rebuild its dynamic columns when it attaches — the ResultColumns-change
    // trigger alone can fire while another tab is active, when FindControl("ResultsGrid") returns null.
    private void OnResultsGridAttached(object? sender, VisualTreeAttachmentEventArgs e) => RebuildColumns();
```

`RebuildColumns` already `Columns.Clear()`s first and no-ops when the grid is null, so this extra call is idempotent and safe.

- [ ] **Step 3: Build the app to verify the XAML + code-behind compile**

Run: `dotnet build avalonia/toolBax.slnx -c Debug`
Expected: Build succeeded (0 errors). If the build reports `VisualTreeAttachmentEventArgs` not found, confirm the `using Avalonia;` was added.

- [ ] **Step 4: Repoint the `Field_list_stays_height_bounded…` render test to the new invariant**

In `QueryBuilderViewRenderTests.cs`, the field list no longer has a `MaxHeight` cap — it fills its tab and scrolls. Replace the whole `Field_list_stays_height_bounded_so_large_entities_cannot_balloon_it` test with one asserting the new invariant (viewport-bounded + scrollable). It also needs `using Avalonia.VisualTree;` (already imported) for `GetVisualDescendants`:

```csharp
[AvaloniaFact]
public void Field_list_is_viewport_bounded_and_scrolls_so_large_entities_cannot_balloon_it()
{
    var view = new QueryBuilderView
    {
        DataContext = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient()),
    };
    var window = new Window { Content = view, Width = 1100, Height = 720 };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    try
    {
        // Fields is the default tab, so the list renders for CustomersV3 (which has cached fields).
        var fieldList = view.GetVisualDescendants().OfType<ListBox>()
            .FirstOrDefault(lb => lb.Name == "FieldList");
        Assert.NotNull(fieldList);

        // Under the tabbed layout the bounding comes from the tab filling a fixed grid row inside the
        // window (not an arbitrary MaxHeight): the list never exceeds the viewport, and overflow scrolls
        // via the ListBox's built-in ScrollViewer. Guard both so the "balloon" regression can't return.
        Assert.True(fieldList!.Bounds.Height > 0 && fieldList.Bounds.Height <= window.Height,
            $"field list height {fieldList.Bounds.Height} must be >0 and within the {window.Height}px viewport.");
        Assert.NotNull(fieldList.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault());
    }
    finally
    {
        window.Close();
    }
}
```

Add `using Avalonia.Controls.Primitives;`? No — `ScrollViewer` is in `Avalonia.Controls` (already imported). Leave usings as they are.

- [ ] **Step 5: Repoint the `Adding_a_condition…` render test to activate the Filter tab**

The filter builder now lives in the Filter tab (index 1), which is not selected by default, so its editors aren't realized until the tab is shown. Replace the body of `Adding_a_condition_materialises_the_builder_row_editors` so it switches to the Filter tab first:

```csharp
[AvaloniaFact]
public void Adding_a_condition_materialises_the_builder_row_editors()
{
    var vm = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient());
    var view = new QueryBuilderView { DataContext = vm };
    var window = new Window { Content = view, Width = 1100, Height = 760 };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    try
    {
        // The filter builder lives in the Filter tab; select it so its content is realized.
        vm.SelectedTabIndex = 1;
        Dispatcher.UIThread.RunJobs();

        // Adding a condition should render its field + operator combos through the recursive
        // group → ItemsControl → condition template path.
        var before = view.GetVisualDescendants().OfType<ComboBox>().Count();
        vm.FilterRoot.AddConditionCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var after = view.GetVisualDescendants().OfType<ComboBox>().Count();

        Assert.True(after >= before + 2,
            $"expected the condition's field + operator combos to render (before {before}, after {after}).");
    }
    finally
    {
        window.Close();
    }
}
```

- [ ] **Step 6: Run the render suite to confirm the view + repointed tests pass**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewRenderTests"`
Expected: PASS. In particular `Running_a_query_builds_result_grid_columns` still passes — after `RunCommand` + `RunJobs()` the VM auto-switches to Results (`SelectedTabIndex == 3`), the tab realizes, `OnResultsGridAttached` fires, and the columns are rebuilt to match `ResultColumns`.

- [ ] **Step 7: Commit**

```bash
git add avalonia/toolBax.App/Views/QueryBuilderView.axaml avalonia/toolBax.App/Views/QueryBuilderView.axaml.cs avalonia/toolBax.App.Tests/QueryBuilderViewRenderTests.cs
git commit -m "feat(avalonia): query builder workspace as Fields/Filter/Joins/Results tabs"
```

---

## Task 4: Render test — four workspace tabs render

**Files:**
- Test: `avalonia/toolBax.App.Tests/QueryBuilderViewRenderTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `QueryBuilderViewRenderTests.cs`. `TabItem` is in `Avalonia.Controls` (already imported):

```csharp
[AvaloniaFact]
public void Renders_the_four_workspace_tabs()
{
    var view = new QueryBuilderView
    {
        DataContext = new QueryBuilderViewModel(new FakeMetadataService(), new FakeODataClient()),
    };
    var window = new Window { Content = view, Width = 1100, Height = 720 };
    window.Show();
    Dispatcher.UIThread.RunJobs();
    try
    {
        // All four tab headers are realized by the tab strip (only the selected tab's *content* is lazy).
        var tabs = view.GetVisualDescendants().OfType<TabItem>().ToList();
        Assert.Equal(4, tabs.Count);
    }
    finally
    {
        window.Close();
    }
}
```

- [ ] **Step 2: Run the test to verify it passes**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilderViewRenderTests.Renders_the_four_workspace_tabs"`
Expected: PASS (1 passed). If it reports fewer than 4 `TabItem`s, confirm all four `<TabItem>` elements are present in the `TabControl` from Task 3 Step 1.

- [ ] **Step 3: Commit**

```bash
git add avalonia/toolBax.App.Tests/QueryBuilderViewRenderTests.cs
git commit -m "test(avalonia): assert query builder renders four workspace tabs"
```

---

## Task 5: Full verification + branch finish

**Files:** none (verification only).

- [ ] **Step 1: Run the full Query Builder test set**

Run: `dotnet test avalonia/toolBax.slnx --filter "FullyQualifiedName~QueryBuilder"`
Expected: PASS — all `QueryBuilderViewModelTests` (existing + 7 new) and all `QueryBuilderViewRenderTests` (2 repointed + 1 new + the rest).

- [ ] **Step 2: Run the entire Avalonia test suite to confirm no collateral regressions**

Run: `dotnet test avalonia/toolBax.slnx -c Debug`
Expected: PASS (whole suite green).

- [ ] **Step 3: Sanity-check the app visually (optional but recommended)**

Run: `dotnet run --project avalonia/toolBax.App`
Open the Query Builder, select `CustomersV3`, and confirm: the Fields tab shows the full field list (no 4-row cap), the tab headers show counts, hitting Run lands on the Results tab, and the Joins/Filter tabs each have room. Close the app when done.

- [ ] **Step 4: Finish the branch**

Use the `superpowers:finishing-a-development-branch` skill to decide between opening a PR or merging. The branch is `feat/avalonia-query-builder-tabs`; the design spec is at `docs/superpowers/specs/2026-06-11-query-builder-tabs-design.md`.

---

## Self-Review notes (for the author)

- **Spec coverage:** pinned header/footer + 4 tabs (Task 3); Fields full-height list with caps removed (Task 3); Joins promoted out of the Expander with empty-state (Task 3); Results empty-state + dynamic columns under lazy realization (Task 3 Step 2); auto-switch to Results on Run/Load more, Export-all excluded (Task 1); live tab-header counts (Task 2); both repointed existing tests (Task 3 Steps 4–5); new render test (Task 4). All spec sections map to a task.
- **Type/name consistency:** `SelectedTabIndex` (int, two-way), `ResultsTabIndex` (public const = 3), `FieldsTabHeader` / `FilterTabHeader` / `JoinsTabHeader` / `ResultsTabHeader` (string), `OnResultsGridAttached(object?, VisualTreeAttachmentEventArgs)` — all referenced consistently across the view binding, code-behind, and tests.
