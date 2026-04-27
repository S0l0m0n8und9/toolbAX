using DualWriteMapBrowserPlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class DualWriteMapBrowserTestifyIntegrationTests
{
    [Fact]
    public async Task CheckFoRecordExistsAsync_ReturnsFalse_ForStaleCachedEntityUrl()
    {
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationContext(new ThrowingODataClient(new HttpRequestException("404 stale cached URL"))),
            new TestifyConfigurationStore(CreateTempTestifyStorePath()));

        var exists = await viewModel.CheckFoRecordExistsAsync(
            "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-404',dataAreaId='USMF')?cross-company=true",
            CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task FinalizeTestifyFailureAsync_RollsBackMidRunFailure_AndClearsPersistedState()
    {
        var path = CreateTempTestifyStorePath();

        try
        {
            var store = new TestifyConfigurationStore(path);
            var config = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            var instanceUrl = "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0002',dataAreaId='USMF')?cross-company=true";
            config.LastRunToken = "TESTIFY-ROLLBACK";
            config.LastEntityInstanceUrl = instanceUrl;
            await store.SaveAsync(config, CancellationToken.None);

            var deleteClient = new SequenceODataWriteClient(new ODataWriteResponse(204, null, new Dictionary<string, string>()));
            var viewModel = new DualWriteMapBrowserViewModel(new FakeIntegrationWriteContext(deleteClient), store);

            var status = await viewModel.FinalizeTestifyFailureAsync(
                "Map A",
                "map-a",
                config,
                createdThisRun: true,
                "CE verification timed out after patch 1.",
                CancellationToken.None);

            Assert.Equal("CE verification timed out after patch 1. Created record rolled back.", status);
            Assert.Single(deleteClient.Requests);
            Assert.Equal(HttpMethod.Delete, deleteClient.Requests[0].Method);
            Assert.Equal(instanceUrl, deleteClient.Requests[0].Url);

            var reloaded = await store.GetOrCreateAsync("env-1", "map-a", CancellationToken.None);
            Assert.Null(reloaded.LastEntityInstanceUrl);
            Assert.Null(reloaded.LastRunToken);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_ThrowsTimeout_WhenCeRowDoesNotAppear()
    {
        var originalDelay = DualWriteMapBrowserViewModel.TestifyDelayAsync;
        var originalUtcNow = DualWriteMapBrowserViewModel.TestifyUtcNow;
        var now = DateTimeOffset.UtcNow;
        DualWriteMapBrowserViewModel.TestifyDelayAsync = static (_, _) => Task.CompletedTask;
        DualWriteMapBrowserViewModel.TestifyUtcNow = () => now = now.AddMinutes(2);

        try
        {
            using var dataverseHttp = new HttpClient(new SequenceJsonHttpMessageHandler("{\"value\":[]}"))
            {
                BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
            };
            var writeClient = new SequenceODataWriteClient();
            var context = new FakeIntegrationDataverseWriteContext(writeClient, dataverseHttp);
            var storePath = CreateTempTestifyStorePath();
            var store = new TestifyConfigurationStore(storePath);
            var viewModel = new DualWriteMapBrowserViewModel(context, store);
            var config = await store.GetOrCreateAsync("env-1", "map-timeout", CancellationToken.None);
            config.CePollTimeoutMinutes = 1;

            var plan = new TestifyMapPlan(
                mapId: "map-timeout",
                mapDisplayName: "Timeout Map",
                foEntity: "CustomersV3",
                foEntityDetails: null,
                configuration: config,
                foFilter: string.Empty,
                ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "", "", "Name", "name") },
                createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "Timeout" },
                createPayloadJson: "{}",
                enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
                patchSteps: Array.Empty<TestifyPatchStep>(),
                warnings: Array.Empty<string>(),
                coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
                blockingIssues: Array.Empty<string>());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.WaitForCorrelatedCeRowsAsync(
                    plan,
                    plan.CreateValues,
                    correlatedRows: null,
                    CancellationToken.None,
                    "after patch 1"));

            Assert.Equal("CE verification timed out (after patch 1) after 1 minute(s). Increase CePollTimeoutMinutes in Testify configuration if sync is slow.", ex.Message);
        }
        finally
        {
            DualWriteMapBrowserViewModel.TestifyDelayAsync = originalDelay;
            DualWriteMapBrowserViewModel.TestifyUtcNow = originalUtcNow;
        }
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenDuplicateRowsMatchCorrelation()
    {
        using var dataverseHttp = new HttpClient(new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-1\",\"name\":\"TESTIFY-001\"},{\"accountid\":\"row-2\",\"name\":\"TESTIFY-001\"}]}"))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        config.CePollTimeoutMinutes = 1;
        var plan = CreateCorrelationPlan(config, "TESTIFY-001");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, correlatedRows: null, CancellationToken.None, "after create"));

        Assert.Equal("CE verification failed for leg 'leg-1': expected one correlated row for name='TESTIFY-001' but found 2.", ex.Message);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenDuplicateRowsAppearOnLaterDataversePage()
    {
        var originalDelay = DualWriteMapBrowserViewModel.TestifyDelayAsync;
        DualWriteMapBrowserViewModel.TestifyDelayAsync = static (_, _) => Task.CompletedTask;

        try
        {
        var handler = new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-1\",\"name\":\"TESTIFY-001\"}],\"@odata.nextLink\":\"https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$skiptoken=page2\"}",
            "{\"value\":[{\"accountid\":\"row-2\",\"name\":\"TESTIFY-001\"}]}" );
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = CreateCorrelationPlan(config, "TESTIFY-001");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, correlatedRows: null, CancellationToken.None, "after create"));

        Assert.Equal("CE verification failed for leg 'leg-1': expected one correlated row for name='TESTIFY-001' but found 2.", ex.Message);
        Assert.Collection(
            handler.RequestUris,
            uri => Assert.Equal(
                "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$filter=name%20eq%20%27TESTIFY-001%27&$select=name%2Caccountsid",
                uri.AbsoluteUri),
            uri => Assert.Equal(
                "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$skiptoken=page2",
                uri.AbsoluteUri));
        }
        finally
        {
            DualWriteMapBrowserViewModel.TestifyDelayAsync = originalDelay;
        }
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenReturnedRowIsUnrelatedToFoRecord()
    {
        using var dataverseHttp = new HttpClient(new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-1\",\"name\":\"TESTIFY-OTHER\"}]}"))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = CreateCorrelationPlan(config, "TESTIFY-001");

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, correlatedRows: null, CancellationToken.None, "after create"));

        Assert.Equal("CE verification failed for leg 'leg-1': correlated row did not match FO value 'TESTIFY-001'.", ex.Message);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenUpdateFindsDifferentRow()
    {
        using var dataverseHttp = new HttpClient(new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-2\",\"name\":\"TESTIFY-001\"}]}"))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = CreateCorrelationPlan(config, "TESTIFY-001");
        var existing = new Dictionary<string, TestifyCorrelatedCeRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-1"] = new TestifyCorrelatedCeRow("leg-1", "accounts", "row-1", "leg-1|accounts|Name|name|TESTIFY-001", "TESTIFY-001", "Name", "name")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, existing, CancellationToken.None, "after patch 1"));

        Assert.Equal("CE verification failed for leg 'leg-1': expected CE row 'row-1' but found 'row-2'.", ex.Message);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_ReturnsSameRow_WhenCorrelationMatchesExistingRecord()
    {
        var handler = new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-1\",\"name\":\"TESTIFY-001\"}]}");
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = CreateCorrelationPlan(config, "TESTIFY-001");
        var existing = new Dictionary<string, TestifyCorrelatedCeRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-1"] = new TestifyCorrelatedCeRow("leg-1", "accounts", "row-1", "leg-1|accounts|Name|name|TESTIFY-001", "TESTIFY-001", "Name", "name")
        };

        var rows = await viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, existing, CancellationToken.None, "after patch 1");

        Assert.Equal("row-1", rows["leg-1"].RowId);
        Assert.Equal("leg-1|accounts|Name|name|TESTIFY-001", rows["leg-1"].DeterministicKey);
        Assert.Equal("TESTIFY-001", rows["leg-1"].CorrelationValue);
        Assert.Equal("Name", rows["leg-1"].FoCorrelationField);
        Assert.Equal("name", rows["leg-1"].CeCorrelationField);
        Assert.Single(handler.RequestUris);
        Assert.Equal(
            "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$filter=name%20eq%20%27TESTIFY-001%27&$select=name%2Caccountsid",
            handler.RequestUris[0].AbsoluteUri);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_ReusesExplicitStableRowIdField_WhenCorrelationMatchesExistingRecord()
    {
        var handler = new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountnumber\":\"ACC-001\",\"tbx_externalid\":\"stable-row-1\"}]}");
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = new TestifyMapPlan(
            mapId: "map-timeout",
            mapDisplayName: "Timeout Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: config,
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "", "", "AccountNumber", "accountnumber", "tbx_externalid") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AccountNumber"] = "ACC-001" },
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());
        var existing = new Dictionary<string, TestifyCorrelatedCeRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-1"] = new TestifyCorrelatedCeRow("leg-1", "accounts", "stable-row-1", "leg-1|accounts|AccountNumber|accountnumber|ACC-001", "ACC-001", "AccountNumber", "accountnumber")
        };

        var rows = await viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, existing, CancellationToken.None, "after patch 1");

        Assert.Equal("stable-row-1", rows["leg-1"].RowId);
        Assert.Equal("leg-1|accounts|AccountNumber|accountnumber|ACC-001", rows["leg-1"].DeterministicKey);
        Assert.Equal("ACC-001", rows["leg-1"].CorrelationValue);
        Assert.Equal("AccountNumber", rows["leg-1"].FoCorrelationField);
        Assert.Equal("accountnumber", rows["leg-1"].CeCorrelationField);
        Assert.Single(handler.RequestUris);
        Assert.Equal(
            "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$filter=accountnumber%20eq%20%27ACC-001%27&$select=accountnumber%2Ctbx_externalid%2Caccountsid",
            handler.RequestUris[0].AbsoluteUri);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenExplicitStableRowIdFieldIsMissing()
    {
        var handler = new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"account-row-1\",\"accountnumber\":\"ACC-001\"}]}");
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = new TestifyMapPlan(
            mapId: "map-timeout",
            mapDisplayName: "Timeout Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: config,
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "", "", "AccountNumber", "accountnumber", "tbx_externalid") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AccountNumber"] = "ACC-001" },
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, correlatedRows: null, CancellationToken.None, "after create"));

        Assert.Equal("CE verification failed for leg 'leg-1': correlated row did not expose a stable CE id.", ex.Message);
        Assert.Single(handler.RequestUris);
        Assert.Equal(
            "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$filter=accountnumber%20eq%20%27ACC-001%27&$select=accountnumber%2Ctbx_externalid%2Caccountsid",
            handler.RequestUris[0].AbsoluteUri);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenConventionalCeRowIdIsMissing()
    {
        var handler = new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountnumber\":\"ACC-001\",\"ownerid\":\"owner-row-9\"}]}");
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = new TestifyMapPlan(
            mapId: "map-timeout",
            mapDisplayName: "Timeout Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: config,
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "", "", "AccountNumber", "accountnumber") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AccountNumber"] = "ACC-001" },
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, correlatedRows: null, CancellationToken.None, "after create"));

        Assert.Equal("CE verification failed for leg 'leg-1': correlated row did not expose a stable CE id.", ex.Message);
        Assert.Single(handler.RequestUris);
        Assert.Equal(
            "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$filter=accountnumber%20eq%20%27ACC-001%27&$select=accountnumber%2Caccountsid",
            handler.RequestUris[0].AbsoluteUri);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_ReturnsStableRows_ForEachCorrelatedLeg()
    {
        var handler = new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"account-row-1\",\"name\":\"TESTIFY-001\"}]}",
            "{\"value\":[{\"contactid\":\"contact-row-1\",\"emailaddress1\":\"testify@example.com\"}]}");
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = new TestifyMapPlan(
            mapId: "map-timeout",
            mapDisplayName: "Timeout Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: config,
            foFilter: string.Empty,
            ceLegs: new[]
            {
                new TestifyLegPlan("leg-1", "accounts", string.Empty, string.Empty, "Name", "name"),
                new TestifyLegPlan("leg-2", "contacts", string.Empty, string.Empty, "Email", "emailaddress1")
            },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "TESTIFY-001",
                ["Email"] = "testify@example.com"
            },
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());

        var rows = await viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, correlatedRows: null, CancellationToken.None, "after create");

        Assert.Collection(
            rows.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase),
            pair =>
            {
                Assert.Equal("leg-1", pair.Key);
                Assert.Equal("account-row-1", pair.Value.RowId);
                Assert.Equal("leg-1|accounts|Name|name|TESTIFY-001", pair.Value.DeterministicKey);
                Assert.Equal("TESTIFY-001", pair.Value.CorrelationValue);
                Assert.Equal("Name", pair.Value.FoCorrelationField);
                Assert.Equal("name", pair.Value.CeCorrelationField);
            },
            pair =>
            {
                Assert.Equal("leg-2", pair.Key);
                Assert.Equal("contact-row-1", pair.Value.RowId);
                Assert.Equal("leg-2|contacts|Email|emailaddress1|testify@example.com", pair.Value.DeterministicKey);
                Assert.Equal("testify@example.com", pair.Value.CorrelationValue);
                Assert.Equal("Email", pair.Value.FoCorrelationField);
                Assert.Equal("emailaddress1", pair.Value.CeCorrelationField);
            });

        Assert.Collection(
            handler.RequestUris,
            uri => Assert.Equal(
                "https://contoso.crm.dynamics.com/api/data/v9.2/accounts?$filter=name%20eq%20%27TESTIFY-001%27&$select=name%2Caccountsid",
                uri.AbsoluteUri),
            uri => Assert.Equal(
                "https://contoso.crm.dynamics.com/api/data/v9.2/contacts?$filter=emailaddress1%20eq%20%27testify%40example.com%27&$select=emailaddress1%2Ccontactsid",
                uri.AbsoluteUri));
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenFoCorrelationValueChangesForExistingCeRow()
    {
        using var dataverseHttp = new HttpClient(new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-1\",\"name\":\"TESTIFY-UPDATED\"}]}"))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = CreateCorrelationPlan(config, "TESTIFY-UPDATED");
        var existing = new Dictionary<string, TestifyCorrelatedCeRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-1"] = new TestifyCorrelatedCeRow("leg-1", "accounts", "row-1", "leg-1|accounts|Name|name|TESTIFY-001", "TESTIFY-001", "Name", "name")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, existing, CancellationToken.None, "after patch 1"));

        Assert.Equal("CE verification failed for leg 'leg-1': expected correlation value 'TESTIFY-001' but FO field 'Name' resolved to 'TESTIFY-UPDATED'.", ex.Message);
    }

    [Fact]
    public async Task WaitForCorrelatedCeRowsAsync_Throws_WhenDeterministicKeyChangesForExistingRow()
    {
        using var dataverseHttp = new HttpClient(new SequenceJsonHttpMessageHandler(
            "{\"value\":[{\"accountid\":\"row-1\",\"name\":\"TESTIFY-001\"}]}"))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };

        var viewModel = CreateCorrelationViewModel(dataverseHttp, out var config);
        var plan = new TestifyMapPlan(
            mapId: "map-timeout",
            mapDisplayName: "Timeout Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: config,
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "", "", "Description", "name") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Description"] = "TESTIFY-001" },
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());
        var existing = new Dictionary<string, TestifyCorrelatedCeRow>(StringComparer.OrdinalIgnoreCase)
        {
            ["leg-1"] = new TestifyCorrelatedCeRow("leg-1", "accounts", "row-1", "leg-1|accounts|Name|name|TESTIFY-001", "TESTIFY-001", "Name", "name")
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCorrelatedCeRowsAsync(plan, plan.CreateValues, existing, CancellationToken.None, "after patch 1"));

        Assert.Equal("CE verification failed for leg 'leg-1': expected deterministic key 'leg-1|accounts|Name|name|TESTIFY-001' but resolved 'leg-1|accounts|Description|name|TESTIFY-001'.", ex.Message);
    }

    private static DualWriteMapBrowserViewModel CreateCorrelationViewModel(HttpClient dataverseHttp, out TestifyMapConfiguration config)
    {
        var writeClient = new SequenceODataWriteClient();
        var context = new FakeIntegrationDataverseWriteContext(writeClient, dataverseHttp);
        var storePath = CreateTempTestifyStorePath();
        var store = new TestifyConfigurationStore(storePath);
        var viewModel = new DualWriteMapBrowserViewModel(context, store);
        config = store.GetOrCreateAsync("env-1", "map-timeout", CancellationToken.None).GetAwaiter().GetResult();
        return viewModel;
    }

    private static string CreateTempTestifyStorePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), "toolbAX-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, "testify-integration.json");
    }

    private static TestifyMapPlan CreateCorrelationPlan(TestifyMapConfiguration config, string name)
    {
        return new TestifyMapPlan(
            mapId: "map-timeout",
            mapDisplayName: "Timeout Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: config,
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "", "", "Name", "name") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = name },
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());
    }

    private class FakeIntegrationContext : IPluginContext
    {
        public FakeIntegrationContext(IODataClient oData)
        {
            CurrentEnv = new FoEnvironment("env-1", "Env 1", "https://contoso.operations.dynamics.com", "tenant", "USMF");
            OData = oData;
            Catalog = new FakeCatalogService();
            Logger = NullLogger.Instance;
        }

        public FoEnvironment CurrentEnv { get; set; }
        public IODataClient OData { get; }
        public ICatalogService Catalog { get; }
        public Microsoft.Extensions.Logging.ILogger Logger { get; }
    }

    private class FakeIntegrationWriteContext : FakeIntegrationContext, IPluginContextWrite
    {
        public FakeIntegrationWriteContext(IODataWriteClient writeClient)
            : base(new EmptyODataClient())
        {
            ODataWrite = writeClient;
        }

        public IODataWriteClient ODataWrite { get; }
    }

    private sealed class FakeIntegrationDataverseWriteContext : FakeIntegrationWriteContext, IPluginContextDataverse
    {
        public FakeIntegrationDataverseWriteContext(IODataWriteClient writeClient, HttpClient dataverseHttp)
            : base(writeClient)
        {
            DataverseHttp = dataverseHttp;
            CurrentDataverseEnv = new DataverseEnvironment("dv-1", "https://contoso.crm.dynamics.com", "tenant");
        }

        public bool HasDataverseProfile => true;
        public DataverseEnvironment? CurrentDataverseEnv { get; }
        public HttpClient? DataverseHttp { get; }
    }

    private sealed class EmptyODataClient : IODataClient
    {
        public IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, CancellationToken cancellationToken = default) =>
            ODataClientExtensions.EmptyPages(cancellationToken);
    }

    private sealed class ThrowingODataClient : IODataClient
    {
        private readonly Exception _exception;

        public ThrowingODataClient(Exception exception)
        {
            _exception = exception;
        }

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            throw _exception;
#pragma warning disable CS0162
            yield break;
#pragma warning restore CS0162
        }
    }

    private sealed class SequenceODataWriteClient : IODataWriteClient
    {
        private readonly Queue<ODataWriteResponse> _responses;

        public SequenceODataWriteClient(params ODataWriteResponse[] responses)
        {
            _responses = new Queue<ODataWriteResponse>(responses);
        }

        public List<ODataWriteRequest> Requests { get; } = new();

        public Task<ODataWriteResponse> SendAsync(ODataWriteRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var response = _responses.Count > 0
                ? _responses.Dequeue()
                : new ODataWriteResponse(204, null, new Dictionary<string, string>());
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceJsonHttpMessageHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responses;

        public SequenceJsonHttpMessageHandler(params string[] json)
        {
            _responses = new Queue<string>(json);
        }

        public List<Uri> RequestUris { get; } = new();

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri is not null)
            {
                RequestUris.Add(request.RequestUri);
            }

            var json = _responses.Count > 0 ? _responses.Dequeue() : "{\"value\":[]}";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class FakeCatalogService : ICatalogService
    {
        public Task<TableCatalog> GetTablesAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default) =>
            Task.FromResult(new TableCatalog("test", "Test", DateTime.UtcNow, Array.Empty<TableInfo>()));

        public Task<ODataMetadata> GetODataMetadataAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default) =>
            Task.FromResult(new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null));

        public Task<CatalogSnapshot> GetSnapshotAsync(FoEnvironment env, CatalogRefreshMode mode, CancellationToken ct = default) =>
            Task.FromResult(new CatalogSnapshot(env.Id, env.BaseUrl, new TableCatalog("test", "Test", DateTime.UtcNow, Array.Empty<TableInfo>()), new ODataMetadata(Array.Empty<ODataEntity>(), Array.Empty<ODataEnumType>(), null), DateTime.UtcNow));

        public Task RefreshAsync(FoEnvironment env, CatalogRefreshScope scope, CancellationToken ct = default) => Task.CompletedTask;

        public Task<TableCatalog> ImportTableCatalogAsync(FoEnvironment env, string json, CancellationToken ct = default) =>
            Task.FromResult(new TableCatalog("import", "Import", DateTime.UtcNow, Array.Empty<TableInfo>()));

        public Task<string> GetTableBrowserUrlTemplateAsync(CancellationToken ct = default) =>
            Task.FromResult("{BaseUrl}/?mi=SysTableBrowser&table={TableName}");

        public Task SetTableBrowserUrlTemplateAsync(string template, CancellationToken ct = default) => Task.CompletedTask;

        public string BuildTableBrowserUrl(FoEnvironment env, string tableName) =>
            $"{env.BaseUrl}/?mi=SysTableBrowser&table={tableName}";

        public string BuildODataEntityUrl(FoEnvironment env, string entityName) =>
            $"{env.BaseUrl}/data/{entityName}";
    }
}
