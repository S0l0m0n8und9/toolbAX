using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ToolBax.Core.Models;

namespace ToolBax.App.ViewModels;

/// <summary>
/// An OData comparison operator offered by the Query Builder filter tree. Function-style operators
/// (<c>startswith</c>/<c>endswith</c>/<c>contains</c>) render as <c>op(field,value)</c>; the rest as
/// <c>field op value</c>. Mirrors the prototype's <c>window.OPERATORS</c>.
/// </summary>
public sealed record QueryFilterOperator(string Op, string Symbol, bool IsFunction)
{
    public string Display => $"{Op} · {Symbol}";

    public static readonly IReadOnlyList<QueryFilterOperator> All = new[]
    {
        new QueryFilterOperator("eq", "=", false),
        new QueryFilterOperator("ne", "≠", false),
        new QueryFilterOperator("gt", ">", false),
        new QueryFilterOperator("ge", "≥", false),
        new QueryFilterOperator("lt", "<", false),
        new QueryFilterOperator("le", "≤", false),
        new QueryFilterOperator("startswith", "^=", true),
        new QueryFilterOperator("endswith", "$=", true),
        new QueryFilterOperator("contains", "~", true),
    };
}

/// <summary>
/// How a field's value is rendered into an OData <c>$filter</c> literal. The cases mirror the type names
/// <c>CoreMetadataService.MapType</c> actually emits: single-quoting a date/boolean/GUID/numeric is a
/// guaranteed 400 from F&amp;O ("incompatible types Edm.DateTimeOffset and Edm.String").
/// </summary>
public enum QueryLiteralKind
{
    /// <summary>Single-quoted, embedded quotes doubled — Edm.String and F&amp;O enum members.</summary>
    Quoted,

    /// <summary>Bare numeric literal (blank renders as <c>0</c>).</summary>
    Number,

    /// <summary>Bare <c>true</c>/<c>false</c>.</summary>
    Boolean,

    /// <summary>Bare GUID literal — OData v4 dropped the <c>guid'…'</c> prefixed form.</summary>
    Guid,

    /// <summary>Bare timestamp literal, e.g. <c>2026-01-01T00:00:00Z</c>.</summary>
    DateTime,

    /// <summary>Bare date literal, e.g. <c>2026-01-01</c>.</summary>
    Date,
}

/// <summary>
/// The field/enum metadata a filter condition needs to populate its field dropdown and pick the right
/// value editor (enum dropdown / numeric / text). Rebuilt whenever the selected entity's fields load.
/// </summary>
public sealed class QueryFilterContext
{
    private readonly Func<string, IReadOnlyList<string>> _enumMembers;
    private readonly Dictionary<string, EntityField> _byName;

    public QueryFilterContext(IReadOnlyList<EntityField> fields, Func<string, IReadOnlyList<string>> enumMembers)
    {
        _enumMembers = enumMembers;
        FieldNames = fields.Select(f => f.Name).ToList();
        _byName = fields.GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First());
    }

    public IReadOnlyList<string> FieldNames { get; }

    public EntityField? Meta(string? name) =>
        name is not null && _byName.TryGetValue(name, out var f) ? f : null;

    public IReadOnlyList<string> EnumMembers(string enumType) => _enumMembers(enumType);

    /// <summary>
    /// Classifies a field type (as produced by <c>CoreMetadataService.MapType</c>) into the OData literal
    /// syntax its values need. MapType emits "String"/"Decimal"/"DateTime"/"Date"/"Boolean"/"Guid"/"Enum"/
    /// "Collection" and passes the remaining Edm primitives through under their own names, so those are
    /// the names matched here — the WPF-era "Int"/"Real" spellings are never emitted.
    /// </summary>
    public static QueryLiteralKind LiteralKind(string? type) => type switch
    {
        // Every numeric MapType can produce: "Decimal" plus the Edm primitives it passes through.
        "Decimal" or "Int16" or "Int32" or "Int64" or "Double" or "Single" or "Byte" or "SByte"
            => QueryLiteralKind.Number,
        "Boolean" => QueryLiteralKind.Boolean,
        "Guid" => QueryLiteralKind.Guid,
        "DateTime" => QueryLiteralKind.DateTime,
        "Date" => QueryLiteralKind.Date,
        // String, Enum, Collection and anything unrecognised quote as text. F&O entities don't expose
        // Edm.Duration/TimeOfDay/Binary, and a wrongly-quoted value is a loud 400 rather than a silently
        // mis-scoped query.
        _ => QueryLiteralKind.Quoted,
    };

    /// <summary>True when the type renders as a bare numeric literal.</summary>
    public static bool IsNumericType(string? type) => LiteralKind(type) == QueryLiteralKind.Number;

    /// <summary>True when the type is a plain string — the only argument type OData's string functions
    /// (<c>contains</c>/<c>startswith</c>/<c>endswith</c>) accept. An unknown field (no metadata) is
    /// treated as a string, matching how <see cref="LiteralKind"/> quotes it.</summary>
    public static bool IsStringType(string? type) => type is null or "String";
}

