# Codex Exec Transcript

- Command: codex exec --model gpt-5.4 --config model_reasoning_effort="medium" --sandbox danger-full-access --config approval_policy="never" --cd c:\Users\ben.jones\Repos\toolbAX --output-last-message c:\Users\ben.jones\Repos\toolbAX\.ralph\runs\bootstrap-001.last-message.md -
- Workspace root: c:\Users\ben.jones\Repos\toolbAX
- Execution root: c:\Users\ben.jones\Repos\toolbAX
- Prompt path: c:\Users\ben.jones\Repos\toolbAX\.ralph\artifacts\iteration-001\prompt.md
- Prompt hash: sha256:fe7d6f05a981029de296a08160d156f791b53b1f70bbf141cd27db3722b1aee0
- Prompt bytes: 8821
- Reasoning effort: medium
- Stdin hash: sha256:fe7d6f05a981029de296a08160d156f791b53b1f70bbf141cd27db3722b1aee0
- Payload matched prompt artifact: yes
- Last message path: c:\Users\ben.jones\Repos\toolbAX\.ralph\runs\bootstrap-001.last-message.md
- Exit code: 0

## Stdout

Changed files:
- [DualWriteMapBrowserView.xaml](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml)
- [DualWriteMapBrowserViewModel.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs)
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/[redacted])
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/[redacted])
- [AssemblyInfo.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/AssemblyInfo.cs)
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/tests/FoToolbox.Tests/[redacted])

The change adds a `Testify Settings` toolbar button and an inline WPF per-map settings panel in `DualWriteMapBrowser`. The panel binds to the selected map, loads from `TestifyConfigurationStore` on selection/open, and saves `omitCreateFields`, `preferredCreateValues`, `cePollTimeoutMinutes`, and `allowPartialEnumCoverage` back through the existing store path. I also added a small text serializer for the multiline editor format and test access for the plugin internals.

Validation results:
- `dotnet build .\FoToolbox.sln -c Release` succeeded.
- `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests` built successfully but could not run because this machine is missing the x64 `Microsoft.AspNetCore.App` 8.0 runtime required by `testhost.exe`.

Assumptions or blockers:
- `AGENTS.md` was not present at the repo root, so I used the Ralph files and repository code as the source of truth.
- I treated “panel inside DualWriteMapBrowser” as an inline expandable editor rather than a separate modal dialog.
- Test execution is currently blocked by the local runtime environment, not by compile errors.

Known limitations or follow-up work:
- Saving updates the persisted config immediately, but existing preflight/run state is not recomputed automatically; the UI prompts the user to run `Prepare Testify` again.
- The new persistence tests are in place but still need to be executed once the required x64 .NET 8 ASP.NET runtime is installed.
- A quick manual WPF smoke test is still warranted for the selection-change/load-save flow.

```json
{
  "selectedTaskId": "T1",
  "requestedStatus": "done",
  "progressNote": "Added a Testify Settings toolbar entry and inline per-map WPF editor bound to the selected map, with load/save through TestifyConfigurationStore. Release solution build passed. Targeted test assembly builds, but test execution is blocked on this machine by a missing x64 Microsoft.AspNetCore.App 8.0 runtime.",
  "validationRan": "dotnet build .\\FoToolbox.sln -c Release",
  "blocker": "Targeted test execution could not run because testhost.exe requires the x64 Microsoft.AspNetCore.App 8.0 runtime, which is not installed on this machine.",
  "needsHumanReview": true
}
```


## Stderr

        CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserResizeColumns="True"
                                  IsReadOnly="True"
                                  HeadersVisibility="Column"
                                  GridLinesVisibility="Horizontal"
                                  RowHeaderWidth="0"
                                  MinHeight="130"
                                  [redacted]="Auto"
                                  EnableRowVirtualization="True"
                                  EnableColumnVirtualization="True">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Key" Binding="{Binding Key, Mode=OneWay}" Width="250" MinWidth="170" />
                                <DataGridTextColumn Header="Type" Binding="{Binding Type, Mode=OneWay}" Width="90" MinWidth="80" />
                                <DataGridTextColumn Header="Value" Binding="{Binding Value, Mode=OneWay}" Width="*" MinWidth="260" />
                            </DataGrid.Columns>
                        </DataGrid>

                        <TextBlock Text="Count Validation"
                                   Margin="0,12,0,4"
                                   FontWeight="SemiBold" />
                        <TextBlock Text="Count Setup (select included legs and optional FO entity overrides)"
                                   Margin="0,0,0,4"
                                   Foreground="{DynamicResource Fo.SubtleTextBrush}" />
                        <DataGrid ItemsSource="{Binding CountLegConfigs}"
                                  SelectedItem="{Binding SelectedCountLegConfig, Mode=TwoWay}"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserResizeColumns="True"
                                  IsReadOnly="False"
                                  HeadersVisibility="Column"
                                  GridLinesVisibility="Horizontal"
                                  RowHeaderWidth="0"
                                  MinHeight="180"
                                  [redacted]="Auto"
                                  EnableRowVirtualization="True"
                                  EnableColumnVirtualization="True">
                            <DataGrid.Columns>
                                <DataGridCheckBoxColumn Header="Incl" Binding="{Binding Include, Mode=TwoWay}" Width="50" />
                                <DataGridTextColumn Header="Map" Binding="{Binding MapDisplayName, Mode=OneWay}" IsReadOnly="True" Width="220" MinWidth="160" />
                                <DataGridTextColumn Header="Leg" Binding="{Binding LegId, Mode=OneWay}" IsReadOnly="True" Width="60" MinWidth="50" />
                                <DataGridTextColumn Header="Source Schema" Binding="{Binding SourceSchema, Mode=OneWay}" IsReadOnly="True" Width="160" MinWidth="130" />
                                <DataGridTextColumn Header="Source Distinct" Binding="{Binding SourceSchemaDistinctName, Mode=OneWay}" IsReadOnly="True" Width="160" MinWidth="130" />
                                <DataGridTextColumn Header="FO Resolved" Binding="{Binding FoEntityResolved, Mode=OneWay}" IsReadOnly="True" Width="180" MinWidth="140" />
                                <DataGridTemplateColumn Header="FO Override" Width="180" MinWidth="140">
                                    <[redacted]>
                                        <DataTemplate>
                                            <TextBlock Text="{Binding FoEntityEffective}" />
                                        </DataTemplate>
                                    </[redacted]>
                                    <[redacted]>
                                        <DataTemplate>
                                            <ComboBox ItemsSource="{Binding DataContext.FoEntities, RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                      SelectedValue="{Binding FoEntityOverride, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
                                                      SelectedValuePath="Value"
                                                      DisplayMemberPath="DisplayName" />
                                        </DataTemplate>
                                    </[redacted]>
                                </DataGridTemplateColumn>
                                <DataGridTextColumn Header="CE Entity" Binding="{Binding CeEntity, Mode=OneWay}" IsReadOnly="True" Width="160" MinWidth="120" />
                                <DataGridTextColumn Header="FO Filter (Converted)" Binding="{Binding FoFilter, Mode=OneWay}" IsReadOnly="True" Width="320" MinWidth="220" />
                                <DataGridTextColumn Header="CE Filter (Reversed)" Binding="{Binding CeFilter, Mode=OneWay}" IsReadOnly="True" Width="320" MinWidth="220" />
                            </DataGrid.Columns>
                        </DataGrid>

                        <TextBlock Text="FO Count Request Preview"
                                   Margin="0,8,0,2"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding FoCountPreviewUrl, Mode=OneWay}"
                                 IsReadOnly="True"
                                 MinHeight="52"
                                 TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto" />

                        <TextBlock Text="CE Count Request Preview"
                                   Margin="0,8,0,2"
                                   FontWeight="SemiBold" />
                        <TextBox Text="{Binding CeCountPreviewUrl, Mode=OneWay}"
                                 IsReadOnly="True"
                                 MinHeight="52"
                                 TextWrapping="Wrap"
                                 VerticalScrollBarVisibility="Auto" />

                        <TextBlock Text="{Binding CountSummary}"
                                   Margin="0,8,0,4"
                                   Foreground="{DynamicResource Fo.SubtleTextBrush}" />
                        <DataGrid ItemsSource="{Binding CountResults}"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserResizeColumns="True"
                                  IsReadOnly="True"
                                  HeadersVisibility="Column"
                                  GridLinesVisibility="Horizontal"
                                  RowHeaderWidth="0"
                                  MinHeight="180"
                                  [redacted]="Auto"
                                  EnableRowVirtualization="True"
                                  EnableColumnVirtualization="True">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Map" Binding="{Binding MapDisplayName, Mode=OneWay}" Width="220" MinWidth="160" />
                                <DataGridTextColumn Header="Leg" Binding="{Binding LegId, Mode=OneWay}" Width="60" MinWidth="50" />
                                <DataGridTextColumn Header="FO Entity" Binding="{Binding FoEntity, Mode=OneWay}" Width="140" MinWidth="120" />
                                <DataGridTextColumn Header="FO Count" Binding="{Binding FoCount, Mode=OneWay}" Width="90" MinWidth="80" />
                                <DataGridTextColumn Header="CE Entity" Binding="{Binding CeEntity, Mode=OneWay}" Width="140" MinWidth="120" />
                                <DataGridTextColumn Header="CE Count" Binding="{Binding CeCount, Mode=OneWay}" Width="90" MinWidth="80" />
                                <DataGridCheckBoxColumn Header="Match" Binding="{Binding CountsMatch, Mode=OneWay}" Width="70" />
                                <DataGridTextColumn Header="Status" Binding="{Binding Status, Mode=OneWay}" Width="320" MinWidth="220" />
                                <DataGridTextColumn Header="FO Filter" Binding="{Binding FoFilter, Mode=OneWay}" Width="320" MinWidth="220" />
                                <DataGridTextColumn Header="CE Filter" Binding="{Binding CeFilter, Mode=OneWay}" Width="320" MinWidth="220" />
                            </DataGrid.Columns>
                        </DataGrid>

                        <TextBlock Text="Testify Validation"
                                   Margin="0,12,0,4"
                                   FontWeight="SemiBold" />
                        <TextBlock Text="{Binding TestifySummary}"
                                   Margin="0,0,0,4"
                                   Foreground="{DynamicResource Fo.SubtleTextBrush}" />

                        <TextBlock Text="Preflight"
                                   Margin="0,4,0,4"
                                   FontWeight="SemiBold" />
                        <DataGrid ItemsSource="{Binding TestifyPreflightRows}"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserResizeColumns="True"
                                  IsReadOnly="True"
                                  HeadersVisibility="Column"
                                  GridLinesVisibility="Horizontal"
                                  RowHeaderWidth="0"
                                  MinHeight="140"
                                  [redacted]="Auto"
                                  EnableRowVirtualization="True"
                                  EnableColumnVirtualization="True">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Map" Binding="{Binding MapDisplayName, Mode=OneWay}" Width="220" MinWidth="160" />
                                <DataGridTextColumn Header="Map Id" Binding="{Binding MapId, Mode=OneWay}" Width="190" MinWidth="140" />
                                <DataGridTextColumn Header="FO Entity" Binding="{Binding FoEntity, Mode=OneWay}" Width="160" MinWidth="120" />
                                <DataGridTextColumn Header="Enum Fields" Binding="{Binding EnumFields, Mode=OneWay}" Width="90" MinWidth="80" />
                                <DataGridTextColumn Header="Planned PATCH" Binding="{Binding PlannedUpdates, Mode=OneWay}" Width="110" MinWidth="90" />
                                <DataGridCheckBoxColumn Header="Ready" Binding="{Binding IsReady, Mode=OneWay}" Width="70" />
                                <DataGridTextColumn Header="Status" Binding="{Binding Status, Mode=OneWay}" Width="110" MinWidth="90" />
                                <DataGridTextColumn Header="Blocking Issue" Binding="{Binding BlockingIssue, Mode=OneWay}" Width="*" MinWidth="260" />
                            </DataGrid.Columns>
                        </DataGrid>

                        <TextBlock Text="Execution Results"
                                   Margin="0,8,0,4"
                                   FontWeight="SemiBold" />
                        <DataGrid ItemsSource="{Binding TestifyResultRows}"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserResizeColumns="True"
                                  IsReadOnly="True"
                                  HeadersVisibility="Column"
                                  GridLinesVisibility="Horizontal"
                                  RowHeaderWidth="0"
                                  MinHeight="120"
                                  [redacted]="Auto"
                                  EnableRowVirtualization="True"
                                  EnableColumnVirtualization="True">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Map" Binding="{Binding MapDisplayName, Mode=OneWay}" Width="220" MinWidth="160" />
                                <DataGridCheckBoxColumn Header="Valid" Binding="{Binding Valid, Mode=OneWay}" Width="60" />
                                <DataGridCheckBoxColumn Header="Created" Binding="{Binding CreateSucceeded, Mode=OneWay}" Width="70" />
                                <DataGridTextColumn Header="Patches Planned" Binding="{Binding PatchesPlanned, Mode=OneWay}" Width="100" MinWidth="90" />
                                <DataGridTextColumn Header="Patches OK" Binding="{Binding PatchesSucceeded, Mode=OneWay}" Width="90" MinWidth="80" />
                                <DataGridCheckBoxColumn Header="CE OK" Binding="{Binding CeVerificationSucceeded, Mode=OneWay}" Width="70" />
                                <DataGridTextColumn Header="Status" Binding="{Binding Status, Mode=OneWay}" Width="*" MinWidth="260" />
                            </DataGrid.Columns>
                        </DataGrid>

                        <TextBlock Text="Execution Log"
                                   Margin="0,8,0,4"
                                   FontWeight="SemiBold" />
                        <DataGrid ItemsSource="{Binding TestifyLogRows}"
                                  AutoGenerateColumns="False"
                                  CanUserAddRows="False"
                                  CanUserDeleteRows="False"
                                  CanUserResizeColumns="True"
                                  IsReadOnly="True"
                                  HeadersVisibility="Column"
                                  GridLinesVisibility="Horizontal"
                                  RowHeaderWidth="0"
                                  MinHeight="140"
                                  [redacted]="Auto"
                                  EnableRowVirtualization="True"
                                  EnableColumnVirtualization="True">
                            <DataGrid.Columns>
                                <DataGridTextColumn Header="Time" Binding="{Binding TimestampDisplay, Mode=OneWay}" Width="170" MinWidth="140" />
                                <DataGridTextColumn Header="Map" Binding="{Binding MapDisplayName, Mode=OneWay}" Width="200" MinWidth="150" />
                                <DataGridTextColumn Header="Phase" Binding="{Binding Phase, Mode=OneWay}" Width="110" MinWidth="90" />
                                <DataGridTextColumn Header="Status" Binding="{Binding Status, Mode=OneWay}" Width="90" MinWidth="80" />
                                <DataGridTextColumn Header="Detail" Binding="{Binding Detail, Mode=OneWay}" Width="*" MinWidth="260" />
                            </DataGrid.Columns>
                        </DataGrid>
                    </StackPanel>
                </ScrollViewer>
            </Border>
        </Grid>

        <Border Grid.Row="2"
                Padding="8"
                BorderThickness="0,1,0,0"
                BorderBrush="{DynamicResource Fo.BorderBrush}"
                Background="{DynamicResource Fo.SurfaceAltBrush}">
            <TextBlock Text="{Binding StatusMessage}" TextWrapping="Wrap" />
        </Border>
    </Grid>
</UserControl>


 succeeded in 4164ms:
using System;
using System.Collections.Generic;

namespace DualWriteMapBrowserPlugin;

public sealed class TestifyMapPlan
{
    public TestifyMapPlan(
        string mapId,
        string mapDisplayName,
        string foEntity,
        [redacted]? foEntityDetails,
        TestifyMapConfiguration configuration,
        string foFilter,
        IReadOnlyList<TestifyLegPlan> ceLegs,
        IReadOnlyDictionary<string, string> createValues,
        string createPayloadJson,
        IReadOnlyDictionary<string, TestifyEnumFieldPlan> enumFields,
        IReadOnlyList<TestifyPatchStep> patchSteps,
        IReadOnlyList<string> warnings,
        IReadOnlyList<string> blockingIssues)
    {
        MapId = mapId;
        MapDisplayName = mapDisplayName;
        FoEntity = foEntity;
        FoEntityDetails = foEntityDetails;
        Configuration = configuration;
        FoFilter = foFilter;
        CeLegs = ceLegs;
        CreateValues = createValues;
        CreatePayloadJson = createPayloadJson;
        EnumFields = enumFields;
        PatchSteps = patchSteps;
        Warnings = warnings;
        BlockingIssues = blockingIssues;
    }

    public string MapId { get; }
    public string MapDisplayName { get; }
    public string FoEntity { get; }
    public [redacted]? FoEntityDetails { get; }
    public TestifyMapConfiguration Configuration { get; }
    public string FoFilter { get; }
    public IReadOnlyList<TestifyLegPlan> CeLegs { get; }
    public IReadOnlyDictionary<string, string> CreateValues { get; }
    public string CreatePayloadJson { get; }
    public IReadOnlyDictionary<string, TestifyEnumFieldPlan> EnumFields { get; }
    public IReadOnlyList<TestifyPatchStep> PatchSteps { get; }
    public IReadOnlyList<string> Warnings { get; }
    public IReadOnlyList<string> BlockingIssues { get; }
    public bool CanRun => BlockingIssues.Count == 0 && FoEntityDetails is not null && !string.IsNullOrWhiteSpace(CreatePayloadJson);
}

public sealed class TestifyLegPlan
{
    public TestifyLegPlan(string legId, string ceEntity, string ceFilter)
    {
        LegId = legId;
        CeEntity = ceEntity;
        CeFilter = ceFilter;
    }

    public string LegId { get; }
    public string CeEntity { get; }
    public string CeFilter { get; }
}

public sealed class TestifyEnumFieldPlan
{
    public TestifyEnumFieldPlan(
        string fieldName,
        string enumType,
        IReadOnlyList<string> enumMembers,
        IReadOnlySet<string> transformKeys,
        IReadOnlyList<string> missingMembers,
        string? fixedValue,
        bool parseFailed,
        string parseError)
    {
        FieldName = fieldName;
        EnumType = enumType;
        EnumMembers = enumMembers;
        TransformKeys = transformKeys;
        MissingMembers = missingMembers;
        FixedValue = fixedValue;
        ParseFailed = parseFailed;
        ParseError = parseError;
    }

    public string FieldName { get; }
    public string EnumType { get; }
    public IReadOnlyList<string> EnumMembers { get; }
    public IReadOnlySet<string> TransformKeys { get; }
    public IReadOnlyList<string> MissingMembers { get; }
    public string? FixedValue { get; }
    public bool ParseFailed { get; }
    public string ParseError { get; }
}

public sealed class TestifyPatchStep
{
    public TestifyPatchStep(int stepNumber, IReadOnlyDictionary<string, string> enumValues)
    {
        StepNumber = stepNumber;
        EnumValues = enumValues;
    }

