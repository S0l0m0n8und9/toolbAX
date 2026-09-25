using System;

namespace ToolBax.Core.Services;

/// <summary>A structural metadata-response failure. Messages identify only the response location.</summary>
public sealed class MetadataResponseFormatException : FormatException
{
    public MetadataResponseFormatException(string message) : base(message) { }
    public MetadataResponseFormatException(string message, Exception innerException) : base(message, innerException) { }
}
