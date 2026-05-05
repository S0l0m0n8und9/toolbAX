using DualWriteMapBrowserPlugin;
using FoToolbox.Core.Catalog;
using FoToolbox.Core.Models;
using FoToolbox.Core.OData;
using FoToolbox.SDK.Plugins;
using Microsoft.Extensions.Logging.Abstractions;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Runtime.CompilerServices;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class DualWriteMapBrowserTestifyIntegrationTests
{
    [Fact]
    public async Task VerifyFoPersistedValuesAsync_AllowsMatchingCreateReadback()
    {
        var entity = BuildCustomersEntity();
        var oData = new StaticRowODataClient(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["AccountNumber"] = "CUST-0001",
            ["dataAreaId"] = "USMF",
            ["Name"] = "TESTIFY-CREATE"
        });
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationContext(oData),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));

        await viewModel.VerifyFoPersistedValuesAsync(
            "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true",
            entity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["AccountNumber"] = "CUST-0001",
                ["dataAreaId"] = "USMF",
                ["Name"] = "TESTIFY-CREATE"
            },
            "FO create",
            CancellationToken.None);

        Assert.Single(oData.Requests);
        Assert.Equal("https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true", oData.Requests[0]);
    }

    [Fact]
    public async Task VerifyFoPersistedValuesAsync_ThrowsDetailedMismatch_ForCreateReadback()
    {
        var entity = BuildCustomersEntity();
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationContext(new StaticRowODataClient(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["AccountNumber"] = "CUST-0001",
                ["dataAreaId"] = "USMF",
                ["Name"] = "ACTUAL-NAME"
            })),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.VerifyFoPersistedValuesAsync(
            "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true",
            entity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "EXPECTED-NAME"
            },
            "FO create",
            CancellationToken.None));

        Assert.Equal("FO create succeeded but persisted value for 'Name' was 'ACTUAL-NAME' instead of expected 'EXPECTED-NAME'.", ex.Message);
    }

    [Fact]
    public async Task VerifyFoPersistedValuesAsync_ThrowsDetailedMismatch_ForPatchReadback()
    {
        var entity = BuildCustomersEntity();
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationContext(new StaticRowODataClient(new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["AccountNumber"] = "CUST-0001",
                ["dataAreaId"] = "USMF",
                ["Name"] = "PATCH-0"
            })),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => viewModel.VerifyFoPersistedValuesAsync(
            "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-0001',dataAreaId='USMF')?cross-company=true",
            entity,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "PATCH-1"
            },
            "FO PATCH step 1",
            CancellationToken.None));

        Assert.Equal("FO PATCH step 1 succeeded but persisted value for 'Name' was 'PATCH-0' instead of expected 'PATCH-1'.", ex.Message);
    }

    [Fact]
    public async Task CheckFoRecordExistsAsync_ReturnsFalse_ForStaleCachedEntityUrl()
    {
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationContext(new ThrowingODataClient(new HttpRequestException("404 stale cached URL"))),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));

        var exists = await viewModel.CheckFoRecordExistsAsync(
            "https://contoso.operations.dynamics.com/data/CustomersV3(AccountNumber='CUST-404',dataAreaId='USMF')?cross-company=true",
            CancellationToken.None);

        Assert.False(exists);
    }

    [Fact]
    public async Task FinalizeTestifyFailureAsync_RollsBackMidRunFailure_AndClearsPersistedState()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json");

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
    public async Task WaitForCeCorrelationAsync_ThrowsTimeout_WhenCorrelatedRowDoesNotAppear()
    {
        var originalDelay = DualWriteMapBrowserViewModel.TestifyDelayAsync;
        var originalUtcNow = DualWriteMapBrowserViewModel.TestifyUtcNow;
        var now = DateTimeOffset.UtcNow;
        DualWriteMapBrowserViewModel.TestifyDelayAsync = static (_, _) => Task.CompletedTask;
        DualWriteMapBrowserViewModel.TestifyUtcNow = () => now = now.AddMinutes(2);

        try
        {
            using var dataverseHttp = new HttpClient(new StaticJsonHttpMessageHandler("{\"value\":[]}"))
            {
                BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
            };
            var writeClient = new SequenceODataWriteClient();
            var context = new FakeIntegrationDataverseWriteContext(writeClient, dataverseHttp);
            var storePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json");
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
                ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "$filter=name eq 'Timeout'", "Name", "name") },
                createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
                createPayloadJson: "{}",
                enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
                patchSteps: Array.Empty<TestifyPatchStep>(),
                ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
                warnings: Array.Empty<string>(),
                coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
                blockingIssues: Array.Empty<string>());

            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                viewModel.WaitForCeCorrelationAsync(
                    plan,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "TESTIFY-TIMEOUT" },
                    expectedRowIdentities: null,
                    CancellationToken.None,
                    "after patch 1"));

            Assert.Equal("CE correlation verification timed out (after patch 1) after 1 minute(s). Increase CePollTimeoutMinutes in Testify configuration if sync is slow.", ex.Message);
        }
        finally
        {
            DualWriteMapBrowserViewModel.TestifyDelayAsync = originalDelay;
            DualWriteMapBrowserViewModel.TestifyUtcNow = originalUtcNow;
        }
    }

    [Fact]
    public async Task WaitForCeCorrelationAsync_Throws_WhenDuplicateRowsMatchCorrelation()
    {
        using var dataverseHttp = new HttpClient(new StaticJsonHttpMessageHandler("""
{"value":[{"accountid":"11111111-1111-1111-1111-111111111111","name":"TESTIFY-DUP"},{"accountid":"22222222-2222-2222-2222-222222222222","name":"TESTIFY-DUP"}]}
"""))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationDataverseWriteContext(new SequenceODataWriteClient(), dataverseHttp),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));
        var plan = BuildCeCorrelationPlan(new TestifyMapConfiguration { CePollTimeoutMinutes = 1 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCeCorrelationAsync(
                plan,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "TESTIFY-DUP" },
                expectedRowIdentities: null,
                CancellationToken.None,
                "after create"));

        Assert.Contains("matched 2 rows", ex.Message);
    }

    [Fact]
    public async Task WaitForCeCorrelationAsync_Throws_WhenReturnedRowDoesNotMatchExpectedCorrelation()
    {
        using var dataverseHttp = new HttpClient(new StaticJsonHttpMessageHandler("""
{"value":[{"accountid":"11111111-1111-1111-1111-111111111111","name":"UNRELATED"}]}
"""))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationDataverseWriteContext(new SequenceODataWriteClient(), dataverseHttp),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));
        var plan = BuildCeCorrelationPlan(new TestifyMapConfiguration { CePollTimeoutMinutes = 1 });

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCeCorrelationAsync(
                plan,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "TESTIFY-EXPECTED" },
                expectedRowIdentities: null,
                CancellationToken.None,
                "after create"));

        Assert.Contains("returned an unrelated row", ex.Message);
    }

    [Fact]
    public async Task WaitForCeCorrelationAsync_Throws_WhenPatchMatchesDifferentCeRowIdentity()
    {
        using var dataverseHttp = new HttpClient(new StaticJsonHttpMessageHandler(
            """
{"value":[{"accountid":"22222222-2222-2222-2222-222222222222","name":"TESTIFY-ROW"}]}
"""))
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationDataverseWriteContext(new SequenceODataWriteClient(), dataverseHttp),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));
        var plan = BuildCeCorrelationPlan(new TestifyMapConfiguration { CePollTimeoutMinutes = 1 });
        var runtimeValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "TESTIFY-ROW" };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            viewModel.WaitForCeCorrelationAsync(
                plan,
                runtimeValues,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leg-1"] = "11111111-1111-1111-1111-111111111111"
                },
                CancellationToken.None,
                "after patch 1"));

        Assert.Contains("matched a different row after update", ex.Message);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_PassesCreatePhase_ForEnumValueMapAndScalar()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "statuscode",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Open"] = "1" },
                defaultValue: null),
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "IsActive",
                foFieldType: "Edm.Boolean",
                ceField: "isactive",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null)
        });

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "Open",
                ["IsActive"] = "true"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["statuscode"] = "1",
                    ["isactive"] = "true"
                }
            },
            "Create");

        Assert.Equal(2, assertions.Count);
        Assert.All(assertions, assertion => Assert.True(assertion.Passed));
    }

    [Fact]
    public void EvaluateCeFieldAssertions_ThrowsPatchMismatch_ForEnumValueMapOutput()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "statuscode",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Open"] = "1",
                    ["Closed"] = "2"
                },
                defaultValue: null)
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
                plan,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Status"] = "Closed"
                },
                new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["statuscode"] = "1"
                    }
                },
                "Patch 1"));

        Assert.Contains("Patch 1", ex.Message);
        Assert.Contains("statuscode", ex.Message);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_NormalizesScalarValues()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Quantity",
                foFieldType: "Edm.Int32",
                ceField: "new_quantity",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null),
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "IsPreferred",
                foFieldType: "Edm.Boolean",
                ceField: "new_ispreferred",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null)
        });

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Quantity"] = "1",
                ["IsPreferred"] = "1"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_quantity"] = "1.0",
                    ["new_ispreferred"] = "true"
                }
            },
            "Create");

        Assert.Equal(2, assertions.Count);
        Assert.All(assertions, assertion => Assert.True(assertion.Passed));
    }

    [Fact]
    public void EvaluateCeFieldAssertions_NormalizesDirectGuidValues()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "ExternalId",
                foFieldType: "Edm.Guid",
                ceField: "new_externalid",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null)
        });

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ExternalId"] = "3F2504E0-4F89-11D3-9A0C-0305E82C3301"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_externalid"] = "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}"
                }
            },
            "Create");

        var assertion = Assert.Single(assertions);
        Assert.True(assertion.Passed);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_NormalizesGuidShapedValueMapOutputs()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "new_statuslookup",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Open"] = "3F2504E0-4F89-11D3-9A0C-0305E82C3301"
                },
                defaultValue: null)
        });

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "Open"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_statuslookup"] = "{3f2504e0-4f89-11d3-9a0c-0305e82c3301}"
                }
            },
            "Patch 1");

        var assertion = Assert.Single(assertions);
        Assert.True(assertion.Passed);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_NormalizesBooleanShapedValueMapOutputs()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "new_isenabled",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Enabled"] = "1"
                },
                defaultValue: "0")
        });

        var mappedAssertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "Enabled"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_isenabled"] = "true"
                }
            },
            "Create");

        var mappedAssertion = Assert.Single(mappedAssertions);
        Assert.True(mappedAssertion.Passed);

        var defaultAssertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "Disabled"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_isenabled"] = "false"
                }
            },
            "Patch 1");

        var defaultAssertion = Assert.Single(defaultAssertions);
        Assert.True(defaultAssertion.Passed);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_PreservesMappedNullOutputs()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "new_optionalvalue",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Cleared"] = null
                },
                defaultValue: null)
        });

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "Cleared"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_optionalvalue"] = null
                }
            },
            "Create");

        var assertion = Assert.Single(assertions);
        Assert.True(assertion.Passed);
        Assert.Null(assertion.ExpectedValue);
        Assert.Null(assertion.ActualValue);
        Assert.Equal("<null>", assertion.ExpectedDisplay);
        Assert.Equal("<null>", assertion.ActualDisplay);
        Assert.Equal("Create new_optionalvalue expected '<null>' actual '<null>'", assertion.Detail);
    }

    [Fact]
    public void TryBuildCeValueMapAssertionPlan_PreservesExplicitNullDefaultOutput()
    {
        var method = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("TryBuildCeValueMapAssertionPlan", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        var args = new object?[]
        {
            "leg-1",
            "Status",
            "Edm.String",
            "new_optionalvalue",
            new[]
            {
                new MappingValueTransformRow(
                    legId: "leg-1",
                    sourceField: "Status",
                    destinationField: "new_optionalvalue",
                    transformType: "ValueMap",
                    defaultValue: (string)null!,
                    hasDefaultValue: true,
                    valueMap: """{"Enabled":"1"}""",
                    createValuesOnDestination: null)
            },
            null,
            null
        };

        var built = (bool)method.Invoke(null, args)!;
        Assert.True(built);

        var plan = Assert.IsType<TestifyCeFieldPlan>(args[5]);
        var hasDefaultValueProperty = typeof(TestifyCeFieldPlan).GetProperty("HasDefaultValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
        Assert.NotNull(hasDefaultValueProperty);
        Assert.True(Assert.IsType<bool>(hasDefaultValueProperty!.GetValue(plan)));

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            BuildCeAssertionPlan(new[] { plan }),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Status"] = "Disabled"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_optionalvalue"] = null
                }
            },
            "Patch 1");

        var assertion = Assert.Single(assertions);
        Assert.True(assertion.Passed);
        Assert.Null(assertion.ExpectedValue);
        Assert.Null(assertion.ActualValue);
        Assert.Equal("<null>", assertion.ExpectedDisplay);
        Assert.Equal("<null>", assertion.ActualDisplay);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_ThrowsPatchMismatch_ForBooleanShapedValueMapOutputs()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "new_isenabled",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Enabled"] = "1"
                },
                defaultValue: null)
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
                plan,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Status"] = "Enabled"
                },
                new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["new_isenabled"] = "false"
                    }
                },
                "Patch 1"));

        Assert.Contains("Patch 1", ex.Message);
        Assert.Contains("new_isenabled", ex.Message);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_ThrowsPatchMismatch_WhenActualCeValueIsNull()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "statuscode",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Open"] = "1"
                },
                defaultValue: null)
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
                plan,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Status"] = "Open"
                },
                new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["statuscode"] = null
                    }
                },
                "Patch 1"));

        Assert.Equal("CE Patch 1 assertion failed for leg 'leg-1' field 'statuscode': expected '1' but found '<null>'.", ex.Message);
    }

    [Fact]
    public void EvaluateCeFieldAssertions_NormalizesTemporalValues()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "InvoiceDate",
                foFieldType: "Edm.Date",
                ceField: "new_invoicedate",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null),
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "ModifiedDateTime",
                foFieldType: "Edm.DateTimeOffset",
                ceField: "new_modifieddatetime",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null)
        });

        var assertions = DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InvoiceDate"] = "2026-05-05",
                ["ModifiedDateTime"] = "2026-05-05T12:34:56Z"
            },
            new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
            {
                ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["new_invoicedate"] = "2026-05-05T00:00:00-07:00",
                    ["new_modifieddatetime"] = "2026-05-05T05:34:56-07:00"
                }
            },
            "Create");

        Assert.Equal(2, assertions.Count);
        Assert.All(assertions, assertion => Assert.True(assertion.Passed));
    }

    [Fact]
    public void EvaluateCeFieldAssertions_ThrowsPatchMismatch_ForTemporalValues()
    {
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "ModifiedDateTime",
                foFieldType: "Edm.DateTimeOffset",
                ceField: "new_modifieddatetime",
                kind: TestifyCeFieldAssertionKind.DirectScalar,
                valueMap: null,
                defaultValue: null)
        });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DualWriteMapBrowserViewModel.EvaluateCeFieldAssertions(
                plan,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModifiedDateTime"] = "2026-05-05T12:34:56Z"
                },
                new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase)
                {
                    ["leg-1"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["new_modifieddatetime"] = "2026-05-05T05:35:56-07:00"
                    }
                },
                "Patch 1"));

        Assert.Contains("Patch 1", ex.Message);
        Assert.Contains("new_modifieddatetime", ex.Message);
    }

    [Fact]
    public void IsSupportedDirectCeScalarType_IncludesTemporalTypes()
    {
        var method = typeof(DualWriteMapBrowserViewModel)
            .GetMethod("IsSupportedDirectCeScalarType", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        Assert.True((bool)method.Invoke(null, new object[] { "Edm.Date" })!);
        Assert.True((bool)method.Invoke(null, new object[] { "Edm.DateTimeOffset" })!);
    }

    [Fact]
    public async Task ReadCorrelatedCeRowsAsync_SelectsCeAssertionColumns()
    {
        var handler = new CapturingJsonHttpMessageHandler("""
{"value":[{"accountid":"11111111-1111-1111-1111-111111111111","name":"TESTIFY-ROW","statuscode":"1"}]}
""");
        using var dataverseHttp = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://contoso.crm.dynamics.com/")
        };
        var viewModel = new DualWriteMapBrowserViewModel(
            new FakeIntegrationDataverseWriteContext(new SequenceODataWriteClient(), dataverseHttp),
            new TestifyConfigurationStore(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-integration.json")));
        var plan = BuildCeAssertionPlan(new[]
        {
            new TestifyCeFieldPlan(
                legId: "leg-1",
                foField: "Status",
                foFieldType: "Edm.String",
                ceField: "statuscode",
                kind: TestifyCeFieldAssertionKind.ValueMap,
                valueMap: new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["Open"] = "1" },
                defaultValue: null)
        });

        var rows = await viewModel.ReadCorrelatedCeRowsAsync(
            plan,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Name"] = "TESTIFY-ROW" },
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["leg-1"] = "11111111-1111-1111-1111-111111111111" },
            CancellationToken.None);

        Assert.True(rows.ContainsKey("leg-1"));
        Assert.NotNull(handler.LastRequestUri);
        Assert.Contains("$select=", handler.LastRequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("statuscode", handler.LastRequestUri!.ToString(), StringComparison.OrdinalIgnoreCase);
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

    private sealed class StaticRowODataClient : IODataClient
    {
        private readonly IReadOnlyDictionary<string, object?>? _row;

        public StaticRowODataClient(IReadOnlyDictionary<string, object?>? row)
        {
            _row = row;
        }

        public List<string> Requests { get; } = new();

        public async IAsyncEnumerable<ODataPage> StreamAsync(QueryRequest request, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Requests.Add(request.Url);
            await Task.Yield();

            if (_row is null)
            {
                yield break;
            }

            yield return new ODataPage(new[] { _row }, null);
        }
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

    private sealed class StaticJsonHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public StaticJsonHttpMessageHandler(string json)
        {
            _json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }

    private sealed class CapturingJsonHttpMessageHandler : HttpMessageHandler
    {
        private readonly string _json;

        public CapturingJsonHttpMessageHandler(string json)
        {
            _json = json;
        }

        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_json, Encoding.UTF8, "application/json")
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

    private static ODataEntity BuildCustomersEntity()
    {
        return new ODataEntity(
            "CustomersV3",
            new[]
            {
                new ODataProperty("AccountNumber", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true, MaxLength: "20"),
                new ODataProperty("dataAreaId", "Edm.String", Nullable: false, IsKey: true, IsMandatory: true, MaxLength: "4"),
                new ODataProperty("Name", "Edm.String", Nullable: true, MaxLength: "60")
            },
            Array.Empty<ODataNavigationProperty>());
    }

    private static TestifyMapPlan BuildCeCorrelationPlan(TestifyMapConfiguration configuration)
    {
        return new TestifyMapPlan(
            mapId: "map-correlation",
            mapDisplayName: "Correlation Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: configuration,
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "$filter=statecode eq 0", "Name", "name") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: Array.Empty<TestifyCeFieldPlan>(),
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());
    }

    private static TestifyMapPlan BuildCeAssertionPlan(IReadOnlyList<TestifyCeFieldPlan> ceFieldPlans)
    {
        return new TestifyMapPlan(
            mapId: "map-assertions",
            mapDisplayName: "Assertion Map",
            foEntity: "CustomersV3",
            foEntityDetails: null,
            configuration: new TestifyMapConfiguration { CePollTimeoutMinutes = 1 },
            foFilter: string.Empty,
            ceLegs: new[] { new TestifyLegPlan("leg-1", "accounts", "$filter=statecode eq 0", "Name", "name") },
            createValues: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            createPayloadJson: "{}",
            enumFields: new Dictionary<string, TestifyEnumFieldPlan>(StringComparer.OrdinalIgnoreCase),
            patchSteps: Array.Empty<TestifyPatchStep>(),
            ceFieldPlans: ceFieldPlans,
            warnings: Array.Empty<string>(),
            coverageGaps: Array.Empty<TestifyEnumCoverageGap>(),
            blockingIssues: Array.Empty<string>());
    }
}