    public int StepNumber { get; }
    public IReadOnlyDictionary<string, string> EnumValues { get; }
}

public sealed class TestifyPreflightRow
{
    public TestifyPreflightRow(
        string mapDisplayName,
        string mapId,
        string foEntity,
        int enumFields,
        int plannedUpdates,
        bool isReady,
        string status,
        string blockingIssue)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        FoEntity = foEntity;
        EnumFields = enumFields;
        PlannedUpdates = plannedUpdates;
        IsReady = isReady;
        Status = status;
        BlockingIssue = blockingIssue;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public string FoEntity { get; }
    public int EnumFields { get; }
    public int PlannedUpdates { get; }
    public bool IsReady { get; }
    public string Status { get; }
    public string BlockingIssue { get; }
}

public sealed class TestifyExecutionLogRow
{
    public TestifyExecutionLogRow(DateTimeOffset timestampUtc, string mapDisplayName, string phase, string status, string detail)
    {
        TimestampUtc = timestampUtc;
        MapDisplayName = mapDisplayName;
        Phase = phase;
        Status = status;
        Detail = detail;
    }

    public DateTimeOffset TimestampUtc { get; }
    public string MapDisplayName { get; }
    public string Phase { get; }
    public string Status { get; }
    public string Detail { get; }
    public string TimestampDisplay => TimestampUtc.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'");
}

public sealed class TestifyResultRow
{
    public TestifyResultRow(
        string mapDisplayName,
        string mapId,
        bool valid,
        bool createSucceeded,
        int patchesPlanned,
        int patchesSucceeded,
        bool ceVerificationSucceeded,
        string status)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        Valid = valid;
        CreateSucceeded = createSucceeded;
        PatchesPlanned = patchesPlanned;
        PatchesSucceeded = patchesSucceeded;
        CeVerificationSucceeded = ceVerificationSucceeded;
        Status = status;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public bool Valid { get; }
    public bool CreateSucceeded { get; }
    public int PatchesPlanned { get; }
    public int PatchesSucceeded { get; }
    public bool CeVerificationSucceeded { get; }
    public string Status { get; }
}


 succeeded in 4455ms:
using DualWriteMapBrowserPlugin;
using FoToolbox.Core.OData;
using Xunit;

namespace FoToolbox.Tests;

public sealed class DualWriteMapBrowserTestifyTests
{
    [Fact]
    public void [redacted]()
    {
        var entity = new ODataEntity(
            "CustomersV3",
            new[]
            {
                new ODataProperty("AccountNumber", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true, MaxLength: "20"),
                new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true, MaxLength: "4"),
                new ODataProperty("CustomerType", "Default.CustomerType", Nullable: false, IsMandatory: false),
            },
            Array.Empty<ODataNavigationProperty>());

        var rawValues = new Dictionary<string, string>([redacted])
        {
            ["ACCOUNTNUMBER"] = "CUST-0001",
            ["DATAAREAID"] = "USMF"
        };

        var normalized = [redacted](rawValues, entity.Properties, out var warnings);
        Assert.Empty(warnings);

        var enumFields = new Dictionary<string, IReadOnlyList<string>>([redacted])
        {
            ["CustomerType"] = new[] { "Retail", "Wholesale", "Online" }
        };

        var steps = [redacted](enumFields);
        Assert.Equal(2, steps.Count);

        normalized["CustomerType"] = "Retail";

        var enumByType = new Dictionary<string, IReadOnlyList<string>>([redacted])
        {
            ["Default.CustomerType"] = new[] { "Retail", "Wholesale", "Online" }
        };

        var okPayload = TestifyRunner.TryBuildPayload(entity, normalized, enumByType, enforceMandatory: true, out var createJson, out var issues);
        Assert.True(okPayload, string.Join(" | ", issues));
        Assert.Contains("CustomerType", createJson);

        var okUrl = [redacted](
            "https://contoso.operations.dynamics.com/data/CustomersV3",
            entity,
            normalized,
            out var instanceUrl,
            out var urlError);

        Assert.True(okUrl, urlError);
        Assert.Contains("AccountNumber='CUST-0001'", instanceUrl);
        Assert.Contains("dataAreaId='USMF'", instanceUrl);
        Assert.Contains("cross-company=true", instanceUrl);
    }
}


codex
I have the view and model shape. I’m drilling into `SelectedRecord` and command wiring now, then I’ll add a narrow regression test around config persistence before touching production code.
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "rg -n \"SelectedRecord|GetMapsForCounting|CountLegConfigs|StatusMessage|OnPropertyChanged\\(\" plugins/DualWriteMapBrowser" in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command 'rg -n "TestifyConfigurationStore|TestifyMapConfiguration|AllowPartialEnumCoverage|CePollTimeoutMinutes|PreferredCreateValuesByCompany" tests/FoToolbox.Tests' in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs'" in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 1799ms:
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:150:                              SelectedItem="{Binding SelectedRecord, Mode=TwoWay}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:218:                            <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding SelectedRecord.DisplayName}" TextWrapping="Wrap" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:220:                            <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding SelectedRecord.Name}" TextWrapping="Wrap" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:222:                            <TextBlock Grid.Row="2" Grid.Column="1" Text="{Binding SelectedRecord.Id}" TextWrapping="Wrap" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:224:                            <TextBlock Grid.Row="3" Grid.Column="1" Text="{Binding SelectedRecord.Version}" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:226:                            <TextBlock Grid.Row="4" Grid.Column="1" Text="{Binding SelectedRecord.Owner}" TextWrapping="Wrap" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:228:                            <TextBlock Grid.Row="5" Grid.Column="1" Text="{Binding SelectedRecord.CreatedOnDisplay}" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:230:                            <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:232:                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:240:                                <DataGrid ItemsSource="{Binding [redacted]}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:259:                                <DataGrid ItemsSource="{Binding SelectedRecord.MappingLegRows}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:284:                                <DataGrid ItemsSource="{Binding SelectedRecord.MappingFieldRows}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:310:                                <DataGrid ItemsSource="{Binding [redacted]}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:334:                                <DataGrid ItemsSource="{Binding SelectedRecord.MappingRows}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:358:                        <DataGrid ItemsSource="{Binding SelectedRecord.PropertiesRows}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:384:                        <DataGrid ItemsSource="{Binding CountLegConfigs}"
plugins/DualWriteMapBrowser\DualWriteMapBrowserView.xaml:570:            <TextBlock Text="{Binding StatusMessage}" TextWrapping="Wrap" />
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:104:            StatusMessage = $"Command failed: {ex.Message}";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:118:            StatusMessage = "Dataverse profile is not configured for this environment.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:143:    public ReadOnlyObservableCollection<CountLegConfigRow> CountLegConfigs => _countLegConfigsReadOnly;
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:157:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:158:            OnPropertyChanged(nameof(IsNotLoading));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:159:            OnPropertyChanged(nameof(IsBusy));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:176:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:177:            OnPropertyChanged(nameof(IsBusy));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:178:            OnPropertyChanged(nameof(IsNotLoading));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:193:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:194:            OnPropertyChanged(nameof(IsBusy));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:195:            OnPropertyChanged(nameof(IsNotLoading));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:212:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:227:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:244:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:251:    public DualWriteMapRecord? SelectedRecord
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:262:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:277:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:292:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:297:    public string StatusMessage
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:303:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:313:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:323:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:333:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:348:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:364:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:379:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:387:            StatusMessage = "Dataverse profile is not configured for this environment.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:398:            StatusMessage = "Select a solution, or clear 'Filter by solution'.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:404:        SelectedRecord = null;
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:409:        StatusMessage = "Loading dual-write map records...";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:416:            StatusMessage = $"Resolving map components for solution '{SelectedSolution.DisplayName}'...";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:425:                StatusMessage = $"No dual-write map components found in solution '{SelectedSolution.DisplayName}'.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:483:                StatusMessage = $"Loaded {_records.Count} records so far...";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:488:            SelectedRecord ??= _records.FirstOrDefault();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:490:            StatusMessage = FilterBySolution && SelectedSolution is not null
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:496:            StatusMessage = "Load cancelled.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:501:            StatusMessage = $"Load failed: {ex.Message}";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:698:        var selectedMaps = GetMapsForCounting();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:705:                StatusMessage = "Select one or more maps (checkbox), or select a current map.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:758:                StatusMessage = "No count legs available for the selected maps.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:774:            StatusMessage = $"Prepared count setup for {_countLegConfigs.Count} leg(s).";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:782:            StatusMessage = "Dataverse profile is not configured for this environment.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:789:            StatusMessage = "No count legs available for the selected maps.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:797:            StatusMessage = "No count legs included. Select at least one row in count setup.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:920:            StatusMessage = "Count validation finished.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:925:            StatusMessage = "Count validation cancelled.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:966:    private List<DualWriteMapRecord> GetMapsForCounting()
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:969:        if (selectedMaps.Count == 0 && SelectedRecord is not null)
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:971:            selectedMaps.Add(SelectedRecord);
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:1921:        SelectedRecord = null;
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:1928:        StatusMessage = "Cleared.";
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:2473:    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:2750:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:2766:            OnPropertyChanged();
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:2767:            OnPropertyChanged(nameof(FoEntityEffective));
plugins/DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs:2778:    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
plugins/DualWriteMapBrowser\[redacted]:55:            OnPropertyChanged();
plugins/DualWriteMapBrowser\[redacted]:70:            OnPropertyChanged(nameof(IsBusy));
plugins/DualWriteMapBrowser\[redacted]:71:            OnPropertyChanged(nameof(IsNotLoading));
plugins/DualWriteMapBrowser\[redacted]:86:            OnPropertyChanged(nameof(IsBusy));
plugins/DualWriteMapBrowser\[redacted]:87:            OnPropertyChanged(nameof(IsNotLoading));
plugins/DualWriteMapBrowser\[redacted]:95:            StatusMessage = "Testify requires OData.Write capability, but it is not available in this host context.";
plugins/DualWriteMapBrowser\[redacted]:99:        var selectedMaps = GetMapsForCounting();
plugins/DualWriteMapBrowser\[redacted]:102:            StatusMessage = "Select one or more maps (checkbox), or select a current map.";
plugins/DualWriteMapBrowser\[redacted]:152:            StatusMessage = "Testify preflight complete.";
plugins/DualWriteMapBrowser\[redacted]:157:            StatusMessage = "Testify preflight cancelled.";
plugins/DualWriteMapBrowser\[redacted]:163:            StatusMessage = $"Testify preflight failed: {ex.Message}";
plugins/DualWriteMapBrowser\[redacted]:175:            StatusMessage = "Testify requires OData.Write capability, but it is not available in this host context.";
plugins/DualWriteMapBrowser\[redacted]:181:            StatusMessage = "Dataverse profile is not configured for this environment.";
plugins/DualWriteMapBrowser\[redacted]:197:            StatusMessage = "No Testify-ready maps. Run 'Prepare Testify' and resolve blocking issues.";
plugins/DualWriteMapBrowser\[redacted]:215:            StatusMessage = "Testify run cancelled.";
plugins/DualWriteMapBrowser\[redacted]:386:            StatusMessage = "Testify run complete.";
plugins/DualWriteMapBrowser\[redacted]:391:            StatusMessage = "Testify run cancelled.";
plugins/DualWriteMapBrowser\[redacted]:1620:            StatusMessage = "Testify cleanup requires OData.Write capability, but it is not available in this host context.";
plugins/DualWriteMapBrowser\[redacted]:1629:                StatusMessage = "No Testify plans available for cleanup. Run 'Prepare Testify' first.";
plugins/DualWriteMapBrowser\[redacted]:1692:            StatusMessage = "No Testify test records found to clean up.";
plugins/DualWriteMapBrowser\[redacted]:1709:            StatusMessage = "Testify cleanup cancelled.";
plugins/DualWriteMapBrowser\[redacted]:1756:        StatusMessage = $"Testify cleanup complete. Deleted {deleted} record(s).";

 succeeded in 2085ms:
using FoToolbox.Core.Auth;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Commands;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace DualWriteMapBrowserPlugin;

public sealed partial class DualWriteMapBrowserViewModel : INotifyPropertyChanged
{
    private const int DualWriteMapComponentType = 500;
    private static readonly string SelectColumns = string.Join(",",
        "msdyn_dualwriteentitymapid",
        "solutionid",
        "msdyn_name",
        "msdyn_displayname",
        "msdyn_mapping",
        "msdyn_properties",
        "msdyn_version",
        "createdon",
        "modifiedon",
        "statecode",
        "statuscode",
        "ownerid");

    private readonly IPluginContext _ctx;
    private readonly IPluginContextDataverse? _dataverse;
    private readonly ObservableCollection<PublisherOption> _publishers = new();
    private readonly ObservableCollection<SolutionOption> _solutions = new();
    private readonly ObservableCollection<DualWriteMapRecord> _records = new();
    private readonly ObservableCollection<FoEntityOption> _foEntities = new();
    private readonly ObservableCollection<CountLegConfigRow> _countLegConfigs = new();
    private readonly ObservableCollection<CountValidationRow> _countResults = new();
    private readonly ReadOnlyObservableCollection<PublisherOption> _publishersReadOnly;
    private readonly ReadOnlyObservableCollection<SolutionOption> _solutionsReadOnly;
    private readonly ReadOnlyObservableCollection<FoEntityOption> _foEntitiesReadOnly;
    private readonly ReadOnlyObservableCollection<CountLegConfigRow> _countLegConfigsReadOnly;
    private readonly ReadOnlyObservableCollection<CountValidationRow> _countResultsReadOnly;
    private Dictionary<string, string>? _foEntityLookup;
    private Dictionary<string, ODataEnumType> _foEnumLookup = new([redacted]);
    private readonly Dictionary<string, ODataEntity?> _foEntityDetailsCache = new([redacted]);
    private readonly Dictionary<string, Dictionary<string, string>> _foEntityFieldLookup = new([redacted]);
    private readonly Dictionary<string, Dictionary<string, ODataEnumType>> _foEntityEnumFields = new([redacted]);
    private List<string> _foEntityNames = new();
    private string _statusMessage = "Ready.";
    private string _solutionSummary = "Solutions not loaded.";
    private string _recordSummary = "Showing 0 of 0 records";
    private string _countSummary = "No count run yet.";
    private string _foCountPreviewUrl = string.Empty;
    private string _ceCountPreviewUrl = string.Empty;
    private bool _isLoading;
    private bool _isLoadingSolutions;
    private bool _isCounting;
    private bool _useExactCeCount;
    private bool _filterBySolution;
    private string? _searchText;
    private PublisherOption? _selectedPublisher;
    private SolutionOption? _selectedSolution;
    private DualWriteMapRecord? _selectedRecord;
    private CountLegConfigRow? _selectedCountLegConfig;