/// <summary>Base for the recursive filter tree (a <see cref="QueryFilterGroup"/> or
/// <see cref="QueryFilterCondition"/>). Each node carries a "remove me" command its parent assigns.</summary>
public abstract partial class QueryFilterNode : ObservableObject
{
    /// <summary>Removes this node from its parent group; null on the root (so its remove button hides).</summary>
    public IRelayCommand? RemoveSelfCommand { get; set; }

    public bool CanRemove => RemoveSelfCommand is not null;

    /// <summary>Renders this node to an OData <c>$filter</c> fragment, or null when it contributes nothing.</summary>
    public abstract string? Render();

    /// <summary>Number of leaf conditions under this node (1 for a condition).</summary>
    public abstract int ConditionCount { get; }
}

/// <summary>A single <c>field op value</c> (or function) condition in the filter tree.</summary>
public sealed partial class QueryFilterCondition : QueryFilterNode
{
    private readonly QueryFilterContext _context;
    private readonly Action _onChanged;

    public QueryFilterCondition(QueryFilterContext context, Action onChanged)
    {
        _context = context;
        _onChanged = onChanged;
        _field = context.FieldNames.FirstOrDefault();
    }

    public IReadOnlyList<string> FieldNames => _context.FieldNames;

    // Function operators (contains/startswith/endswith) take string arguments only; hide them for every
    // other type so an invalid expression like contains(CreditLimit,10000) or
    // contains(CreatedDateTime,2026-01-01) can't be composed.
    public IReadOnlyList<QueryFilterOperator> Operators =>
        SupportsFunctions
            ? QueryFilterOperator.All
            : QueryFilterOperator.All.Where(o => !o.IsFunction).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEnum))]
    [NotifyPropertyChangedFor(nameof(IsNumeric))]
    [NotifyPropertyChangedFor(nameof(SupportsFunctions))]
    [NotifyPropertyChangedFor(nameof(EnumMembers))]
    [NotifyPropertyChangedFor(nameof(Operators))]
    private string? _field;

    [ObservableProperty]
    private QueryFilterOperator _operator = QueryFilterOperator.All[0];

    [ObservableProperty]
    private string _value = string.Empty;

    public bool IsEnum => _context.Meta(Field)?.Type == "Enum";

    /// <summary>True when this field's value renders as a bare numeric literal.</summary>
    public bool IsNumeric => QueryFilterContext.IsNumericType(_context.Meta(Field)?.Type);

    /// <summary>True when this field can take OData's string functions (a plain string field).</summary>
    public bool SupportsFunctions => QueryFilterContext.IsStringType(_context.Meta(Field)?.Type);

    public IReadOnlyList<string> EnumMembers
    {
        get
        {
            var meta = _context.Meta(Field);
            return meta is { Type: "Enum", EnumType: { } enumType }
                ? _context.EnumMembers(enumType)
                : Array.Empty<string>();
        }
    }

    // Changing the field clears the value (an enum value rarely makes sense against a new field's type).
    partial void OnFieldChanged(string? value)
    {
        Value = string.Empty;
        // If the field switched to a non-string type while a string-only function operator was selected,
        // fall back to a comparison operator so the rendered filter stays valid.
        if (!SupportsFunctions && Operator.IsFunction)
        {
            Operator = QueryFilterOperator.All[0]; // eq
        }

        _onChanged();
    }

    partial void OnOperatorChanged(QueryFilterOperator value) => _onChanged();

    partial void OnValueChanged(string value) => _onChanged();

    public override int ConditionCount => 1;

    public override string? Render()
    {
        // Whitespace-only is treated as empty (so a stray space can't render as a bare 0 / '').
        if (string.IsNullOrEmpty(Field) || string.IsNullOrWhiteSpace(Value))
        {
            return null;
        }

        var literal = FormatValue(_context.Meta(Field), Value);
        return Operator.IsFunction
            ? $"{Operator.Op}({Field},{literal})"
            : $"{Field} {Operator.Op} {literal}";
    }

    // Literal syntax per type: numerics, Booleans, GUIDs and date/times emit a bare literal (quoting them
    // is an "incompatible types" 400 from F&O); strings and enum members are single-quoted with embedded
    // quotes doubled per OData escaping. Well-formed input renders faithfully; malformed input passes
    // through so the server rejects it loudly rather than the builder silently rewriting it.
    private static string FormatValue(EntityField? meta, string raw) =>
        QueryFilterContext.LiteralKind(meta?.Type) switch
        {
            QueryLiteralKind.Number => string.IsNullOrWhiteSpace(raw) ? "0" : raw,
            QueryLiteralKind.Boolean => FormatBoolean(raw),
            QueryLiteralKind.Guid or QueryLiteralKind.DateTime or QueryLiteralKind.Date => raw,
            _ => $"'{raw.Replace("'", "''")}'",
        };

    // OData wants lowercase true/false, so a typed "True"/"TRUE" is normalised. Anything that isn't a
    // boolean word goes through as typed — the server rejects it, the same way a non-numeric value in a
    // numeric field behaves.
    private static string FormatBoolean(string raw) =>
        bool.TryParse(raw, out var parsed) ? (parsed ? "true" : "false") : raw;
}

