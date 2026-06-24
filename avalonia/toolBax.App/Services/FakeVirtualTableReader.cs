using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// Design-mode / headless <see cref="IVirtualTableReader"/>: returns a representative mix of
/// F&amp;O-backed and other virtual tables so the screen renders without a live environment.
/// </summary>
public sealed class FakeVirtualTableReader : IVirtualTableReader
{
    public Task<VirtualTableLoadResult> GetVirtualTablesAsync(CancellationToken ct = default) =>
        Task.FromResult(VirtualTableLoadResult.Ok(new[]
        {
            new VirtualTableInfo("mserp_custcustomerv3entity", "Customer V3", "CustCustomerV3Entity",
                "mserp_custcustomerv3entities", "11111111-1111-1111-1111-111111111111",
                "22222222-2222-2222-2222-222222222222", IsManaged: true, VirtualTableSource.FinanceAndOperations),
            new VirtualTableInfo("mserp_vendvendorv2entity", "Vendor V2", "VendVendorV2Entity",
                "mserp_vendvendorv2entities", "11111111-1111-1111-1111-111111111111",
                "22222222-2222-2222-2222-222222222222", IsManaged: true, VirtualTableSource.FinanceAndOperations),
            new VirtualTableInfo("contoso_sqlproduct", "SQL Product", "Products",
                "contoso_sqlproducts", "33333333-3333-3333-3333-333333333333",
                "44444444-4444-4444-4444-444444444444", IsManaged: false, VirtualTableSource.Other),
        }));
}
