using System.Collections.Generic;

namespace FoToolbox.Core.OData;

public sealed record QuerySpec(
    string Entity,
    bool CrossCompany = true,
    string? Company = null,
    IReadOnlyList<string>? Select = null,
    string? OrderBy = null,
    int? Top = null,
    int? Skip = null,
    string? Expand = null,
    string? Filter = null,
    bool Count = false);