    public DualWriteMapBrowserViewModel(IPluginContext ctx)
    {
        _ctx = ctx;
        _dataverse = ctx as IPluginContextDataverse;
        _write = ctx as IPluginContextWrite;
        DataverseEndpoint = HasDataverseConnection
            ? [redacted](_dataverse!.CurrentDataverseEnv!.BaseUrl)
            : "Dataverse profile not configured. Open Profiles and set CE/Dataverse values.";
        _publishersReadOnly = new ReadOnlyObservableCollection<PublisherOption>(_publishers);
        _solutionsReadOnly = new ReadOnlyObservableCollection<SolutionOption>(_solutions);
        _foEntitiesReadOnly = new ReadOnlyObservableCollection<FoEntityOption>(_foEntities);
        _countLegConfigsReadOnly = new ReadOnlyObservableCollection<CountLegConfigRow>(_countLegConfigs);
        _countResultsReadOnly = new ReadOnlyObservableCollection<CountValidationRow>(_countResults);
        _testifyPreflightRowsReadOnly = new ReadOnlyObservableCollection<TestifyPreflightRow>(_testifyPreflightRows);
        _testifyLogRowsReadOnly = new ReadOnlyObservableCollection<TestifyExecutionLogRow>(_testifyLogRows);
        _testifyResultRowsReadOnly = new ReadOnlyObservableCollection<TestifyResultRow>(_testifyResultRows);

        SolutionsView = [redacted](_solutions);
        SolutionsView.Filter = SolutionFilter;

        RecordsView = [redacted](_records);
        RecordsView.Filter = RecordFilter;

        Action<Exception> onError = ex =>
        {
            _ctx.Logger.LogError(ex, "DualWriteMapBrowser command failed.");
            StatusMessage = $"Command failed: {ex.Message}";
        };

        LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
        LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
        ClearCommand = new RelayCommand(_ => ClearRecords());

        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
        }
        else
        {
            _ = LoadSolutionsAsync(CancellationToken.None);
        }
    }

    private bool HasDataverseConnection =>
        _dataverse is not null &&
        _dataverse.HasDataverseProfile &&
        _dataverse.DataverseHttp is not null &&
        _dataverse.CurrentDataverseEnv is not null;

    public ICollectionView SolutionsView { get; }
    public ICollectionView RecordsView { get; }
    public AsyncRelayCommand LoadMapsCommand { get; }
    public AsyncRelayCommand LoadSolutionsCommand { get; }
    public AsyncRelayCommand RefreshCountSetupCommand { get; }
    public AsyncRelayCommand ValidateCountsCommand { get; }
    public RelayCommand ClearCommand { get; }
    public string DataverseEndpoint { get; }
    public ReadOnlyObservableCollection<PublisherOption> Publishers => _publishersReadOnly;
    public ReadOnlyObservableCollection<SolutionOption> Solutions => _solutionsReadOnly;
    public ReadOnlyObservableCollection<FoEntityOption> FoEntities => _foEntitiesReadOnly;
    public ReadOnlyObservableCollection<CountLegConfigRow> CountLegConfigs => _countLegConfigsReadOnly;
    public ReadOnlyObservableCollection<CountValidationRow> CountResults => _countResultsReadOnly;

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            if (_isLoading == value)
            {
                return;
            }

            _isLoading = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotLoading));
            OnPropertyChanged(nameof(IsBusy));
        }
    }

    public bool IsNotLoading => !IsBusy;

    public bool IsLoadingSolutions
    {
        get => _isLoadingSolutions;
        set
        {
            if (_isLoadingSolutions == value)
            {
                return;
            }

            _isLoadingSolutions = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsCounting
    {
        get => _isCounting;
        set
        {
            if (_isCounting == value)
            {
                return;
            }

            _isCounting = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsNotLoading));
        }
    }

    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;

    public bool FilterBySolution
    {
        get => _filterBySolution;
        set
        {
            if (_filterBySolution == value)
            {
                return;
            }

            _filterBySolution = value;
            OnPropertyChanged();
        }
    }

    public string? SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value)
            {
                return;
            }

            _searchText = value;
            OnPropertyChanged();
            RecordsView.Refresh();
            UpdateRecordSummary();
        }
    }

    public PublisherOption? SelectedPublisher
    {
        get => _selectedPublisher;
        set
        {
            if (_selectedPublisher == value)
            {
                return;
            }

            _selectedPublisher = value;
            OnPropertyChanged();
            SolutionsView.Refresh();
            SelectedSolution = SolutionsView.Cast<SolutionOption>().FirstOrDefault();
            UpdateSolutionSummary();
        }
    }

    public DualWriteMapRecord? SelectedRecord
    {
        get => _selectedRecord;
        set
        {
            if (_selectedRecord == value)
            {
                return;
            }

            _selectedRecord = value;
            OnPropertyChanged();
        }
    }

    public SolutionOption? SelectedSolution
    {
        get => _selectedSolution;
        set
        {
            if (_selectedSolution == value)
            {
                return;
            }

            _selectedSolution = value;
            OnPropertyChanged();
        }
    }

    public CountLegConfigRow? SelectedCountLegConfig
    {
        get => _selectedCountLegConfig;
        set
        {
            if (_selectedCountLegConfig == value)
            {
                return;
            }

            _selectedCountLegConfig = value;
            OnPropertyChanged();
            RefreshCountPreviewUrls();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string RecordSummary
    {
        get => _recordSummary;
        set
        {
            _recordSummary = value;
            OnPropertyChanged();
        }
    }

    public string SolutionSummary
    {
        get => _solutionSummary;
        set
        {
            _solutionSummary = value;
            OnPropertyChanged();
        }
    }

    public string CountSummary
    {
        get => _countSummary;
        set
        {
            _countSummary = value;
            OnPropertyChanged();
        }
    }

    public bool UseExactCeCount
    {
        get => _useExactCeCount;
        set
        {
            if (_useExactCeCount == value)
            {
                return;
            }

            _useExactCeCount = value;
            OnPropertyChanged();
            RefreshCountPreviewUrls();
        }
    }

    public string FoCountPreviewUrl
    {
        get => _foCountPreviewUrl;
        set
        {
            if (_foCountPreviewUrl == value)
            {
                return;
            }

            _foCountPreviewUrl = value;
            OnPropertyChanged();
        }
    }

    public string CeCountPreviewUrl
    {
        get => _ceCountPreviewUrl;
        set
        {
            if (_ceCountPreviewUrl == value)
            {
                return;
            }

            _ceCountPreviewUrl = value;
            OnPropertyChanged();
        }
    }

    private async Task LoadMapsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
            return;
        }

        if (FilterBySolution && _solutions.Count == 0)
        {
            await LoadSolutionsAsync(cancellationToken);
        }

        if (FilterBySolution && SelectedSolution is null)
        {
            StatusMessage = "Select a solution, or clear 'Filter by solution'.";
            return;
        }

        IsLoading = true;
        _records.Clear();
        SelectedRecord = null;
        ClearCountSetup();
        _countResults.Clear();
        CountSummary = "No count run yet.";
        UpdateRecordSummary();
        StatusMessage = "Loading dual-write map records...";

        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = [redacted](_dataverse.CurrentDataverseEnv!.BaseUrl);
        HashSet<Guid>? componentMapIds = null;
        if (FilterBySolution && SelectedSolution is not null)
        {
            StatusMessage = $"Resolving map components for solution '{SelectedSolution.DisplayName}'...";
            componentMapIds = await LoadDualWriteComponentIdsForSolutionAsync(
                dataverseHttp,
                apiBase,
                SelectedSolution.UniqueName,
                cancellationToken);

            if (componentMapIds.Count == 0)
            {
                StatusMessage = $"No dual-write map components found in solution '{SelectedSolution.DisplayName}'.";
                IsLoading = false;
                return;
            }
        }

        var nextLink = BuildMapsUrl(apiBase);
        var pageCount = 0;

        try
        {
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                [redacted]();

                using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                [redacted](
                    "Prefer",
                    "odata.include-annotations=\"[redacted]\",odata.maxpagesize=250");

                using var response = await dataverseHttp.SendAsync(
                    request,
                    [redacted],
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await [redacted](cancellationToken);
                    throw new InvalidOperationException(
                        $"Dataverse request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
                }

                await using var stream = await [redacted](cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;

                if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Dataverse response did not contain a 'value' array.");
                }

                foreach (var item in valueArray.EnumerateArray())
                {
                    if (componentMapIds is not null)
                    {
                        var mapIdText = GetValueAsString(item, "msdyn_dualwriteentitymapid");
                        if (!Guid.TryParse(mapIdText, out var mapId) || !componentMapIds.Contains(mapId))
                        {
                            continue;
                        }
                    }

                    _records.Add(ParseRecord(item));
                }

                pageCount++;
                nextLink = GetValueAsString(root, "@odata.nextLink");
                StatusMessage = $"Loaded {_records.Count} records so far...";
            }

            RecordsView.Refresh();
            UpdateRecordSummary();
            SelectedRecord ??= _records.FirstOrDefault();
            await RefreshCountSetupCoreAsync(cancellationToken, updateStatus: false);
            StatusMessage = FilterBySolution && SelectedSolution is not null
                ? $"Loaded {_records.Count} dual-write map records from solution '{SelectedSolution.DisplayName}' ({pageCount} page(s))."
                : $"Loaded {_records.Count} dual-write map records from {pageCount} page(s).";
        }
        catch (OperationCanceledException) when ([redacted])
        {
            StatusMessage = "Load cancelled.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load msdyn_dualwriteentitymap records.");
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadSolutionsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            SolutionSummary = "Dataverse profile not configured.";
            return;
        }

        IsLoadingSolutions = true;
        var dataverseHttp = _dataverse!.DataverseHttp!;
        var apiBase = [redacted](_dataverse.CurrentDataverseEnv!.BaseUrl);
        var nextLink = $"{apiBase}/solutions?$select=solutionid,uniquename,friendlyname,version,_publisherid_value&$expand=publisherid($select=uniquename,friendlyname)&$orderby=uniquename%20asc";
        var loaded = new List<SolutionOption>();
        var pageCount = 0;
        var selectedId = SelectedSolution?.Id;
        var selectedPublisherKey = SelectedPublisher?.UniqueName;

        try
        {
            while (!string.IsNullOrWhiteSpace(nextLink))
            {
                [redacted]();

                using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                [redacted]("Prefer", "odata.maxpagesize=500");

                using var response = await dataverseHttp.SendAsync(
                    request,
                    [redacted],
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var body = await [redacted](cancellationToken);
                    throw new InvalidOperationException(
                        $"Dataverse solutions request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
                }

                await using var stream = await [redacted](cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
                {
                    throw new InvalidOperationException("Dataverse solutions response did not contain a 'value' array.");
                }

                foreach (var solution in valueArray.EnumerateArray())
                {
                    var idText = GetValueAsString(solution, "solutionid");
                    if (!Guid.TryParse(idText, out var id))
                    {
                        continue;
                    }

                    var uniqueName = GetValueAsString(solution, "uniquename") ?? string.Empty;
                    var friendlyName = GetValueAsString(solution, "friendlyname") ?? string.Empty;
                    var version = GetValueAsString(solution, "version") ?? string.Empty;

                    var publisherUniqueName = string.Empty;
                    var publisherDisplayName = GetValueAsString(solution, "_publisherid_value@[redacted]") ?? string.Empty;
                    if (solution.TryGetProperty("publisherid", out var publisher) && publisher.ValueKind == JsonValueKind.Object)
                    {
                        publisherUniqueName = GetValueAsString(publisher, "uniquename") ?? string.Empty;
                        var friendlyPublisherName = GetValueAsString(publisher, "friendlyname") ?? string.Empty;
                        if (!string.IsNullOrWhiteSpace(friendlyPublisherName))
                        {
                            publisherDisplayName = friendlyPublisherName;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(publisherUniqueName))
                    {
                        publisherUniqueName = GetValueAsString(solution, "_publisherid_value") ?? string.Empty;
                    }

                    if (string.IsNullOrWhiteSpace(publisherDisplayName))
                    {
                        publisherDisplayName = string.IsNullOrWhiteSpace(publisherUniqueName)
                            ? "(Unknown Publisher)"
                            : publisherUniqueName;
                    }

                    var display = string.IsNullOrWhiteSpace(friendlyName) ? uniqueName : $"{friendlyName} [{uniqueName}]";
                    if (!string.IsNullOrWhiteSpace(version))
                    {
                        display = $"{display} v{version}";
                    }

                    loaded.Add(new SolutionOption(
                        id,
                        display,
                        uniqueName,
                        friendlyName,
                        version,
                        publisherUniqueName,
                        publisherDisplayName));
                }

                pageCount++;
                nextLink = GetValueAsString(root, "@odata.nextLink");
            }

            _solutions.Clear();
            foreach (var option in loaded
                         .OrderBy(s => s.PublisherDisplayName, [redacted])
                         .ThenBy(s => s.UniqueName, [redacted])
                         .ThenBy(s => s.Version, [redacted]))
            {
                _solutions.Add(option);
            }

            RebuildPublishers(loaded, selectedPublisherKey);
            SolutionsView.Refresh();
            SelectedSolution = selectedId is not null
                ? _solutions.FirstOrDefault(s => s.Id == selectedId.Value && SolutionFilter(s)) ?? SolutionsView.Cast<SolutionOption>().FirstOrDefault()
                : SolutionsView.Cast<SolutionOption>().FirstOrDefault();

            UpdateSolutionSummary(pageCount);
        }
        catch (OperationCanceledException) when ([redacted])
        {
            SolutionSummary = "Solutions load cancelled.";
        }
        catch (Exception ex)
        {
            _ctx.Logger.LogError(ex, "Failed to load solutions.");
            SolutionSummary = $"Solutions load failed: {ex.Message}";
        }
        finally
        {
            IsLoadingSolutions = false;
        }
    }

    private void RebuildPublishers(IEnumerable<SolutionOption> loaded, string? selectedPublisherKey)
    {
        var selectedKey = string.IsNullOrWhiteSpace(selectedPublisherKey)
            ? PublisherOption.All.UniqueName
            : selectedPublisherKey;

        _publishers.Clear();
        _publishers.Add(PublisherOption.All);

        foreach (var publisher in loaded
                     .Where(s => !string.IsNullOrWhiteSpace(s.PublisherUniqueName))
                     .GroupBy(s => s.PublisherUniqueName, [redacted])
                     .Select(g => new PublisherOption(
                         g.First().PublisherUniqueName,
                         g.First().PublisherDisplayName,
                         g.Count()))
                     .OrderBy(p => p.DisplayName, [redacted]))
        {
            _publishers.Add(publisher);
        }

        SelectedPublisher = _publishers.FirstOrDefault(p => string.Equals(p.UniqueName, selectedKey, [redacted]))
            ?? _publishers.FirstOrDefault();
    }

    private bool SolutionFilter(object? item)
    {
        if (item is not SolutionOption solution)
        {
            return false;
        }

        if (SelectedPublisher is null || SelectedPublisher.IsAll)
        {
            return true;
        }

        return string.Equals(solution.PublisherUniqueName, SelectedPublisher.UniqueName, [redacted]);
    }

    private void UpdateSolutionSummary(int? pageCount = null)
    {
        var visible = SolutionsView.Cast<object>().Count();
        var pagePart = pageCount is null ? string.Empty : $" from {pageCount.Value} page(s)";
        SolutionSummary = $"Showing {visible} of {_solutions.Count} solutions{pagePart}.";
    }

    private async Task RefreshCountSetupAsync(CancellationToken cancellationToken)
    {
        await RefreshCountSetupCoreAsync(cancellationToken, updateStatus: true);
    }

    private async Task RefreshCountSetupCoreAsync(CancellationToken cancellationToken, bool updateStatus)
    {
        var selectedMaps = GetMapsForCounting();
        if (selectedMaps.Count == 0)
        {
            ClearCountSetup();
            CountSummary = "No count setup prepared.";
            if (updateStatus)
            {
                StatusMessage = "Select one or more maps (checkbox), or select a current map.";
            }
            return;
        }

        await EnsureFoEntityLookupAsync(cancellationToken);

        var previousRows = _countLegConfigs.ToDictionary(
            row => BuildCountLegKey(row.MapId, row.LegId),
            row => row,
            [redacted]);
        var selectedKey = SelectedCountLegConfig is null
            ? null
            : BuildCountLegKey(SelectedCountLegConfig.MapId, SelectedCountLegConfig.LegId);

        ClearCountSetup();

        foreach (var map in selectedMaps)
        {
            [redacted]();
            foreach (var leg in map.MappingLegRows)
            {
                var rowKey = BuildCountLegKey(map.Id, leg.LegId);
                previousRows.TryGetValue(rowKey, out var previous);
                var foEntityResolved = ResolveFoEntityName(leg.SourceSchemaDistinctName, leg.SourceSchema);
                var (foFilter, filterNote) = await ConvertSourceFilterToODataAsync(foEntityResolved, leg.SourceFilter, cancellationToken);
                var row = new CountLegConfigRow(
                    mapDisplayName: map.DisplayName,
                    mapId: map.Id,
                    legId: leg.LegId,
                    sourceSchema: leg.SourceSchema,
                    sourceSchemaDistinctName: leg.SourceSchemaDistinctName,
                    sourceEnvironmentType: leg.SourceEnvironmentType,
                    destinationEnvironmentType: leg.DestinationEnvironmentType,
                    foEntityResolved: foEntityResolved,
                    sourceFilterXpp: leg.SourceFilter,
                    foFilter: foFilter,
                    foFilterNote: filterNote,
                    ceEntity: leg.DestinationSchema,
                    ceFilter: leg.ReversedSourceFilter?.Trim() ?? string.Empty,
                    include: previous?.Include ?? true,
                    foEntityOverride: previous?.FoEntityOverride ?? string.Empty);

                AttachCountLegConfig(row);
                _countLegConfigs.Add(row);
            }
        }

        if (_countLegConfigs.Count == 0)
        {
            CountSummary = "No count legs available for the selected maps.";
            if (updateStatus)
            {
                StatusMessage = "No count legs available for the selected maps.";
            }
            return;
        }

        SelectedCountLegConfig = selectedKey is null
            ? _countLegConfigs.FirstOrDefault()
            : _countLegConfigs.FirstOrDefault(row => string.Equals(
                    BuildCountLegKey(row.MapId, row.LegId),
                    selectedKey,
                    [redacted]))
              ?? _countLegConfigs.FirstOrDefault();

        CountSummary = $"Prepared count setup for {_countLegConfigs.Count} leg(s).";
        if (updateStatus)
        {
            StatusMessage = $"Prepared count setup for {_countLegConfigs.Count} leg(s).";
        }
    }

    private async Task ValidateCountsAsync(CancellationToken cancellationToken)
    {
        if (!HasDataverseConnection)
        {
            StatusMessage = "Dataverse profile is not configured for this environment.";
            return;
        }

        await RefreshCountSetupCoreAsync(cancellationToken, updateStatus: false);
        if (_countLegConfigs.Count == 0)
        {
            StatusMessage = "No count legs available for the selected maps.";
            CountSummary = "No count legs available for the selected maps.";
            return;
        }

        var legsToValidate = _countLegConfigs.Where(row => row.Include).ToList();
        if (legsToValidate.Count == 0)
        {
            StatusMessage = "No count legs included. Select at least one row in count setup.";
            CountSummary = "No count legs selected for validation.";
            return;
        }

        _countResults.Clear();
        CountSummary = UseExactCeCount
            ? $"Running count validation for {legsToValidate.Count} leg(s) [Exact CE]..."
            : $"Running count validation for {legsToValidate.Count} leg(s) [Fast CE]...";
        IsCounting = true;

        try
        {
            var dataverseHttp = _dataverse!.DataverseHttp!;
            var apiBase = [redacted](_dataverse.CurrentDataverseEnv!.BaseUrl);

            foreach (var legConfig in legsToValidate)
            {
                [redacted]();
                var ceEntity = legConfig.CeEntity;
                var foEntity = legConfig.FoEntityEffective;
                var foFilter = legConfig.FoFilter;
                var ceFilter = legConfig.CeFilter;
                var foFilterNote = legConfig.FoFilterNote;
                if (!string.IsNullOrWhiteSpace(foEntity))
                {
                    var converted = await ConvertSourceFilterToODataAsync(foEntity, legConfig.SourceFilterXpp, cancellationToken);
                    foFilter = converted.Filter;
                    foFilterNote = converted.Note;
                }

                long? ceCount = null;
                long? foCount = null;
                bool? match = null;
                var statusParts = new List<string>();

                if (!string.Equals(legConfig.SourceEnvironmentType, "AX", [redacted]) ||
                    !string.Equals([redacted], "CRM", [redacted]))
                {
                    statusParts.Add("Leg direction is not AX->CRM; using Source as FO and Destination as CE.");
                }

                if (string.IsNullOrWhiteSpace(ceEntity))
                {
                    statusParts.Add("Missing CE destination schema.");
                }
                else
                {
                    try
                    {
                        ceCount = UseExactCeCount
                            ? await GetDataverseExactCountAsync(dataverseHttp, apiBase, ceEntity, ceFilter, cancellationToken)
                            : await GetDataverseCountAsync(dataverseHttp, apiBase, ceEntity, ceFilter, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        statusParts.Add($"CE count failed: {ex.Message}");
                    }
                }

                if (string.IsNullOrWhiteSpace(foEntity))
                {
                    var schemaInfo = string.IsNullOrWhiteSpace([redacted])
                        ? legConfig.SourceSchema
                        : $"{[redacted]}' / '{legConfig.SourceSchema}";
                    statusParts.Add($"FO entity unresolved from source schema '{schemaInfo}'.");
                }
                else
                {
                    try
                    {
                        var foResult = await GetFoCountWithFallbackAsync(foEntity, foFilter, cancellationToken);
                        foCount = foResult.Count;
                        if (!string.IsNullOrWhiteSpace(foResult.Note))
                        {
                            statusParts.Add(foResult.Note);
                        }
                    }
                    catch (Exception ex)
                    {
                        statusParts.Add($"FO count failed: {ex.Message}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(foFilterNote))
                {
                    statusParts.Add(foFilterNote);
                }

                if (ceCount.HasValue && foCount.HasValue)
                {
                    match = ceCount.Value == foCount.Value;
                    if (match == false)
                    {
                        statusParts.Add("Counts differ.");
                    }
                }

                if (!UseExactCeCount && ceCount == 5000)
                {
                    statusParts.Add("CE count returned 5000 (possible API cap). Enable Exact CE Count for full value.");
                }

                var status = statusParts.Count == 0 ? "OK" : string.Join(" ", statusParts);
                _countResults.Add(new CountValidationRow(
                    legConfig.MapDisplayName,
                    legConfig.MapId,
                    legConfig.LegId,
                    foEntity,
                    foFilter,
                    ceEntity,
                    ceFilter,
                    foCount,
                    ceCount,
                    match,
                    status));
            }

            var matched = _countResults.Count(r => r.CountsMatch == true);
            var mismatched = _countResults.Count(r => r.CountsMatch == false);
            var incomplete = _countResults.Count - matched - mismatched;
            var modeText = UseExactCeCount ? "Exact CE" : "Fast CE";
            CountSummary = $"Validated {_countResults.Count} leg(s) [{modeText}]. Matched: {matched}. Mismatch: {mismatched}. Incomplete: {incomplete}.";
            StatusMessage = "Count validation finished.";
        }
        catch (OperationCanceledException) when ([redacted])
        {
            CountSummary = "Count validation cancelled.";
            StatusMessage = "Count validation cancelled.";
        }
        finally
        {
            IsCounting = false;
        }
    }

    private async Task EnsureFoEntityLookupAsync(CancellationToken cancellationToken)
    {
        if (_foEntityLookup is not null)
        {
            return;
        }

        var index = await [redacted](
            _ctx.CurrentEnv,
            [redacted],
            cancellationToken);

        var lookup = new Dictionary<string, string>([redacted]);
        var names = new HashSet<string>([redacted]);
        foreach (var entity in index.Entities)
        {
            names.Add(entity.Name);
            var key = NormalizeEntityKey(entity.Name);
            if (!lookup.ContainsKey(key))
            {
                lookup.Add(key, entity.Name);
            }
        }

        _foEntityLookup = lookup;
        _foEnumLookup = BuildEnumLookup(index.Enums);
        _foEntityDetailsCache.Clear();
        _foEntityFieldLookup.Clear();
        _foEntityEnumFields.Clear();
        _foEntityNames = names.OrderBy(v => v, [redacted]).ToList();
        RebuildFoEntityOptions();
    }

    private List<DualWriteMapRecord> GetMapsForCounting()
    {
        var selectedMaps = _records.Where(r => r.IsSelected).ToList();
        if (selectedMaps.Count == 0 && SelectedRecord is not null)
        {
            selectedMaps.Add(SelectedRecord);
        }

        return selectedMaps;
    }

    private static string BuildCountLegKey(string mapId, string legId) => $"{mapId}|{legId}";

    private void RebuildFoEntityOptions()
    {
        _foEntities.Clear();
        _foEntities.Add(FoEntityOption.Auto);
        if (_foEntityNames.Count == 0)
        {
            return;
        }

        foreach (var entity in _foEntityNames)
        {
            _foEntities.Add(new FoEntityOption(entity, entity));
        }
    }

    private void AttachCountLegConfig(CountLegConfigRow row)
    {
        row.PropertyChanged += OnCountLegConfigPropertyChanged;
    }

    private void ClearCountSetup()
    {
        foreach (var row in _countLegConfigs)
        {
            row.PropertyChanged -= OnCountLegConfigPropertyChanged;
        }

        _countLegConfigs.Clear();
        SelectedCountLegConfig = null;
        FoCountPreviewUrl = string.Empty;
        CeCountPreviewUrl = string.Empty;
    }

    private void OnCountLegConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CountLegConfigRow row || SelectedCountLegConfig != row)
        {
            return;
        }

        if (e.PropertyName == nameof([redacted]) ||
            e.PropertyName == nameof([redacted]) ||
            e.PropertyName == nameof(CountLegConfigRow.FoFilter) ||
            e.PropertyName == nameof(CountLegConfigRow.CeEntity) ||
            e.PropertyName == nameof(CountLegConfigRow.CeFilter))
        {
            RefreshCountPreviewUrls();
        }
    }

    private void RefreshCountPreviewUrls()
    {
        if (SelectedCountLegConfig is null)
        {
            FoCountPreviewUrl = string.Empty;
            CeCountPreviewUrl = string.Empty;
            return;
        }

        FoCountPreviewUrl = BuildFoCountPreviewUrl([redacted], SelectedCountLegConfig.FoFilter);
        CeCountPreviewUrl = BuildDataverseCountPreviewUrl(SelectedCountLegConfig.CeEntity, SelectedCountLegConfig.CeFilter);
    }

    private string BuildFoCountPreviewUrl(string foEntity, string? oDataFilter)
    {
        if (string.IsNullOrWhiteSpace(foEntity))
        {
            return "(FO entity unresolved)";
        }

        var spec = new QuerySpec(
            Entity: foEntity,
            Filter: string.IsNullOrWhiteSpace(oDataFilter) ? null : oDataFilter,
            Top: 1,
            Count: true,
            CrossCompany: true);

        return QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec).ToString();
    }

    private string BuildDataverseCountPreviewUrl(string ceEntity, string? oDataFilter)
    {
        if (!HasDataverseConnection)
        {
            return "(Dataverse profile not configured)";
        }

        if (string.IsNullOrWhiteSpace(ceEntity))
        {
            return "(CE entity not provided)";
        }

        var apiBase = [redacted](_dataverse!.CurrentDataverseEnv!.BaseUrl);
        if (UseExactCeCount)
        {
            return $"{BuildDataversePagedCountStartUrl(apiBase, ceEntity, oDataFilter)} [paged, prefer: odata.maxpagesize=5000]";
        }

        var query = new List<string> { "$top=1", "$count=true" };
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        return $"{apiBase}/{ceEntity}?{string.Join("&", query)}";
    }

    private async Task<(string Filter, string Note)> ConvertSourceFilterToODataAsync(
        string foEntity,
        string? xppFilter,
        CancellationToken cancellationToken)
    {
        var filter = ConvertXppFilterToOData(xppFilter, out var conversionNote);
        if (string.IsNullOrWhiteSpace(filter))
        {
            return (filter, conversionNote);
        }

        await EnsureFoEntityLookupAsync(cancellationToken);
        var fieldLookup = await GetFoEntityFieldLookupAsync(foEntity, cancellationToken);
        if (fieldLookup.Count > 0)
        {
            filter = NormalizeFilterFieldNames(filter, fieldLookup, out var fieldRenameCount);
            if (fieldRenameCount > 0)
            {
                conversionNote = AppendNote(conversionNote, $"Normalized {fieldRenameCount} field name(s) to FO entity property names.");
            }
        }

        var enumFields = await GetFoEntityEnumFieldLookupAsync(foEntity, cancellationToken);
        if (enumFields.Count == 0)
        {
            if (filter.Contains("::", StringComparison.Ordinal))
            {
                conversionNote = AppendNote(conversionNote, "Source filter still contains enum tokens (::); FO filter conversion may need manual adjustment.");
            }

            return (filter, conversionNote);
        }

        var replacements = 0;
        filter = Regex.Replace(
            filter,
            @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>eq|ne|gt|ge|lt|le)\s+(?<enum>[A-Za-z_][A-Za-z0-9_.]*)::(?<member>[A-Za-z_][A-Za-z0-9_]*)\b",
            m =>
            {
                var field = m.Groups["field"].Value;
                var op = m.Groups["op"].Value;
                var enumToken = m.Groups["enum"].Value;
                var memberToken = m.Groups["member"].Value;

                if (!enumFields.TryGetValue(field, out var enumType))
                {
                    enumType = ResolveEnumType(_foEnumLookup, enumToken);
                    if (enumType is null)
                    {
                        return m.Value;
                    }
                }

                var member = ResolveEnumMember(enumType, memberToken);
                if (string.IsNullOrWhiteSpace(member))
                {
                    return m.Value;
                }

                replacements++;
                return $"{field} {op} {enumType.Name}'{EscapeSingleQuoted(member)}'";
            },
            RegexOptions.IgnoreCase);

        filter = Regex.Replace(
            filter,
            @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>eq|ne|gt|ge|lt|le)\s+'(?<value>[^']*)'",
            m =>
            {
                var field = m.Groups["field"].Value;
                if (!enumFields.TryGetValue(field, out var enumType))
                {
                    return m.Value;
                }

                var valueToken = m.Groups["value"].Value.Replace("''", "'", StringComparison.Ordinal);
                var member = ResolveEnumMember(enumType, valueToken);
                if (string.IsNullOrWhiteSpace(member))
                {
                    return m.Value;
                }

                replacements++;
                var op = m.Groups["op"].Value;
                return $"{field} {op} {enumType.Name}'{EscapeSingleQuoted(member)}'";
            },
            RegexOptions.IgnoreCase);

        if (replacements > 0)
        {
            conversionNote = AppendNote(conversionNote, $"Applied enum metadata conversion on {replacements} condition(s).");
        }

        if (filter.Contains("::", StringComparison.Ordinal))
        {
            conversionNote = AppendNote(conversionNote, "Source filter still contains enum tokens (::); FO filter conversion may need manual adjustment.");
        }

        return (Regex.Replace(filter, @"\s+", " ").Trim(), conversionNote);
    }

    private async Task<Dictionary<string, ODataEnumType>> GetFoEntityEnumFieldLookupAsync(string foEntity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(foEntity))
        {
            return new Dictionary<string, ODataEnumType>([redacted]);
        }

        if (_foEntityEnumFields.TryGetValue(foEntity, out var cached))
        {
            return cached;
        }

        var details = await GetFoEntityDetailsCachedAsync(foEntity, cancellationToken);

        var lookup = new Dictionary<string, ODataEnumType>([redacted]);
        if (details is not null)
        {
            foreach (var property in details.Properties)
            {
                var enumType = ResolveEnumType(_foEnumLookup, property.Type);
                if (enumType is null)
                {
                    continue;
                }

                lookup[property.Name] = enumType;
            }
        }

        _foEntityEnumFields[foEntity] = lookup;
        return lookup;
    }

    private async Task<Dictionary<string, string>> GetFoEntityFieldLookupAsync(string foEntity, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(foEntity))
        {
            return new Dictionary<string, string>([redacted]);
        }

        if ([redacted](foEntity, out var cached))
        {
            return cached;
        }

        var details = await GetFoEntityDetailsCachedAsync(foEntity, cancellationToken);
        var lookup = new Dictionary<string, string>([redacted]);
        if (details is not null)
        {
            foreach (var property in details.Properties)
            {
                var key = NormalizeEntityKey(property.Name);
                if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
                {
                    lookup.Add(key, property.Name);
                }
            }
        }

        _foEntityFieldLookup[foEntity] = lookup;
        return lookup;
    }

    private async Task<ODataEntity?> GetFoEntityDetailsCachedAsync(string foEntity, CancellationToken cancellationToken)
    {
        if ([redacted](foEntity, out var cached))
        {
            return cached;
        }

        var details = await [redacted](
            _ctx.CurrentEnv,
            foEntity,
            [redacted],
            cancellationToken);

        details ??= await [redacted](
            _ctx.CurrentEnv,
            foEntity,
            [redacted],
            cancellationToken);

        _foEntityDetailsCache[foEntity] = details;
        return details;
    }

    private static Dictionary<string, ODataEnumType> BuildEnumLookup(IReadOnlyList<ODataEnumType> enums)
    {
        var lookup = new Dictionary<string, ODataEnumType>([redacted]);
        foreach (var enumType in enums)
        {
            if (!lookup.ContainsKey(enumType.Name))
            {
                lookup.Add(enumType.Name, enumType);
            }

            var shortName = enumType.Name.Split('.').LastOrDefault();
            if (!string.IsNullOrWhiteSpace(shortName) && !lookup.ContainsKey(shortName))
            {
                lookup.Add(shortName, enumType);
            }
        }

        return lookup;
    }

    private static ODataEnumType? ResolveEnumType(Dictionary<string, ODataEnumType> lookup, string type)
    {
        if (lookup.Count == 0 || string.IsNullOrWhiteSpace(type))
        {
            return null;
        }

        var normalized = type;
        if (normalized.StartsWith("Collection(", [redacted]) &&
            normalized.EndsWith(")", [redacted]))
        {
            normalized = normalized.Substring("Collection(".Length, normalized.Length - "Collection(".Length - 1);
        }

        if (lookup.TryGetValue(normalized, out var enumType))
        {
            return enumType;
        }

        var shortName = normalized.Split('.').LastOrDefault();
        if (!string.IsNullOrWhiteSpace(shortName) && lookup.TryGetValue(shortName, out enumType))
        {
            return enumType;
        }

        return null;
    }

    private static string NormalizeFilterFieldNames(
        string filter,
        Dictionary<string, string> fieldLookup,
        out int replacementCount)
    {
        var replacements = 0;
        if (string.IsNullOrWhiteSpace(filter) || fieldLookup.Count == 0)
        {
            replacementCount = 0;
            return filter;
        }

        var normalized = Regex.Replace(
            filter,
            @"\b(?<field>[A-Za-z_][A-Za-z0-9_]*)\s+(?<op>eq|ne|gt|ge|lt|le)\b",
            m =>
            {
                var field = m.Groups["field"].Value;
                var normalized = NormalizeEntityKey(field);
                if (string.IsNullOrWhiteSpace(normalized) || !fieldLookup.TryGetValue(normalized, out var actual))
                {
                    return m.Value;
                }

                if (string.Equals(field, actual, StringComparison.Ordinal))
                {
                    return m.Value;
                }

                replacements++;
                return $"{actual} {m.Groups["op"].Value}";
            },
            RegexOptions.IgnoreCase);

        replacementCount = replacements;
        return normalized;
    }

    private static string? ResolveEnumMember(ODataEnumType enumType, string member)
    {
        if (string.IsNullOrWhiteSpace(member))
        {
            return null;
        }

        return enumType.Members.FirstOrDefault(m => string.Equals(m, member, [redacted]));
    }

    private static string EscapeSingleQuoted(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string AppendNote(string current, string next)
    {
        if (string.IsNullOrWhiteSpace(next))
        {
            return current;
        }

        if (string.IsNullOrWhiteSpace(current))
        {
            return next;
        }

        return $"{current} {next}";
    }

    private string ResolveFoEntityName(params string?[] sourceSchemas)
    {
        if (_foEntityLookup is null || _foEntityLookup.Count == 0 || _foEntityNames.Count == 0)
        {
            return string.Empty;
        }

        foreach (var sourceSchema in sourceSchemas)
        {
            var resolved = ResolveFoEntityNameSingle(sourceSchema);
            if (!string.IsNullOrWhiteSpace(resolved))
            {
                return resolved;
            }
        }

        return string.Empty;
    }

    private string ResolveFoEntityNameSingle(string? sourceSchema)
    {
        if (string.IsNullOrWhiteSpace(sourceSchema))
        {
            return string.Empty;
        }

        var aliases = BuildNormalizedAliases(sourceSchema);
        foreach (var alias in aliases)
        {
            if (_foEntityLookup!.TryGetValue(alias, out var direct))
            {
                return direct;
            }
        }

        var sourceTokens = TokenizeName(sourceSchema)
            .Where(t => !StopTokens.Contains(t))
            .ToList();
        if (sourceTokens.Count == 0)
        {
            sourceTokens = TokenizeName(sourceSchema).ToList();
        }

        var ranked = new List<(string Name, int Score)>(_foEntityNames.Count);
        foreach (var entityName in _foEntityNames)
        {
            var score = ScoreEntityName(entityName, aliases, sourceTokens);
            if (score > int.MinValue)
            {
                ranked.Add((entityName, score));
            }
        }

        if (ranked.Count == 0)
        {
            return string.Empty;
        }

        var best = ranked.OrderByDescending(r => r.Score).First();
        if (best.Score < 110)
        {
            return string.Empty;
        }

        var second = ranked
            .Where(r => !string.Equals(r.Name, best.Name, [redacted]))
            .OrderByDescending(r => r.Score)
            .FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(second.Name) && second.Score >= best.Score - 8)
        {
            return string.Empty;
        }

        return best.Name;
    }

    private static readonly HashSet<string> StopTokens = new([redacted])
    {
        "cds", "dynamics", "d365", "entity", "entities", "the", "of", "and", "for", "data"
    };

    private static List<string> BuildNormalizedAliases(string sourceSchema)
    {
        var aliases = new HashSet<string>([redacted]);
        var raw = sourceSchema.Trim();
        var withoutParen = Regex.Replace(raw, @"\([^)]*\)", " ");
        var tokens = TokenizeName(withoutParen).ToList();
        var filtered = tokens.Where(t => !StopTokens.Contains(t)).ToList();

        AddAlias(aliases, raw);
        AddAlias(aliases, withoutParen);
        AddAlias(aliases, string.Concat(filtered));
        AddAlias(aliases, string.Concat(filtered.Where(t => !Regex.IsMatch(t, @"^v\d+$", RegexOptions.IgnoreCase))));
        AddAlias(aliases, string.Concat(filtered.Select(t => t.StartsWith("v", [redacted]) && t.Length > 1 ? t[1..] : t)));

        return aliases.ToList();
    }

    private static void AddAlias(HashSet<string> aliases, string candidate)
    {
        var normalized = NormalizeEntityKey(candidate);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            aliases.Add(normalized);
        }
    }

    private static int ScoreEntityName(string entityName, IReadOnlyList<string> aliases, IReadOnlyList<string> sourceTokens)
    {
        var entityNorm = NormalizeEntityKey(entityName);
        if (string.IsNullOrWhiteSpace(entityNorm))
        {
            return int.MinValue;
        }

        var bestScore = int.MinValue;
        foreach (var alias in aliases)
        {
            var score = 0;
            if (string.Equals(entityNorm, alias, [redacted]))
            {
                score += 220;
            }
            else if (entityNorm.StartsWith(alias, [redacted]) ||
                     alias.StartsWith(entityNorm, [redacted]))
            {
                score += 130;
            }
            else if (entityNorm.Contains(alias, [redacted]) ||
                     alias.Contains(entityNorm, [redacted]))
            {
                score += 90;
            }

            score -= Math.Abs(entityNorm.Length - alias.Length);
            bestScore = Math.Max(bestScore, score);
        }

        var entityTokens = TokenizeName(entityName)
            .Where(t => !StopTokens.Contains(t))
            .ToList();

        if (entityTokens.Count > 0 && sourceTokens.Count > 0)
        {
            var overlap = entityTokens.Intersect(sourceTokens, [redacted]).Count();
            bestScore += overlap * 28;

            if (string.Equals(entityTokens[0], sourceTokens[0], [redacted]))
            {
                bestScore += 20;
            }
        }

        return bestScore;
    }

    private static IEnumerable<string> TokenizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var withBoundaries = Regex.Replace(value, @"([a-z])([A-Z])", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"([A-Za-z])(\d)", "$1 $2");
        withBoundaries = Regex.Replace(withBoundaries, @"(\d)([A-Za-z])", "$1 $2");

        foreach (Match match in Regex.Matches(withBoundaries, @"[A-Za-z0-9]+"))
        {
            var token = match.Value.Trim();
            if (!string.IsNullOrWhiteSpace(token))
            {
                yield return token.ToLowerInvariant();
            }
        }
    }

    private static string NormalizeEntityKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray();
        return new string(chars);
    }

    private async Task<(long? Count, string Note)> GetFoCountWithFallbackAsync(string foEntity, string? oDataFilter, CancellationToken cancellationToken)
    {
        var candidates = BuildFoFilterCandidates(oDataFilter);
        Exception? lastException = null;

        for (var i = 0; i < candidates.Count; i++)
        {
            [redacted]();
            var candidate = candidates[i];
            try
            {
                var count = await GetFoCountAsync(foEntity, candidate, cancellationToken);
                if (i == 0)
                {
                    return (count, string.Empty);
                }

                return (count, $"FO filter fallback variant {i + 1} of {candidates.Count} succeeded.");
            }
            catch (Exception ex) when (i < candidates.Count - 1 && IsHttp400(ex))
            {
                lastException = ex;
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        if (lastException is not null)
        {
            var attempted = string.Join(" || ", candidates
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => TrimForStatus(c!))
                .Take(6));
            throw new InvalidOperationException(
                $"FO count failed after {candidates.Count} filter variant(s). Last error: {lastException.Message} Attempted filters: {attempted}",
                lastException);
        }

        return (null, string.Empty);
    }

    private static List<string?> BuildFoFilterCandidates(string? oDataFilter)
    {
        var candidates = new List<string?> { oDataFilter };

        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            var boolNoYes = oDataFilter;
            boolNoYes = Regex.Replace(
                boolNoYes,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'Yes'",
                "true",
                RegexOptions.IgnoreCase);
            boolNoYes = Regex.Replace(
                boolNoYes,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'No'",
                "false",
                RegexOptions.IgnoreCase);
            if (!string.Equals(boolNoYes, oDataFilter, StringComparison.Ordinal))
            {
                candidates.Add(boolNoYes);
            }

            var plainMember = Regex.Replace(
                oDataFilter,
                @"\b[A-Za-z_][A-Za-z0-9_.]*'([A-Za-z_][A-Za-z0-9_]*)'",
                "'$1'");
            if (!string.Equals(plainMember, oDataFilter, StringComparison.Ordinal))
            {
                candidates.Add(plainMember);
            }

            var partyTypeTyped = Regex.Replace(
                oDataFilter,
                @"\bPartyType\s+(eq|ne)\s+'([A-Za-z_][A-Za-z0-9_]*)'",
                "PartyType $1 [redacted]'$2'",
                RegexOptions.IgnoreCase);
            if (!string.Equals(partyTypeTyped, oDataFilter, StringComparison.Ordinal))
            {
                candidates.Add(partyTypeTyped);
            }

            var partyTypeBoolNoYes = Regex.Replace(
                partyTypeTyped,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'Yes'",
                "true",
                RegexOptions.IgnoreCase);
            partyTypeBoolNoYes = Regex.Replace(
                partyTypeBoolNoYes,
                @"Microsoft\.Dynamics\.DataEntities\.NoYes'No'",
                "false",
                RegexOptions.IgnoreCase);
            if (!string.Equals(partyTypeBoolNoYes, partyTypeTyped, StringComparison.Ordinal))
            {
                candidates.Add(partyTypeBoolNoYes);
            }
        }

        return candidates
            .Distinct([redacted])
            .ToList();
    }

    private static bool IsHttp400(Exception ex)
    {
        var text = ex.ToString();
        return text.Contains(" 400 ", [redacted]) ||
               text.Contains("400 (", [redacted]) ||
               text.Contains("StatusCode: 400", [redacted]);
    }

    private async Task<long?> GetFoCountAsync(string foEntity, string? oDataFilter, CancellationToken cancellationToken)
    {
        var spec = new QuerySpec(
            Entity: foEntity,
            Filter: string.IsNullOrWhiteSpace(oDataFilter) ? null : oDataFilter,
            Top: 1,
            Count: true,
            CrossCompany: true);

        var request = QueryBuilder.Build(_ctx.CurrentEnv.BaseUrl, spec);
        await foreach (var page in _ctx.OData.StreamAsync(request, cancellationToken))
        {
            return page.ODataCount ?? page.Rows.Count;
        }

        return null;
    }

    private static async Task<long?> GetDataverseCountAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        CancellationToken cancellationToken)
    {
        var query = new List<string> { "$top=1", "$count=true" };
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        var url = $"{apiBase}/{entitySetName}?{string.Join("&", query)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        [redacted]("Prefer", "odata.maxpagesize=1");

        using var response = await dataverseHttp.SendAsync(request, [redacted], cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var body = await [redacted](cancellationToken);
            throw new InvalidOperationException(
                $"Dataverse count request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
        }

        await using var stream = await [redacted](cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (root.TryGetProperty("@odata.count", out var countElement))
        {
            if (countElement.ValueKind == JsonValueKind.Number && countElement.TryGetInt64(out var number))
            {
                return number;
            }

            if (countElement.ValueKind == JsonValueKind.String && long.TryParse(countElement.GetString(), out var parsed))
            {
                return parsed;
            }
        }

        return null;
    }

    private static async Task<long?> GetDataverseExactCountAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        CancellationToken cancellationToken)
    {
        // Exact mode is a true page walk to avoid the Dataverse 5,000 row count ceiling behavior.
        return await GetDataversePagedCountAsync(dataverseHttp, apiBase, entitySetName, oDataFilter, cancellationToken);
    }

    private static async Task<long?> GetDataversePagedCountAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string entitySetName,
        string? oDataFilter,
        CancellationToken cancellationToken)
    {
        var nextLink = BuildDataversePagedCountStartUrl(apiBase, entitySetName, oDataFilter);
        long total = 0;
        while (!string.IsNullOrWhiteSpace(nextLink))
        {
            [redacted]();

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            [redacted]("Prefer", "odata.maxpagesize=5000");

            using var response = await dataverseHttp.SendAsync(request, [redacted], cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var body = await [redacted](cancellationToken);
                throw new InvalidOperationException(
                    $"Dataverse exact count paging failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
            }

            await using var stream = await [redacted](cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Dataverse exact count paging response did not contain a 'value' array.");
            }

            total += valueArray.GetArrayLength();
            nextLink = GetValueAsString(root, "@odata.nextLink");
        }

        return total;
    }

    private static string BuildDataversePagedCountStartUrl(string apiBase, string entitySetName, string? oDataFilter)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(oDataFilter))
        {
            query.Add($"$filter={Uri.EscapeDataString(oDataFilter)}");
        }

        var baseUrl = $"{apiBase}/{entitySetName}";
        return query.Count == 0 ? baseUrl : $"{baseUrl}?{string.Join("&", query)}";
    }

    private static string ConvertXppFilterToOData(string? xppFilter, out string conversionNote)
    {
        conversionNote = string.Empty;
        if (string.IsNullOrWhiteSpace(xppFilter))
        {
            return string.Empty;
        }

        var source = xppFilter.Trim();
        var output = new System.Text.StringBuilder(source.Length * 2);
        var inString = false;

        for (var i = 0; i < source.Length; i++)
        {
            var ch = source[i];

            if (ch == '"')
            {
                inString = !inString;
                output.Append('\'');
                continue;
            }

            if (inString)
            {
                output.Append(ch == '\'' ? "''" : ch);
                continue;
            }

            if (ch == '&' && i + 1 < source.Length && source[i + 1] == '&')
            {
                output.Append(" and ");
                i++;
                continue;
            }

            if (ch == '|' && i + 1 < source.Length && source[i + 1] == '|')
            {
                output.Append(" or ");
                i++;
                continue;
            }

            if (ch == '=' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" eq ");
                i++;
                continue;
            }

            if (ch == '=')
            {
                output.Append(" eq ");
                continue;
            }

            if (ch == '!' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" ne ");
                i++;
                continue;
            }

            if (ch == '>' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" ge ");
                i++;
                continue;
            }

            if (ch == '<' && i + 1 < source.Length && source[i + 1] == '=')
            {
                output.Append(" le ");
                i++;
                continue;
            }

            if (ch == '>')
            {
                output.Append(" gt ");
                continue;
            }

            if (ch == '<')
            {
                output.Append(" lt ");
                continue;
            }

            if (ch is '\r' or '\n' or '\t')
            {
                output.Append(' ');
                continue;
            }

            output.Append(ch);
        }

        var converted = output.ToString();

        return Regex.Replace(converted, @"\s+", " ").Trim();
    }

    private void ClearRecords()
    {
        _records.Clear();
        SelectedRecord = null;
        RecordsView.Refresh();
        UpdateRecordSummary();
        ClearCountSetup();
        _countResults.Clear();
        ClearTestifyState();
        CountSummary = "No count run yet.";
        StatusMessage = "Cleared.";
    }

    private bool RecordFilter(object? item)
    {
        if (item is not DualWriteMapRecord record)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        var term = SearchText.Trim();
        return record.Name.Contains(term, [redacted])
            || record.DisplayName.Contains(term, [redacted])
            || record.Version.Contains(term, [redacted])
            || record.State.Contains(term, [redacted])
            || record.Status.Contains(term, [redacted])
            || record.Owner.Contains(term, [redacted]);
    }

    private void UpdateRecordSummary()
    {
        var visible = RecordsView.Cast<object>().Count();
        RecordSummary = $"Showing {visible} of {_records.Count} records";
    }

    private static DualWriteMapRecord ParseRecord(JsonElement item)
    {
        var stateName = GetValueAsString(item, "statecode@[redacted]")
            ?? GetValueAsString(item, "statecodename")
            ?? GetValueAsString(item, "statecode")
            ?? string.Empty;

        var statusName = GetValueAsString(item, "statuscode@[redacted]")
            ?? GetValueAsString(item, "statuscodename")
            ?? GetValueAsString(item, "statuscode")
            ?? string.Empty;

        var owner = GetValueAsString(item, "_ownerid_value@[redacted]")
            ?? GetValueAsString(item, "owneridname")
            ?? GetValueAsString(item, "_ownerid_value")
            ?? GetValueAsString(item, "ownerid")
            ?? string.Empty;

        var mappingRaw = GetValueAsString(item, "msdyn_mapping");
        var propertiesRaw = GetValueAsString(item, "msdyn_properties");
        var mappingRoot = TryParseJsonElement(mappingRaw);
        var propertiesRoot = TryParseJsonElement(propertiesRaw);

        return new DualWriteMapRecord(
            id: GetValueAsString(item, "msdyn_dualwriteentitymapid") ?? string.Empty,
            solutionId: GetValueAsString(item, "solutionid") ?? string.Empty,
            name: GetValueAsString(item, "msdyn_name") ?? string.Empty,
            displayName: GetValueAsString(item, "msdyn_displayname") ?? string.Empty,
            version: GetValueAsString(item, "msdyn_version") ?? string.Empty,
            state: stateName,
            status: statusName,
            owner: owner,
            createdOn: ParseDate(GetValueAsString(item, "createdon")),
            modifiedOn: ParseDate(GetValueAsString(item, "modifiedon")),
            mappingRows: BuildFlattenedRows(mappingRoot, mappingRaw),
            mappingSummaryRows: BuildMappingSummaryRows(mappingRoot),
            mappingLegRows: BuildMappingLegRows(mappingRoot),
            mappingFieldRows: BuildMappingFieldRows(mappingRoot),
            mappingValueTransformRows: BuildMappingValueTransformRows(mappingRoot),
            propertiesRows: BuildPropertiesRows(propertiesRoot, propertiesRaw),
            mappingRaw: mappingRaw,
            propertiesRaw: propertiesRaw);
    }

    private static string? GetValueAsString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.Null => null,
            JsonValueKind.String => value.GetString(),
            JsonValueKind.True => bool.TrueString,
            JsonValueKind.False => bool.FalseString,
            _ => value.ToString()
        };
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | [redacted],
            out var parsed))
        {
            return parsed.ToUniversalTime();
        }

        return null;
    }

    private static JsonElement? TryParseJsonElement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (!trimmed.StartsWith("{", System.StringComparison.Ordinal) &&
            !trimmed.StartsWith("[", System.StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(trimmed);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyList<JsonTableRow> BuildFlattenedRows(JsonElement? root, string? fallbackRaw)
    {
        if (root is null)
        {
            if (string.IsNullOrWhiteSpace(fallbackRaw))
            {
                return Array.Empty<JsonTableRow>();
            }

            return new[] { new JsonTableRow("$", "String", fallbackRaw) };
        }

        var rows = new List<JsonTableRow>();
        AppendJsonRows(root.Value, "$", rows);
        return rows;
    }

    private static void AppendJsonRows(JsonElement element, string path, List<JsonTableRow> rows)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var hasProperties = false;
                foreach (var property in element.EnumerateObject())
                {
                    hasProperties = true;
                    var childPath = path == "$" ? $"$.{property.Name}" : $"{path}.{property.Name}";
                    AppendJsonRows(property.Value, childPath, rows);
                }

                if (!hasProperties)
                {
                    rows.Add(new JsonTableRow(path, "Object", "{}"));
                }
                break;
            }
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AppendJsonRows(item, $"{path}[{index}]", rows);
                    index++;
                }

                if (index == 0)
                {
                    rows.Add(new JsonTableRow(path, "Array", "[]"));
                }
                break;
            }
            case JsonValueKind.String:
                rows.Add(new JsonTableRow(path, "String", element.GetString() ?? string.Empty));
                break;
            case JsonValueKind.Number:
                rows.Add(new JsonTableRow(path, "Number", element.ToString()));
                break;
            case JsonValueKind.True:
            case JsonValueKind.False:
                rows.Add(new JsonTableRow(path, "Boolean", element.GetBoolean() ? "true" : "false"));
                break;
            case JsonValueKind.Null:
                rows.Add(new JsonTableRow(path, "Null", "null"));
                break;
            default:
                rows.Add(new JsonTableRow(path, element.ValueKind.ToString(), element.ToString()));
                break;
        }
    }

    private static IReadOnlyList<MappingSummaryRow> BuildMappingSummaryRows(JsonElement? mappingRoot)
    {
        if (mappingRoot is null || mappingRoot.Value.ValueKind != JsonValueKind.Object)
        {
            return Array.Empty<MappingSummaryRow>();
        }

        var rows = new List<MappingSummaryRow>();
        foreach (var property in [redacted]())
        {
            if (property.NameEquals("legs"))
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    rows.Add(new MappingSummaryRow("legs.count", property.Value.GetArrayLength().ToString(CultureInfo.InvariantCulture)));
                }
                continue;
            }

            if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                continue;
            }

            rows.Add(new MappingSummaryRow(property.Name, GetPrimitiveValue(property.Value)));
        }

        return rows;
    }

    private static IReadOnlyList<MappingLegRow> BuildMappingLegRows(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<MappingLegRow>();
        }

        var rows = new List<MappingLegRow>();
        foreach (var leg in legs.EnumerateArray())
        {
            var fieldCount = 0;
            if (leg.TryGetProperty("fieldMappings", out var fieldMappings) && fieldMappings.ValueKind == JsonValueKind.Array)
            {
                fieldCount = fieldMappings.GetArrayLength();
            }

            rows.Add(new MappingLegRow(
                legId: GetJsonString(leg, "id"),
                sourceSchema: GetJsonString(leg, "sourceSchema"),
                sourceSchemaDistinctName: GetJsonString(leg, "sourceSchemaDistinctName"),
                destinationSchema: GetJsonString(leg, "destinationSchema"),
                sourceEnvironmentType: GetJsonString(leg, "sourceEnvironmentType"),
                destinationEnvironmentType: GetJsonString(leg, "destinationEnvironmentType"),
                sourceFilter: GetJsonString(leg, "sourceFilter"),
                reversedSourceFilter: GetJsonString(leg, "reversedSourceFilter"),
                fieldMappings: fieldCount));
        }

        return rows;
    }

    private static IReadOnlyList<MappingFieldRow> BuildMappingFieldRows(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<MappingFieldRow>();
        }

        var rows = new List<MappingFieldRow>();
        foreach (var leg in legs.EnumerateArray())
        {
            var legId = GetJsonString(leg, "id");
            var sourceSchema = GetJsonString(leg, "sourceSchema");
            var destinationSchema = GetJsonString(leg, "destinationSchema");

            if (!leg.TryGetProperty("fieldMappings", out var fieldMappings) || fieldMappings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                var syncDirection = mapping.TryGetProperty("syncDirection", out var dir)
                    ? dir.ToString()
                    : string.Empty;

                var valueTransforms = 0;
                if (mapping.TryGetProperty("valueTransforms", out var transforms) && transforms.ValueKind == JsonValueKind.Array)
                {
                    valueTransforms = transforms.GetArrayLength();
                }

                rows.Add(new MappingFieldRow(
                    legId: legId,
                    sourceSchema: sourceSchema,
                    destinationSchema: destinationSchema,
                    syncDirection: syncDirection,
                    sourceField: GetJsonString(mapping, "sourceField"),
                    destinationField: GetJsonString(mapping, "destinationField"),
                    destinationLookupEntity: GetJsonString(mapping, "destinationLookupFieldRelatedEntity"),
                    isSystemGenerated: GetJsonBool(mapping, "isSystemGenerated"),
                    valueTransforms: valueTransforms));
            }
        }

        return rows;
    }

    private static IReadOnlyList<MappingValueTransformRow> BuildMappingValueTransformRows(JsonElement? mappingRoot)
    {
        if (!TryGetLegsArray(mappingRoot, out var legs))
        {
            return Array.Empty<MappingValueTransformRow>();
        }

        var rows = new List<MappingValueTransformRow>();
        foreach (var leg in legs.EnumerateArray())
        {
            var legId = GetJsonString(leg, "id");
            if (!leg.TryGetProperty("fieldMappings", out var fieldMappings) || fieldMappings.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var mapping in fieldMappings.EnumerateArray())
            {
                var sourceField = GetJsonString(mapping, "sourceField");
                var destinationField = GetJsonString(mapping, "destinationField");

                if (!mapping.TryGetProperty("valueTransforms", out var transforms) || transforms.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var transform in transforms.EnumerateArray())
                {
                    var valueMap = string.Empty;
                    if (transform.TryGetProperty("valueMap", out var valueMapElement) &&
                        valueMapElement.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        valueMap = JsonSerializer.Serialize(valueMapElement);
                    }

                    rows.Add(new MappingValueTransformRow(
                        legId: legId,
                        sourceField: sourceField,
                        destinationField: destinationField,
                        transformType: GetJsonString(transform, "transformType"),
                        defaultValue: GetJsonString(transform, "defaultValue"),
                        valueMap: valueMap,
                        createValuesOnDestination: GetJsonBool(transform, "createValuesOnDestination")));
                }
            }
        }

        return rows;
    }

    private static IReadOnlyList<PropertyTableRow> BuildPropertiesRows(JsonElement? propertiesRoot, string? fallbackRaw)
    {
        if (propertiesRoot is null)
        {
            if (string.IsNullOrWhiteSpace(fallbackRaw))
            {
                return Array.Empty<PropertyTableRow>();
            }

            return new[] { new PropertyTableRow("$", "String", fallbackRaw) };
        }

        var root = propertiesRoot.Value;
        if (root.ValueKind == JsonValueKind.Object)
        {
            var rows = new List<PropertyTableRow>();
            foreach (var property in root.EnumerateObject())
            {
                var value = property.Value;
                rows.Add(new PropertyTableRow(
                    key: property.Name,
                    type: value.ValueKind.ToString(),
                    value: value.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                        ? JsonSerializer.Serialize(value)
                        : GetPrimitiveValue(value)));
            }

            return rows;
        }

        return new[]
        {
            new PropertyTableRow("$", root.ValueKind.ToString(), root.ValueKind is JsonValueKind.Object or JsonValueKind.Array
                ? JsonSerializer.Serialize(root)
                : GetPrimitiveValue(root))
        };
    }

    private static bool TryGetLegsArray(JsonElement? mappingRoot, out JsonElement legs)
    {
        legs = default;
        if (mappingRoot is null || mappingRoot.Value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (![redacted]("legs", out legs) || legs.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        return true;
    }

    private static string GetPrimitiveValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Null => string.Empty,
            JsonValueKind.String => element.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => element.ToString()
        };
    }

    private static string GetJsonString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return GetPrimitiveValue(value);
    }

    private static bool? GetJsonBool(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.True)
        {
            return true;
        }

        if (value.ValueKind == JsonValueKind.False)
        {
            return false;
        }

        return null;
    }

    private static string BuildMapsUrl(string apiBase)
    {
        var queryParts = new List<string>
        {
            $"$select={Uri.EscapeDataString(SelectColumns)}",
            "$orderby=modifiedon%20desc"
        };

        return $"{apiBase}/msdyn_dualwriteentitymaps?{string.Join("&", queryParts)}";
    }

    private static async Task<HashSet<Guid>> LoadDualWriteComponentIdsForSolutionAsync(
        HttpClient dataverseHttp,
        string apiBase,
        string solutionUniqueName,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<Guid>();
        var escapedSolution = EscapeODataString(solutionUniqueName);
        var filter = $"(componenttype eq {DualWriteMapComponentType}) and (solutionid/uniquename eq '{escapedSolution}')";
        var nextLink = $"{apiBase}/solutioncomponents?$select=objectid&$filter={Uri.EscapeDataString(filter)}";

        while (!string.IsNullOrWhiteSpace(nextLink))
        {
            [redacted]();

            using var request = new HttpRequestMessage(HttpMethod.Get, nextLink);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            [redacted]("Prefer", "odata.maxpagesize=500");

            using var response = await dataverseHttp.SendAsync(
                request,
                [redacted],
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var body = await [redacted](cancellationToken);
                throw new InvalidOperationException(
                    $"Dataverse solutioncomponents request failed: {(int)response.StatusCode} {response.ReasonPhrase}. {TrimForStatus(body)}");
            }

            await using var stream = await [redacted](cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var root = document.RootElement;
            if (!root.TryGetProperty("value", out var valueArray) || valueArray.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Dataverse solutioncomponents response did not contain a 'value' array.");
            }

            foreach (var component in valueArray.EnumerateArray())
            {
                var objectId = GetValueAsString(component, "objectid");
                if (Guid.TryParse(objectId, out var parsed))
                {
                    ids.Add(parsed);
                }
            }

            nextLink = GetValueAsString(root, "@odata.nextLink");
        }

        return ids;
    }

    private static string EscapeODataString(string value) => value.Replace("'", "''", StringComparison.Ordinal);

    private static string TrimForStatus(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var compact = text.Replace("\r", string.Empty).Replace("\n", " ").Trim();
        if (compact.Length <= 280)
        {
            return compact;
        }

        return compact[..280] + "...";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class DualWriteMapRecord
{
    public DualWriteMapRecord(
        string id,
        string solutionId,
        string name,
        string displayName,
        string version,
        string state,
        string status,
        string owner,
        DateTimeOffset? createdOn,
        DateTimeOffset? modifiedOn,
        IReadOnlyList<JsonTableRow> mappingRows,
        IReadOnlyList<MappingSummaryRow> mappingSummaryRows,
        IReadOnlyList<MappingLegRow> mappingLegRows,
        IReadOnlyList<MappingFieldRow> mappingFieldRows,
        IReadOnlyList<MappingValueTransformRow> mappingValueTransformRows,
        IReadOnlyList<PropertyTableRow> propertiesRows,
        string? mappingRaw,
        string? propertiesRaw)
    {
        Id = id;
        SolutionId = solutionId;
        Name = name;
        DisplayName = displayName;
        Version = version;
        State = state;
        Status = status;
        Owner = owner;
        CreatedOn = createdOn;
        ModifiedOn = modifiedOn;
        MappingRows = mappingRows;
        MappingSummaryRows = mappingSummaryRows;
        MappingLegRows = mappingLegRows;
        MappingFieldRows = mappingFieldRows;
        MappingValueTransformRows = mappingValueTransformRows;
        PropertiesRows = propertiesRows;
        MappingRaw = mappingRaw;
        PropertiesRaw = propertiesRaw;
    }

    public string Id { get; }
    public string SolutionId { get; }
    public string Name { get; }
    public string DisplayName { get; }
    public string Version { get; }
    public string State { get; }
    public string Status { get; }
    public string Owner { get; }
    public DateTimeOffset? CreatedOn { get; }
    public DateTimeOffset? ModifiedOn { get; }
    public IReadOnlyList<JsonTableRow> MappingRows { get; }
    public IReadOnlyList<MappingSummaryRow> MappingSummaryRows { get; }
    public IReadOnlyList<MappingLegRow> MappingLegRows { get; }
    public IReadOnlyList<MappingFieldRow> MappingFieldRows { get; }
    public IReadOnlyList<MappingValueTransformRow> MappingValueTransformRows { get; }
    public IReadOnlyList<PropertyTableRow> PropertiesRows { get; }
    public string? MappingRaw { get; }
    public string? PropertiesRaw { get; }
    public bool IsSelected { get; set; }
    public string CreatedOnDisplay => FormatDate(CreatedOn);
    public string ModifiedOnDisplay => FormatDate(ModifiedOn);

    private static string FormatDate(DateTimeOffset? value)
    {
        return value?.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) ?? string.Empty;
    }
}

public sealed class MappingSummaryRow
{
    public MappingSummaryRow(string key, string value)
    {
        Key = key;
        Value = value;
    }

    public string Key { get; }
    public string Value { get; }
}

public sealed class MappingLegRow
{
    public MappingLegRow(
        string legId,
        string sourceSchema,
        string sourceSchemaDistinctName,
        string destinationSchema,
        string sourceEnvironmentType,
        string destinationEnvironmentType,
        string sourceFilter,
        string reversedSourceFilter,
        int fieldMappings)
    {
        LegId = legId;
        SourceSchema = sourceSchema;
        SourceSchemaDistinctName = sourceSchemaDistinctName;
        DestinationSchema = destinationSchema;
        SourceEnvironmentType = sourceEnvironmentType;
        DestinationEnvironmentType = destinationEnvironmentType;
        SourceFilter = sourceFilter;
        ReversedSourceFilter = reversedSourceFilter;
        FieldMappings = fieldMappings;
    }

    public string LegId { get; }
    public string SourceSchema { get; }
    public string SourceSchemaDistinctName { get; }
    public string DestinationSchema { get; }
    public string SourceEnvironmentType { get; }
    public string DestinationEnvironmentType { get; }
    public string SourceFilter { get; }
    public string ReversedSourceFilter { get; }
    public int FieldMappings { get; }
}

public sealed class MappingFieldRow
{
    public MappingFieldRow(
        string legId,
        string sourceSchema,
        string destinationSchema,
        string syncDirection,
        string sourceField,
        string destinationField,
        string destinationLookupEntity,
        bool? isSystemGenerated,
        int valueTransforms)
    {
        LegId = legId;
        SourceSchema = sourceSchema;
        DestinationSchema = destinationSchema;
        SyncDirection = syncDirection;
        SourceField = sourceField;
        DestinationField = destinationField;
        DestinationLookupEntity = destinationLookupEntity;
        IsSystemGenerated = isSystemGenerated;
        ValueTransforms = valueTransforms;
    }

    public string LegId { get; }
    public string SourceSchema { get; }
    public string DestinationSchema { get; }
    public string SyncDirection { get; }
    public string SourceField { get; }
    public string DestinationField { get; }
    public string DestinationLookupEntity { get; }
    public bool? IsSystemGenerated { get; }
    public int ValueTransforms { get; }
}

public sealed class MappingValueTransformRow
{
    public MappingValueTransformRow(
        string legId,
        string sourceField,
        string destinationField,
        string transformType,
        string defaultValue,
        string valueMap,
        bool? createValuesOnDestination)
    {
        LegId = legId;
        SourceField = sourceField;
        DestinationField = destinationField;
        TransformType = transformType;
        DefaultValue = defaultValue;
        ValueMap = valueMap;
        CreateValuesOnDestination = createValuesOnDestination;
    }

    public string LegId { get; }
    public string SourceField { get; }
    public string DestinationField { get; }
    public string TransformType { get; }
    public string DefaultValue { get; }
    public string ValueMap { get; }
    public bool? CreateValuesOnDestination { get; }
}

public sealed class PropertyTableRow
{
    public PropertyTableRow(string key, string type, string value)
    {
        Key = key;
        Type = type;
        Value = value;
    }

    public string Key { get; }
    public string Type { get; }
    public string Value { get; }
}

public sealed class FoEntityOption
{
    public static readonly FoEntityOption Auto = new(string.Empty, "(Auto)");

    public FoEntityOption(string value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }

    public string Value { get; }
    public string DisplayName { get; }
}

public sealed class CountLegConfigRow : INotifyPropertyChanged
{
    private bool _include;
    private string _foEntityOverride;

    public CountLegConfigRow(
        string mapDisplayName,
        string mapId,
        string legId,
        string sourceSchema,
        string sourceSchemaDistinctName,
        string sourceEnvironmentType,
        string destinationEnvironmentType,
        string foEntityResolved,
        string sourceFilterXpp,
        string foFilter,
        string foFilterNote,
        string ceEntity,
        string ceFilter,
        bool include,
        string foEntityOverride)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        LegId = legId;
        SourceSchema = sourceSchema;
        SourceSchemaDistinctName = sourceSchemaDistinctName;
        SourceEnvironmentType = sourceEnvironmentType;
        DestinationEnvironmentType = destinationEnvironmentType;
        FoEntityResolved = foEntityResolved;
        SourceFilterXpp = sourceFilterXpp;
        FoFilter = foFilter;
        FoFilterNote = foFilterNote;
        CeEntity = ceEntity;
        CeFilter = ceFilter;
        _include = include;
        _foEntityOverride = foEntityOverride ?? string.Empty;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public string LegId { get; }
    public string SourceSchema { get; }
    public string SourceSchemaDistinctName { get; }
    public string SourceEnvironmentType { get; }
    public string DestinationEnvironmentType { get; }
    public string FoEntityResolved { get; }
    public string SourceFilterXpp { get; }
    public string FoFilter { get; }
    public string FoFilterNote { get; }
    public string CeEntity { get; }
    public string CeFilter { get; }

    public bool Include
    {
        get => _include;
        set
        {
            if (_include == value)
            {
                return;
            }

            _include = value;
            OnPropertyChanged();
        }
    }

    public string FoEntityOverride
    {
        get => _foEntityOverride;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            if (string.Equals(_foEntityOverride, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _foEntityOverride = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(FoEntityEffective));
        }
    }

    public string FoEntityEffective =>
        string.IsNullOrWhiteSpace(FoEntityOverride)
            ? FoEntityResolved
            : FoEntityOverride;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class CountValidationRow
{
    public CountValidationRow(
        string mapDisplayName,
        string mapId,
        string legId,
        string foEntity,
        string foFilter,
        string ceEntity,
        string ceFilter,
        long? foCount,
        long? ceCount,
        bool? countsMatch,
        string status)
    {
        MapDisplayName = mapDisplayName;
        MapId = mapId;
        LegId = legId;
        FoEntity = foEntity;
        FoFilter = foFilter;
        CeEntity = ceEntity;
        CeFilter = ceFilter;
        FoCount = foCount;
        CeCount = ceCount;
        CountsMatch = countsMatch;
        Status = status;
    }

    public string MapDisplayName { get; }
    public string MapId { get; }
    public string LegId { get; }
    public string FoEntity { get; }
    public string FoFilter { get; }
    public string CeEntity { get; }
    public string CeFilter { get; }
    public long? FoCount { get; }
    public long? CeCount { get; }
    public bool? CountsMatch { get; }
    public string Status { get; }
}

public sealed class SolutionOption
{
    public SolutionOption(
        Guid id,
        string displayName,
        string uniqueName,
        string friendlyName,
        string version,
        string publisherUniqueName,
        string publisherDisplayName)
    {
        Id = id;
        DisplayName = displayName;
        UniqueName = uniqueName;
        FriendlyName = friendlyName;
        Version = version;
        PublisherUniqueName = publisherUniqueName;
        PublisherDisplayName = publisherDisplayName;
    }

    public Guid Id { get; }
    public string DisplayName { get; }
    public string UniqueName { get; }
    public string FriendlyName { get; }
    public string Version { get; }
    public string PublisherUniqueName { get; }
    public string PublisherDisplayName { get; }
}

public sealed class PublisherOption
{
    public static readonly PublisherOption All = new(string.Empty, "(All Publishers)", 0);

    public PublisherOption(string uniqueName, string displayName, int solutionCount)
    {
        UniqueName = uniqueName;
        DisplayName = solutionCount > 0 && !string.IsNullOrWhiteSpace(uniqueName)
            ? $"{displayName} ({solutionCount})"
            : displayName;
        SolutionCount = solutionCount;
    }

    public string UniqueName { get; }
    public string DisplayName { get; }
    public int SolutionCount { get; }
    public bool IsAll => string.IsNullOrWhiteSpace(UniqueName);
}

public sealed class JsonTableRow
{
    public JsonTableRow(string path, string type, string value)
    {
        Path = path;
        Type = type;
        Value = value;
    }

    public string Path { get; }
    public string Type { get; }
    public string Value { get; }
}



2026-04-24T18:01:55.305909Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 2.3 seconds
Output:

 exited 1 in 2343ms:
codex
I have enough context. The smallest clean change is an inline per-map settings editor in the existing right-hand pane, plus a toolbar button that reveals it and a viewmodel load/save path bound to `SelectedRecord`; I’m putting store round-trip coverage in first so the persistence contract is pinned down before I wire the UI.
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'tests/FoToolbox.Tests/FoToolbox.Tests.csproj'" in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'plugins/DualWriteMapBrowser/DualWriteMapBrowser.csproj'" in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command 'rg -n "InternalsVisibleTo|WPF|UseWPF" -g"*.csproj" -g"*.cs" .' in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 1569ms:
.\plugins\HelloPlugin\HelloPlugin.csproj:7:    <UseWPF>true</UseWPF>
.\plugins\TableEntityBrowser\TableEntityBrowser.csproj:7:    <UseWPF>true</UseWPF>
.\plugins\QueryBuilder\QueryBuilder.csproj:7:    <UseWPF>true</UseWPF>
.\plugins\ODataPostBuilder\ODataPostBuilder.csproj:7:    <UseWPF>true</UseWPF>
.\src\FoToolbox.SDK\Collections\BulkObservableCollection.cs:9:/// This avoids UI freezes when loading thousands of rows into WPF controls.
.\src\FoToolbox.SDK\FoToolbox.SDK.csproj:7:    <UseWPF>true</UseWPF>
.\src\FoToolbox.SDK\Plugins\IFoToolPlugin.cs:31:    /// Creates the WPF control that is displayed as a tab in the host window.
.\plugins\QueryBuilder\AssemblyInfo.cs:3:[assembly: InternalsVisibleTo("FoToolbox.Tests")]
.\plugins\DualWriteMapBrowser\DualWriteMapBrowser.csproj:7:    <UseWPF>true</UseWPF>
.\tests\FoToolbox.Tests\FoToolbox.Tests.csproj:7:    <UseWPF>true</UseWPF>
.\src\FoToolbox.Host\FoToolbox.Host.csproj:8:    <UseWPF>true</UseWPF>
.\src\FoToolbox.Host\AssemblyInfo.cs:1:[assembly: [redacted]("FoToolbox.Tests")]

 succeeded in 1911ms:
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>

    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\\..\\src\\FoToolbox.Core\\FoToolbox.Core.csproj" />
    <ProjectReference Include="..\\..\\src\\FoToolbox.SDK\\FoToolbox.SDK.csproj" />
    <ProjectReference Include="..\\..\\src\\FoToolbox.Host\\FoToolbox.Host.csproj" />
    <ProjectReference Include="..\\..\\src\\FoToolbox.Updater\\FoToolbox.Updater.csproj" />
    <ProjectReference Include="..\\..\\plugins\\HelloPlugin\\HelloPlugin.csproj" />
    <ProjectReference Include="..\\..\\plugins\\QueryBuilder\\QueryBuilder.csproj" />
    <ProjectReference Include="..\\..\\plugins\\DualWriteMapBrowser\\DualWriteMapBrowser.csproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" />
    <PackageReference Include="[redacted]" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="System.Net.Http" />
    <PackageReference Include="System.Text.RegularExpressions" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <None Include="Resources\SampleMetadata.xml" CopyToOutputDirectory="Always" />
  </ItemGroup>

</Project>


 succeeded in 2172ms:
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0-windows</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <UseWPF>true</UseWPF>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
    <RepoRoot Condition="'$(RepoRoot)' == '' and '$(SolutionDir)' != ''">$(SolutionDir)</RepoRoot>
    <RepoRoot Condition="'$(RepoRoot)' == ''">$(MSBuildProjectDirectory)\..\..\</RepoRoot>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\FoToolbox.SDK\FoToolbox.SDK.csproj" />
    <ProjectReference Include="..\..\src\FoToolbox.Core\FoToolbox.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="PluginManifest.json" />
  </ItemGroup>

  <Target Name="CopyPluginToHostBin" AfterTargets="Build">
    <ItemGroup>
      <PluginOutput Include="$(TargetDir)**\*.*" />
    </ItemGroup>
    <Copy SourceFiles="@(PluginOutput)"
          DestinationFiles="@(PluginOutput->'$(RepoRoot)src\FoToolbox.Host\bin\$(Configuration)\net8.0-windows\plugins\DualWriteMapBrowser\%(RecursiveDir)%(Filename)%(Extension)')"
          SkipUnchangedFiles="true" />
  </Target>

</Project>


codex
I need internal test access for the new persistence/parsing coverage, so I’m checking the repo’s existing `InternalsVisibleTo` pattern and then I’ll add the failing test against the store/UI text round-trip.
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'plugins/QueryBuilder/AssemblyInfo.cs'" in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-ChildItem 'plugins/DualWriteMapBrowser'" in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 1383ms:
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("FoToolbox.Tests")]



 succeeded in 1610ms:

    Directory: C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser

[32;1mMode   [0m[32;1m              LastWriteTime[0m [32;1;3m        Length[0m[32;1m Name[0m
[32;1m----   [0m [32;1m             -------------[0m [32;1m        ------[0m [32;1m----[0m
d----          22/04/2026  8:10 am                [44;1mbin[0m
d----          25/04/2026  5:43 am                [44;1mobj[0m
-a---          21/04/2026  9:57 pm           1223 DualWriteMapBrowser.csproj
-a---          21/04/2026  9:57 pm           1084 DualWriteMapBrowserPlugin.cs
-a---          21/04/2026  9:57 pm          41147 DualWriteMapBrowserView.xaml
-a---          21/04/2026  9:57 pm            282 DualWriteMapBrowserView.xaml.cs
-a---          21/04/2026  9:57 pm         104230 DualWriteMapBrowserViewModel.cs
-a---          21/04/2026  9:57 pm          75338 [redacted]
-a---          21/04/2026  9:57 pm            178 PluginManifest.json
-a---          21/04/2026  9:57 pm           7607 TestifyConfigurationStore.cs
-a---          21/04/2026  9:57 pm           6211 TestifyModels.cs
-a---          21/04/2026  9:57 pm          16316 TestifyPlanner.cs
-a---          21/04/2026  9:57 pm           3307 TestifyRunner.cs
-a---          21/04/2026  9:57 pm           4006 TestifyValueMapParser.cs


codex
I’m writing the regression first: one test for config file round-trip and one for the text serialization shape the panel will use. After that fails, I’ll add the smallest production surface needed to make it pass and wire the WPF bindings on top.
apply patch
patch: completed
c:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\AssemblyInfo.cs
c:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted]
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,65 @@
+using DualWriteMapBrowserPlugin;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,65 @@
+using DualWriteMapBrowserPlugin;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "dotnet test .\\tests\\FoToolbox.Tests\\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests" in c:\Users\ben.jones\Repos\toolbAX
2026-04-24T18:03:01.468289Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 13.3 seconds
Output:
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](10,20): error CS0103: The name 'Path' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](10,33): error CS0103: The name 'Path' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](38,17): error CS0103: The name 'File' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](40,17): error CS0103: The name 'File' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](48,24): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](49,29): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](58,20): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](59,25): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]

 exited 1 in 13257ms:
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](10,20): error CS0103: The name 'Path' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](10,33): error CS0103: The name 'Path' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](38,17): error CS0103: The name 'File' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](40,17): error CS0103: The name 'File' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](48,24): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](49,29): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](58,20): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](59,25): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,65 @@
+using DualWriteMapBrowserPlugin;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

codex
The red step is in place. The first failure is mostly scaffolding noise plus the intentionally missing serializer, so I’m fixing the test imports only and then I’ll implement the production helpers/UI against that failing surface.
apply patch
patch: completed
c:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted]
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "dotnet test .\\tests\\FoToolbox.Tests\\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests" in c:\Users\ben.jones\Repos\toolbAX
2026-04-24T18:03:12.974063Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 4.3 seconds
Output:
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](49,24): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](50,29): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](59,20): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](60,25): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]

 exited 1 in 4307ms:
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](49,24): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](50,29): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](59,20): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]
C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\[redacted](60,25): error CS0103: The name 'TestifySettingsTextSerializer' does not exist in the current context [C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\FoToolbox.Tests.csproj]

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'src/FoToolbox.SDK/Commands/RelayCommand.cs'" in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'src/FoToolbox.SDK/Commands/AsyncRelayCommand.cs'" in c:\Users\ben.jones\Repos\toolbAX
2026-04-24T18:03:18.644679Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 1.4 seconds
Output:
[31;1mGet-Content: [0m
[31;1m[36;1mLine |[0m
[31;1m[36;1m[36;1m   2 | [0m [36;1mGet-Content -Raw 'src/FoToolbox.SDK/Commands/RelayCommand.cs'[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m     | [31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m[31;1m[31;1m[36;1m     | [31;1mCannot find path 'C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\Commands\RelayCommand.cs' because it does not exist.[0m

 exited 1 in 1365ms:
[31;1mGet-Content: [0m
[31;1m[36;1mLine |[0m
[31;1m[36;1m[36;1m   2 | [0m [36;1mGet-Content -Raw 'src/FoToolbox.SDK/Commands/RelayCommand.cs'[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m     | [31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m[31;1m[31;1m[36;1m     | [31;1mCannot find path 'C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\Commands\RelayCommand.cs' because it does not exist.[0m

2026-04-24T18:03:18.861289Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 1.6 seconds
Output:
[31;1mGet-Content: [0m
[31;1m[36;1mLine |[0m
[31;1m[36;1m[36;1m   2 | [0m [36;1mGet-Content -Raw 'src/FoToolbox.SDK/Commands/AsyncRelayCommand.cs'[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m     | [31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m[31;1m[31;1m[36;1m     | [31;1mCannot find path 'C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\Commands\AsyncRelayCommand.cs' because it does not exist.[0m

 exited 1 in 1582ms:
[31;1mGet-Content: [0m
[31;1m[36;1mLine |[0m
[31;1m[36;1m[36;1m   2 | [0m [36;1mGet-Content -Raw 'src/FoToolbox.SDK/Commands/AsyncRelayCommand.cs'[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m     | [31;1m ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~[0m
[31;1m[36;1m[36;1m[0m[36;1m[0m[36;1m[31;1m[31;1m[36;1m     | [31;1mCannot find path 'C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\Commands\AsyncRelayCommand.cs' because it does not exist.[0m

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "rg --files src plugins | rg \"RelayCommand\\.cs|AsyncRelayCommand\\.cs\"" in c:\Users\ben.jones\Repos\toolbAX
2026-04-24T18:03:23.147097Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 1.4 seconds
Output:

 exited 1 in 1373ms:
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command 'rg --files src plugins | rg "Command"' in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 1406ms:
src\FoToolbox.SDK\Commands\PluginCommands.cs

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "Get-Content -Raw 'src/FoToolbox.SDK/Commands/PluginCommands.cs'" in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 1433ms:
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FoToolbox.SDK.Commands;

/// <summary>
/// An <see cref="ICommand"/> that wraps an async delegate, routing exceptions to an optional error handler.
/// The command's own <see cref="CancellationTokenSource"/> is passed to the delegate on each execution.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<CancellationToken, Task> _execute;
    private readonly Action<Exception>? _onError;
    private readonly CancellationTokenSource _cts = new();

    public AsyncRelayCommand(Func<CancellationToken, Task> execute, Action<Exception>? onError = null)
    {
        _execute = execute;
        _onError = onError;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => true;

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute(_cts.Token);
        }
        catch (Exception ex)
        {
            if (_onError is not null)
                _onError(ex);
            else
                [redacted](ex);
        }
    }

    public Task ExecuteAsync(CancellationToken cancellationToken = default) => _execute(cancellationToken);
}

/// <summary>
/// An <see cref="ICommand"/> that wraps a synchronous delegate.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged { add { } remove { } }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);
}


diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

codex
I’m at the production edit stage now: a small helper for the text format, a focused partial viewmodel slice for settings state/load/save, and then a narrow XAML section plus toolbar entry. After that I’ll run the targeted tests again before doing the full solution build.
apply patch
patch: completed
c:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\[redacted]
c:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\[redacted]
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

apply patch
patch: completed
c:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\DualWriteMapBrowserViewModel.cs
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

apply patch
patch: completed
c:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\DualWriteMapBrowserView.xaml
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "dotnet test .\\tests\\FoToolbox.Tests\\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests" in c:\Users\ben.jones\Repos\toolbAX
2026-04-24T18:05:34.968467Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 5.4 seconds
Output:
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\[redacted](185,25): error CS1061: 'ILogger' does not contain a definition for 'LogWarning' and no accessible extension method 'LogWarning' accepting a first argument of type 'ILogger' could be found (are you missing a using directive or an assembly reference?) [C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\DualWriteMapBrowser.csproj]

 exited 1 in 5387ms:
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\[redacted](185,25): error CS1061: 'ILogger' does not contain a definition for 'LogWarning' and no accessible extension method 'LogWarning' accepting a first argument of type 'ILogger' could be found (are you missing a using directive or an assembly reference?) [C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\DualWriteMapBrowser.csproj]

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,247 @@
+using FoToolbox.SDK.Commands;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

codex
The new tests are compiling through most of the path. I hit one missing logging import in the new partial file; fixing that now, then I’ll rerun the targeted tests to confirm the round-trip and text contract are green.
apply patch
patch: completed
c:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\[redacted]
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,248 @@
+using FoToolbox.SDK.Commands;
+using Microsoft.Extensions.Logging;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,248 @@
+using FoToolbox.SDK.Commands;
+using Microsoft.Extensions.Logging;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "dotnet test .\\tests\\FoToolbox.Tests\\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests" in c:\Users\ben.jones\Repos\toolbAX
2026-04-24T18:05:57.056573Z ERROR codex_core::tools::router: error=Exit code: 1
Wall time: 8.7 seconds
Output:
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
  FoToolbox.Tests -> C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll
Test run for C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Testhost process for source(s) 'C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll' exited with error: You must install or update .NET to run this application.
App: C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\testhost.exe
Architecture: x64
Framework: 'Microsoft.AspNetCore.App', version '8.0.0' (x64)
.NET location: C:\Program Files\dotnet
The following frameworks were found:
  10.0.6 at [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
The following frameworks for other architectures were found:
  x86
    8.0.26 at [C:\Program Files (x86)\dotnet\shared\Microsoft.AspNetCore.App]
Learn more:
https://aka.ms/dotnet/app-launch-failed
To install missing framework, download:
https://aka.ms/dotnet-core-applaunch?framework=Microsoft.AspNetCore.App&framework_version=8.0.0&arch=x64&rid=win-x64&os=win10
. Please check the diagnostic logs for more information.
Test Run Aborted.

 exited 1 in 8736ms:
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Release\net8.0\FoToolbox.Updater.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
  FoToolbox.Tests -> C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll
Test run for C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll (.NETCoreApp,Version=v8.0)
A total of 1 test files matched the specified pattern.

Testhost process for source(s) 'C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll' exited with error: You must install or update .NET to run this application.
App: C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\testhost.exe
Architecture: x64
Framework: 'Microsoft.AspNetCore.App', version '8.0.0' (x64)
.NET location: C:\Program Files\dotnet
The following frameworks were found:
  10.0.6 at [C:\Program Files\dotnet\shared\Microsoft.AspNetCore.App]
The following frameworks for other architectures were found:
  x86
    8.0.26 at [C:\Program Files (x86)\dotnet\shared\Microsoft.AspNetCore.App]
Learn more:
https://aka.ms/dotnet/app-launch-failed
To install missing framework, download:
https://aka.ms/dotnet-core-applaunch?framework=Microsoft.AspNetCore.App&framework_version=8.0.0&arch=x64&rid=win-x64&os=win10
. Please check the diagnostic logs for more information.
Test Run Aborted.

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,248 @@
+using FoToolbox.SDK.Commands;
+using Microsoft.Extensions.Logging;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

codex
The targeted test assembly now builds, but execution is blocked by the machine missing the x64 `.NET 8` ASP.NET runtime that `testhost.exe` needs. I’m moving to the task’s required validation next with a full Release build, then I’ll report the runtime blocker separately from the code changes.
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command "dotnet build .\\FoToolbox.sln -c Release" in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 9852ms:
  Determining projects to restore...
  All projects are up-to-date for restore.
  FoToolbox.Core -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Core\bin\Release\net8.0-windows\FoToolbox.Core.dll
  FoToolbox.SDK -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.SDK\bin\Release\net8.0-windows\FoToolbox.SDK.dll
  DualWriteMapBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\DualWriteMapBrowser\bin\Release\net8.0-windows\DualWriteMapBrowser.dll
  QueryBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\QueryBuilder\bin\Release\net8.0-windows\QueryBuilder.dll
  HelloPlugin -> C:\Users\ben.jones\Repos\toolbAX\plugins\HelloPlugin\bin\Release\net8.0-windows\HelloPlugin.dll
  FoToolbox.Updater -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Updater\bin\Debug\net8.0\FoToolbox.Updater.dll
  TableEntityBrowser -> C:\Users\ben.jones\Repos\toolbAX\plugins\TableEntityBrowser\bin\Release\net8.0-windows\TableEntityBrowser.dll
  ODataPostBuilder -> C:\Users\ben.jones\Repos\toolbAX\plugins\ODataPostBuilder\bin\Release\net8.0-windows\ODataPostBuilder.dll
  FoToolbox.Host -> C:\Users\ben.jones\Repos\toolbAX\src\FoToolbox.Host\bin\Release\net8.0-windows\FoToolbox.Host.dll
  FoToolbox.Tests -> C:\Users\ben.jones\Repos\toolbAX\tests\FoToolbox.Tests\bin\Release\net8.0-windows\FoToolbox.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:08.64

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,248 @@
+using FoToolbox.SDK.Commands;
+using Microsoft.Extensions.Logging;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command 'git status --short' in c:\Users\ben.jones\Repos\toolbAX
exec
"C:\\Program Files\\PowerShell\\7\\pwsh.exe" -Command 'git diff -- plugins/DualWriteMapBrowser/AssemblyInfo.cs plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs plugins/DualWriteMapBrowser/[redacted] plugins/DualWriteMapBrowser/[redacted] tests/FoToolbox.Tests/[redacted]' in c:\Users\ben.jones\Repos\toolbAX
 succeeded in 1502ms:
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index 6649d6e..0aa3b32 100644
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -56,6 +56,12 @@
                                 MinWidth="100"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
                         <CheckBox Content="Exact CE Count (slower)"
                                   IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
                                   VerticalAlignment="Center"
@@ -232,6 +238,59 @@
                             <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
                         </Grid>
 
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
                         <TextBlock Text="msdyn_mapping"
                                    Margin="0,12,0,4"
                                    FontWeight="SemiBold" />
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index ef9ede9..557d124 100644
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -111,6 +111,7 @@ public sealed partial class DualWriteMapBrowserViewModel : INotifyPropertyChange
         PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
         RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
         CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
         ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
@@ -196,7 +197,7 @@ public sealed partial class DualWriteMapBrowserViewModel : INotifyPropertyChange
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -260,6 +261,7 @@ public sealed partial class DualWriteMapBrowserViewModel : INotifyPropertyChange
 
             _selectedRecord = value;
             OnPropertyChanged();
+            OnSelectedRecordChanged();
         }
     }
 
warning: in the working copy of 'plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml', LF will be replaced by CRLF the next time Git touches it
warning: in the working copy of 'plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs', LF will be replaced by CRLF the next time Git touches it

 succeeded in 1746ms:
 M .ralph/logs/extension.log
 M .ralph/tasks.json
 M .vscode/settings.json
 M plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
 M plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
?? .ralph/claims.json
?? .ralph/progress.md
?? .ralph/prompts/
?? .ralph/state.json
?? plugins/DualWriteMapBrowser/AssemblyInfo.cs
?? plugins/DualWriteMapBrowser/[redacted]
?? plugins/DualWriteMapBrowser/[redacted]
?? tests/FoToolbox.Tests/[redacted]

diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,248 @@
+using FoToolbox.SDK.Commands;
+using Microsoft.Extensions.Logging;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

codex
Changed files:
- [DualWriteMapBrowserView.xaml](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml)
- [DualWriteMapBrowserViewModel.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs)
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/[redacted])
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/[redacted])
- [AssemblyInfo.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/AssemblyInfo.cs)
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/tests/FoToolbox.Tests/[redacted])

The change adds a `Testify Settings` toolbar button and an inline WPF per-map settings panel in `DualWriteMapBrowser`. The panel binds to the selected map, loads from `TestifyConfigurationStore` on selection/open, and saves `omitCreateFields`, `preferredCreateValues`, `cePollTimeoutMinutes`, and `allowPartialEnumCoverage` back through the existing store path. I also added a small text serializer for the multiline editor format and test access for the plugin internals.

Validation results:
- `dotnet build .\FoToolbox.sln -c Release` succeeded.
- `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests` built successfully but could not run because this machine is missing the x64 `Microsoft.AspNetCore.App` 8.0 runtime required by `testhost.exe`.

Assumptions or blockers:
- `AGENTS.md` was not present at the repo root, so I used the Ralph files and repository code as the source of truth.
- I treated “panel inside DualWriteMapBrowser” as an inline expandable editor rather than a separate modal dialog.
- Test execution is currently blocked by the local runtime environment, not by compile errors.

Known limitations or follow-up work:
- Saving updates the persisted config immediately, but existing preflight/run state is not recomputed automatically; the UI prompts the user to run `Prepare Testify` again.
- The new persistence tests are in place but still need to be executed once the required x64 .NET 8 ASP.NET runtime is installed.
- A quick manual WPF smoke test is still warranted for the selection-change/load-save flow.

```json
{
  "selectedTaskId": "T1",
  "requestedStatus": "done",
  "progressNote": "Added a Testify Settings toolbar entry and inline per-map WPF editor bound to the selected map, with load/save through TestifyConfigurationStore. Release solution build passed. Targeted test assembly builds, but test execution is blocked on this machine by a missing x64 Microsoft.AspNetCore.App 8.0 runtime.",
  "validationRan": "dotnet build .\\FoToolbox.sln -c Release",
  "blocker": "Targeted test execution could not run because testhost.exe requires the x64 Microsoft.AspNetCore.App 8.0 runtime, which is not installed on this machine.",
  "needsHumanReview": true
}
```
diff --git a/plugins/DualWriteMapBrowser/AssemblyInfo.cs b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/AssemblyInfo.cs
@@ -0,0 +1,3 @@
+using System.Runtime.CompilerServices;
+
+[assembly: InternalsVisibleTo("FoToolbox.Tests")]
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml
@@ -50,16 +50,22 @@
                                 MinWidth="120"
                                 Margin="6,0,0,0"
                                 IsEnabled="{Binding IsNotLoading}" />
-                        <Button Content="Run Testify"
-                                Command="{Binding RunTestifyCommand}"
-                                Padding="10,4"
-                                MinWidth="100"
-                                Margin="6,0,0,0"
-                                IsEnabled="{Binding IsNotLoading}" />
-                        <CheckBox Content="Exact CE Count (slower)"
-                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
-                                  VerticalAlignment="Center"
-                                  Margin="10,2,0,0" />
+                        <Button Content="Run Testify"
+                                Command="{Binding RunTestifyCommand}"
+                                Padding="10,4"
+                                MinWidth="100"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <Button Content="Testify Settings"
+                                Command="{Binding OpenTestifySettingsCommand}"
+                                Padding="10,4"
+                                MinWidth="120"
+                                Margin="6,0,0,0"
+                                IsEnabled="{Binding IsNotLoading}" />
+                        <CheckBox Content="Exact CE Count (slower)"
+                                  IsChecked="{Binding UseExactCeCount, Mode=TwoWay}"
+                                  VerticalAlignment="Center"
+                                  Margin="10,2,0,0" />
                         <Button Content="Clear"
                                 Command="{Binding ClearCommand}"
                                 Padding="10,4"
@@ -198,7 +204,7 @@
                                    FontWeight="SemiBold"
                                    FontSize="14" />
 
-                        <Grid Margin="0,10,0,0">
+                        <Grid Margin="0,10,0,0">
                             <Grid.ColumnDefinitions>
                                 <ColumnDefinition Width="130" />
                                 <ColumnDefinition Width="*" />
@@ -229,12 +235,65 @@
                             <TextBlock Grid.Row="6" Grid.Column="0" Text="Modified" FontWeight="SemiBold" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="6" Grid.Column="1" Text="{Binding [redacted]}" Margin="0,4,0,0" />
                             <TextBlock Grid.Row="7" Grid.Column="0" Text="Solution ID" FontWeight="SemiBold" Margin="0,4,0,0" />
-                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
-                        </Grid>
-
-                        <TextBlock Text="msdyn_mapping"
-                                   Margin="0,12,0,4"
-                                   FontWeight="SemiBold" />
+                            <TextBlock Grid.Row="7" Grid.Column="1" Text="{Binding SelectedRecord.SolutionId}" TextWrapping="Wrap" Margin="0,4,0,0" />
+                        </Grid>
+
+                        <Expander Header="Testify Map Settings"
+                                  Margin="0,12,0,0"
+                                  IsExpanded="{Binding IsTestifySettingsVisible, Mode=TwoWay}">
+                            <StackPanel Margin="0,8,0,0">
+                                <TextBlock Text="Edit the Testify settings for the selected map without opening the JSON file."
+                                           TextWrapping="Wrap"
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBlock Text="Omit Create Fields"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="One field name per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyOmitCreateFieldsText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="72"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="Preferred Create Values"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBlock Text="Use one `Field=Value` entry per line."
+                                           Foreground="{DynamicResource Fo.SubtleTextBrush}" />
+                                <TextBox Text="{Binding TestifyPreferredCreateValuesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         MinHeight="96"
+                                         AcceptsReturn="True"
+                                         VerticalScrollBarVisibility="Auto"
+                                         TextWrapping="Wrap" />
+
+                                <TextBlock Text="CE Poll Timeout (minutes)"
+                                           Margin="0,10,0,2"
+                                           FontWeight="SemiBold" />
+                                <TextBox Text="{Binding TestifyCePollTimeoutMinutesText, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}"
+                                         Width="120" />
+
+                                <CheckBox Content="Allow partial enum coverage"
+                                          IsChecked="{Binding TestifyAllowPartialEnumCoverage, Mode=TwoWay}"
+                                          Margin="0,10,0,0" />
+
+                                <StackPanel Orientation="Horizontal" Margin="0,10,0,0">
+                                    <Button Content="Save Testify Settings"
+                                            Command="{Binding SaveTestifySettingsCommand}"
+                                            Padding="10,4"
+                                            MinWidth="160"
+                                            IsEnabled="{Binding IsNotLoading}" />
+                                    <TextBlock Text="Loading..."
+                                               Margin="10,4,0,0"
+                                               Foreground="{DynamicResource Fo.SubtleTextBrush}"
+                                               Visibility="{Binding IsLoadingTestifySettings, Converter={StaticResource BoolToVisibility}}" />
+                                </StackPanel>
+                            </StackPanel>
+                        </Expander>
+
+                        <TextBlock Text="msdyn_mapping"
+                                   Margin="0,12,0,4"
+                                   FontWeight="SemiBold" />
                         <TabControl MinHeight="260">
                             <TabItem Header="Summary">
                                 <DataGrid ItemsSource="{Binding [redacted]}"
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,248 @@
+using FoToolbox.SDK.Commands;
+using Microsoft.Extensions.Logging;
+using System;
+using System.Globalization;
+using System.Threading;
+using System.Threading.Tasks;
+
+namespace DualWriteMapBrowserPlugin;
+
+public sealed partial class DualWriteMapBrowserViewModel
+{
+    private bool _isTestifySettingsVisible;
+    private bool _isLoadingTestifySettings;
+    private bool _isSavingTestifySettings;
+    private string _testifyOmitCreateFieldsText = string.Empty;
+    private string [redacted] = string.Empty;
+    private string [redacted] = "5";
+    private bool [redacted];
+
+    public RelayCommand OpenTestifySettingsCommand { get; private set; } = null!;
+    public AsyncRelayCommand SaveTestifySettingsCommand { get; private set; } = null!;
+
+    public bool IsTestifySettingsVisible
+    {
+        get => _isTestifySettingsVisible;
+        set
+        {
+            if (_isTestifySettingsVisible == value)
+            {
+                return;
+            }
+
+            _isTestifySettingsVisible = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool IsLoadingTestifySettings
+    {
+        get => _isLoadingTestifySettings;
+        private set
+        {
+            if (_isLoadingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isLoadingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public bool IsSavingTestifySettings
+    {
+        get => _isSavingTestifySettings;
+        private set
+        {
+            if (_isSavingTestifySettings == value)
+            {
+                return;
+            }
+
+            _isSavingTestifySettings = value;
+            OnPropertyChanged();
+            OnPropertyChanged(nameof(IsBusy));
+            OnPropertyChanged(nameof(IsNotLoading));
+        }
+    }
+
+    public string TestifyOmitCreateFieldsText
+    {
+        get => _testifyOmitCreateFieldsText;
+        set
+        {
+            if (string.Equals(_testifyOmitCreateFieldsText, value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            _testifyOmitCreateFieldsText = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyPreferredCreateValuesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public string TestifyCePollTimeoutMinutesText
+    {
+        get => [redacted];
+        set
+        {
+            if (string.Equals([redacted], value, StringComparison.Ordinal))
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    public bool TestifyAllowPartialEnumCoverage
+    {
+        get => [redacted];
+        set
+        {
+            if ([redacted] == value)
+            {
+                return;
+            }
+
+            [redacted] = value;
+            OnPropertyChanged();
+        }
+    }
+
+    private void InitializeTestifySettingsCommands(Action<Exception> onError)
+    {
+        OpenTestifySettingsCommand = new RelayCommand(_ => OpenTestifySettings());
+        SaveTestifySettingsCommand = new AsyncRelayCommand(SaveTestifySettingsAsync, onError);
+    }
+
+    private void OnSelectedRecordChanged()
+    {
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private void OpenTestifySettings()
+    {
+        if (SelectedRecord is null)
+        {
+            StatusMessage = "Select a dual-write map before opening Testify settings.";
+            return;
+        }
+
+        IsTestifySettingsVisible = true;
+        _ = LoadSelectedTestifyConfigurationAsync(CancellationToken.None);
+    }
+
+    private async Task LoadSelectedTestifyConfigurationAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            TestifyOmitCreateFieldsText = string.Empty;
+            TestifyPreferredCreateValuesText = string.Empty;
+            TestifyCePollTimeoutMinutesText = "5";
+            TestifyAllowPartialEnumCoverage = false;
+            return;
+        }
+
+        IsLoadingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            if (!string.Equals(SelectedRecord?.Id, record.Id, [redacted]))
+            {
+                return;
+            }
+
+            TestifyOmitCreateFieldsText = [redacted](config.OmitCreateFields);
+            TestifyPreferredCreateValuesText = [redacted](config.PreferredCreateValues);
+            TestifyCePollTimeoutMinutesText = [redacted](CultureInfo.InvariantCulture);
+            TestifyAllowPartialEnumCoverage = config.AllowPartialEnumCoverage;
+        }
+        catch (OperationCanceledException) when ([redacted])
+        {
+        }
+        catch (Exception ex)
+        {
+            _ctx.Logger.LogWarning(ex, "Failed to load Testify configuration for map {MapId}", record.Id);
+            StatusMessage = $"Failed to load Testify settings: {ex.Message}";
+        }
+        finally
+        {
+            IsLoadingTestifySettings = false;
+        }
+    }
+
+    private async Task SaveTestifySettingsAsync(CancellationToken cancellationToken)
+    {
+        var record = SelectedRecord;
+        if (record is null)
+        {
+            StatusMessage = "Select a dual-write map before saving Testify settings.";
+            return;
+        }
+
+        if (!int.TryParse(TestifyCePollTimeoutMinutesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var timeoutMinutes) ||
+            timeoutMinutes <= 0)
+        {
+            StatusMessage = "CE poll timeout must be a positive whole number of minutes.";
+            return;
+        }
+
+        var omitCreateFields = [redacted](TestifyOmitCreateFieldsText);
+        Dictionary<string, string> preferredCreateValues;
+        try
+        {
+            preferredCreateValues = [redacted](TestifyPreferredCreateValuesText);
+        }
+        catch (FormatException ex)
+        {
+            StatusMessage = ex.Message;
+            return;
+        }
+
+        IsSavingTestifySettings = true;
+        try
+        {
+            var config = await [redacted](_ctx.CurrentEnv.Id, record.Id, cancellationToken).ConfigureAwait(false);
+            config.OmitCreateFields = omitCreateFields;
+            config.PreferredCreateValues = preferredCreateValues;
+            config.CePollTimeoutMinutes = timeoutMinutes;
+            config.AllowPartialEnumCoverage = TestifyAllowPartialEnumCoverage;
+            await _testifyConfigStore.SaveAsync(config, cancellationToken).ConfigureAwait(false);
+
+            if (_testifyPlans.TryGetValue(record.Id, out var plan))
+            {
+                [redacted] = new HashSet<string>(omitCreateFields, [redacted]);
+                [redacted] = new Dictionary<string, string>(preferredCreateValues, [redacted]);
+                [redacted] = timeoutMinutes;
+                [redacted] = TestifyAllowPartialEnumCoverage;
+            }
+
+            StatusMessage = $"Saved Testify settings for '{record.DisplayName}'. Run 'Prepare Testify' again to refresh any existing preflight state.";
+        }
+        finally
+        {
+            IsSavingTestifySettings = false;
+        }
+    }
+}
diff --git a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
index [redacted]
--- a/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
+++ b/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs
@@ -106,12 +106,13 @@
 
         LoadMapsCommand = new AsyncRelayCommand(LoadMapsAsync, onError);
         LoadSolutionsCommand = new AsyncRelayCommand(LoadSolutionsAsync, onError);
-        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
-        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
-        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
-        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
-        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
-        ClearCommand = new RelayCommand(_ => ClearRecords());
+        RefreshCountSetupCommand = new AsyncRelayCommand(RefreshCountSetupAsync, onError);
+        ValidateCountsCommand = new AsyncRelayCommand(ValidateCountsAsync, onError);
+        PrepareTestifyCommand = new AsyncRelayCommand(PrepareTestifyAsync, onError);
+        RunTestifyCommand = new AsyncRelayCommand(RunTestifyAsync, onError);
+        CleanupTestifyCommand = new AsyncRelayCommand(CleanupTestifyAsync, onError);
+        InitializeTestifySettingsCommands(onError);
+        ClearCommand = new RelayCommand(_ => ClearRecords());
 
         if (!HasDataverseConnection)
         {
@@ -196,7 +197,7 @@
         }
     }
 
-    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify;
+    public bool IsBusy => IsLoading || IsLoadingSolutions || IsCounting || IsPreparingTestify || IsRunningTestify || IsLoadingTestifySettings || IsSavingTestifySettings;
 
     public bool FilterBySolution
     {
@@ -248,20 +249,21 @@
         }
     }
 
-    public DualWriteMapRecord? SelectedRecord
-    {
-        get => _selectedRecord;
-        set
-        {
+    public DualWriteMapRecord? SelectedRecord
+    {
+        get => _selectedRecord;
+        set
+        {
             if (_selectedRecord == value)
             {
                 return;
             }
-
-            _selectedRecord = value;
-            OnPropertyChanged();
-        }
-    }
+
+            _selectedRecord = value;
+            OnPropertyChanged();
+            OnSelectedRecordChanged();
+        }
+    }
 
     public SolutionOption? SelectedSolution
     {
diff --git a/plugins/DualWriteMapBrowser/[redacted] b/plugins/DualWriteMapBrowser/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/plugins/DualWriteMapBrowser/[redacted]
@@ -0,0 +1,82 @@
+using System;
+using System.Collections.Generic;
+using System.Linq;
+
+namespace DualWriteMapBrowserPlugin;
+
+internal static class TestifySettingsTextSerializer
+{
+    public static string FormatLines(IEnumerable<string>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v))
+                .Select(v => v.Trim())
+                .Distinct([redacted])
+                .OrderBy(v => v, [redacted]));
+    }
+
+    public static HashSet<string> ParseLines(string? text)
+    {
+        var values = SplitLines(text)
+            .Where(v => !string.IsNullOrWhiteSpace(v))
+            .Select(v => v.Trim());
+
+        return new HashSet<string>(values, [redacted]);
+    }
+
+    public static string FormatKeyValueLines(IEnumerable<KeyValuePair<string, string>>? values)
+    {
+        if (values is null)
+        {
+            return string.Empty;
+        }
+
+        return string.Join(
+            "\r\n",
+            values
+                .Where(v => !string.IsNullOrWhiteSpace(v.Key) && !string.IsNullOrWhiteSpace(v.Value))
+                .OrderBy(v => v.Key, [redacted])
+                .Select(v => $"{v.Key.Trim()}={v.Value.Trim()}"));
+    }
+
+    public static Dictionary<string, string> ParseKeyValueLines(string? text)
+    {
+        var values = new Dictionary<string, string>([redacted]);
+        foreach (var line in SplitLines(text))
+        {
+            if (string.IsNullOrWhiteSpace(line))
+            {
+                continue;
+            }
+
+            var separatorIndex = line.IndexOf('=');
+            if (separatorIndex <= 0)
+            {
+                throw new FormatException($"Invalid preferred value entry '{line.Trim()}'. Use Field=Value.");
+            }
+
+            var key = line[..separatorIndex].Trim();
+            var value = line[(separatorIndex + 1)..].Trim();
+            if (string.IsNullOrWhiteSpace(key))
+            {
+                throw new FormatException("Preferred value entries require a field name before '='.");
+            }
+
+            values[key] = value;
+        }
+
+        return values;
+    }
+
+    private static IEnumerable<string> SplitLines(string? text) =>
+        string.IsNullOrWhiteSpace(text)
+            ? Array.Empty<string>()
+            : text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n');
+}
diff --git a/tests/FoToolbox.Tests/[redacted] b/tests/FoToolbox.Tests/[redacted]
new file mode 100644
index [redacted]
--- /dev/null
+++ b/tests/FoToolbox.Tests/[redacted]
@@ -0,0 +1,66 @@
+using DualWriteMapBrowserPlugin;
+using System.IO;
+
+namespace FoToolbox.Tests;
+
+public sealed class TestifyConfigurationStoreTests
+{
+    [Fact]
+    public async Task [redacted]()
+    {
+        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");
+
+        try
+        {
+            var store = new TestifyConfigurationStore(path);
+            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, [redacted]);
+            config.PreferredCreateValues = new Dictionary<string, string>([redacted])
+            {
+                ["NumberSequenceGroup"] = "STD",
+                ["CurrencyCode"] = "USD"
+            };
+            config.CePollTimeoutMinutes = 12;
+            config.AllowPartialEnumCoverage = true;
+
+            await store.SaveAsync(config, CancellationToken.None);
+
+            var reloadedStore = new TestifyConfigurationStore(path);
+            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
+
+            Assert.Equal(new[] { "FieldA", "fieldB" }, [redacted](v => v, [redacted]));
+            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
+            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
+            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
+            Assert.True([redacted]);
+        }
+        finally
+        {
+            if (File.Exists(path))
+            {
+                File.Delete(path);
+            }
+        }
+    }
+
+    [Fact]
+    public void [redacted]()
+    {
+        var omitText = [redacted](new HashSet<string>(new[] { "FieldA", "FieldB" }, [redacted]));
+        var preferredText = [redacted](new Dictionary<string, string>([redacted])
+        {
+            ["NumberSequenceGroup"] = "STD",
+            ["CurrencyCode"] = "USD"
+        });
+
+        Assert.Equal("FieldA\r\nFieldB", omitText);
+        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);
+
+        var omit = [redacted](" FieldA \r\n\r\nfieldB \r\n");
+        var preferred = [redacted](" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");
+
+        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, [redacted]));
+        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
+        Assert.Equal("USD", preferred["CurrencyCode"]);
+    }
+}

tokens used
105,115


## Last Message

Changed files:
- [DualWriteMapBrowserView.xaml](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserView.xaml)
- [DualWriteMapBrowserViewModel.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/DualWriteMapBrowserViewModel.cs)
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/[redacted])
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/[redacted])
- [AssemblyInfo.cs](C:/Users/ben.jones/Repos/toolbAX/plugins/DualWriteMapBrowser/AssemblyInfo.cs)
- [[redacted]](C:/Users/ben.jones/Repos/toolbAX/tests/FoToolbox.Tests/[redacted])

The change adds a `Testify Settings` toolbar button and an inline WPF per-map settings panel in `DualWriteMapBrowser`. The panel binds to the selected map, loads from `TestifyConfigurationStore` on selection/open, and saves `omitCreateFields`, `preferredCreateValues`, `cePollTimeoutMinutes`, and `allowPartialEnumCoverage` back through the existing store path. I also added a small text serializer for the multiline editor format and test access for the plugin internals.

Validation results:
- `dotnet build .\FoToolbox.sln -c Release` succeeded.
- `dotnet test .\tests\FoToolbox.Tests\FoToolbox.Tests.csproj -c Release --no-restore --filter TestifyConfigurationStoreTests` built successfully but could not run because this machine is missing the x64 `Microsoft.AspNetCore.App` 8.0 runtime required by `testhost.exe`.

Assumptions or blockers:
- `AGENTS.md` was not present at the repo root, so I used the Ralph files and repository code as the source of truth.
- I treated “panel inside DualWriteMapBrowser” as an inline expandable editor rather than a separate modal dialog.
- Test execution is currently blocked by the local runtime environment, not by compile errors.

Known limitations or follow-up work:
- Saving updates the persisted config immediately, but existing preflight/run state is not recomputed automatically; the UI prompts the user to run `Prepare Testify` again.
- The new persistence tests are in place but still need to be executed once the required x64 .NET 8 ASP.NET runtime is installed.
- A quick manual WPF smoke test is still warranted for the selection-change/load-save flow.

```json
{
  "selectedTaskId": "T1",
  "requestedStatus": "done",
  "progressNote": "Added a Testify Settings toolbar entry and inline per-map WPF editor bound to the selected map, with load/save through TestifyConfigurationStore. Release solution build passed. Targeted test assembly builds, but test execution is blocked on this machine by a missing x64 Microsoft.AspNetCore.App 8.0 runtime.",
  "validationRan": "dotnet build .\\FoToolbox.sln -c Release",
  "blocker": "Targeted test execution could not run because testhost.exe requires the x64 Microsoft.AspNetCore.App 8.0 runtime, which is not installed on this machine.",
  "needsHumanReview": true
}
```
