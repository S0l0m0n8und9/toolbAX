using System.Collections.Generic;

namespace FoToolbox.Core.OData;

public abstract record FilterNode;

public sealed record FilterCondition(string Field, string Operator, string Value) : FilterNode;

public sealed record FilterGroup(string LogicalOperator, IReadOnlyList<FilterNode> Children) : FilterNode;