/// <summary>An AND/OR group of child conditions and nested groups.</summary>
public sealed partial class QueryFilterGroup : QueryFilterNode
{
    private readonly QueryFilterContext _context;
    private readonly Action _onChanged;

    public QueryFilterGroup(QueryFilterContext context, Action onChanged, bool isRoot)
    {
        _context = context;
        _onChanged = onChanged;
        IsRoot = isRoot;
    }

    public bool IsRoot { get; }

    public ObservableCollection<QueryFilterNode> Children { get; } = new();

    /// <summary>"and" or "or" — how this group's children are combined.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsAnd))]
    [NotifyPropertyChangedFor(nameof(IsOr))]
    private string _op = "and";

    public bool IsAnd => Op == "and";

    public bool IsOr => Op == "or";

    public string HeaderText =>
        $"{(IsRoot ? "match" : "group")} · {Children.Count} item{(Children.Count == 1 ? string.Empty : "s")}";

    public bool IsEmptyNonRoot => !IsRoot && Children.Count == 0;

    [RelayCommand]
    private void SetOp(string op)
    {
        Op = op;
        _onChanged();
    }

    [RelayCommand]
    private void AddCondition()
    {
        Adopt(new QueryFilterCondition(_context, _onChanged));
        AfterChildrenChanged();
    }

    [RelayCommand]
    private void AddGroup()
    {
        Adopt(new QueryFilterGroup(_context, _onChanged, isRoot: false) { Op = "or" });
        AfterChildrenChanged();
    }

    // Wires a child's "remove me" command to drop it from this group, then adds it.
    private void Adopt(QueryFilterNode child)
    {
        child.RemoveSelfCommand = new RelayCommand(() =>
        {
            Children.Remove(child);
            AfterChildrenChanged();
        });
        Children.Add(child);
    }

    private void AfterChildrenChanged()
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(IsEmptyNonRoot));
        _onChanged();
    }

    public override int ConditionCount => Children.Sum(c => c.ConditionCount);

    public override string? Render()
    {
        var parts = Children.Select(c => c.Render()).Where(p => !string.IsNullOrEmpty(p)).ToList();
        if (parts.Count == 0)
        {
            return null;
        }

        if (parts.Count == 1)
        {
            return parts[0];
        }

        // Nested groups wrap in parens to bind precedence; the root doesn't need redundant outer parens.
        var joined = string.Join($" {Op} ", parts);
        return IsRoot ? joined : $"({joined})";
    }
}
