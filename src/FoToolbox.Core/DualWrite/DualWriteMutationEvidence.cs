using System;
using System.Collections.Generic;
using System.Threading;

namespace FoToolbox.Core.DualWrite;

/// <summary>Observation only: dispatch does not prove delivery; HTTP acknowledgment does not prove completion.</summary>
public sealed record DualWriteMutationEvidence(bool? DispatchStarted, int? StatusCode = null,
    bool BodyComplete = false, string? FailureKind = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public interface IDualWriteMutationFailure
{
    DualWriteMutationEvidence? Evidence { get; }
}

public sealed class DualWriteMutationValidationException : ArgumentException, IDualWriteMutationFailure
{
    public DualWriteMutationValidationException(ArgumentException inner) : base(inner.Message, inner.ParamName, inner) { }
    public DualWriteMutationEvidence Evidence { get; } = new(false);
}

public sealed class DualWriteMutationException : Exception, IDualWriteMutationFailure
{
    public DualWriteMutationException(DualWriteMutationEvidence evidence, Exception inner)
        : base("Dual-write mutation observation failed; inspect transport evidence before any further action.", inner)
        => Evidence = evidence;
    public DualWriteMutationEvidence Evidence { get; }
}

public sealed class DualWriteMutationCanceledException : OperationCanceledException, IDualWriteMutationFailure
{
    public DualWriteMutationCanceledException(DualWriteMutationEvidence evidence, OperationCanceledException inner,
        CancellationToken token) : base("Dual-write mutation observation cancelled; outcome may be unknown.", inner, token)
        => Evidence = evidence;
    public DualWriteMutationEvidence Evidence { get; }
}
