namespace CloudSave.Agent.Core;

public sealed record AgentEvent(
    DateTimeOffset Timestamp,
    string Level,
    string Type,
    string Message,
    string? Code = null,
    object? Data = null
)
{
    public static AgentEvent Info(string type, string message, object? data = null) =>
        new(DateTimeOffset.UtcNow, "info", type, message, null, data);

    public static AgentEvent Warn(string type, string code, string message, object? data = null) =>
        new(DateTimeOffset.UtcNow, "warn", type, message, code, data);

    public static AgentEvent Error(string type, string code, string message, object? data = null) =>
        new(DateTimeOffset.UtcNow, "error", type, message, code, data);
}
