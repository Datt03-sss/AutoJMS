using System;

namespace AutoJMS;

public sealed class PrintPipelineException : Exception
{
    public PrintPipelineException(string stage, string userMessage, string message, Exception innerException = null)
        : base(message, innerException)
    {
        Stage = stage ?? "";
        UserMessage = string.IsNullOrWhiteSpace(userMessage) ? "Unknown exception" : userMessage;
    }

    public string Stage { get; }
    public string UserMessage { get; }
}
