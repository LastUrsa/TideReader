using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace TideReader.Backend.Services;

// This store contains only safe operational metadata, never request bodies,
// legacy error text, now-playing data, browser titles, or credential material.
public sealed partial class SipObservability(string appVersion, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _lock = new();
    private readonly Dictionary<string, SipOperation> _operations = [];
    private readonly Dictionary<string, string> _snapshots = [];
    private readonly Dictionary<string, DateTimeOffset> _changes = [];
    private readonly Dictionary<string, SipOperation> _confirmed = [];
    public string SessionId { get; } = NewId();
    public DateTimeOffset StartedAt { get; } = (clock ?? TimeProvider.System).GetUtcNow();
    private static string NewId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdPattern();
    internal static string CorrelationOrNew(string value) => IdPattern().IsMatch(value) ? value : NewId();

    public SipOperation? Get(string id)
    {
        lock (_lock) { var operation = _operations.GetValueOrDefault(id); if (operation is not null && operation.Outcome != "running" && _clock.GetUtcNow() - operation.StartedAt > TimeSpan.FromDays(14)) { _operations.Remove(id); return null; } return operation; }
    }

    public object Diagnostics(object dependencies) => new
    {
        AppVersion = appVersion, BuildVersion = SafeBuildVersion(), ProtocolVersion = 1, SessionId, StartedAt,
        UptimeSeconds = (long)(_clock.GetUtcNow() - StartedAt).TotalSeconds,
        Dependencies = dependencies
    };

    public async Task InvokeAsync(HttpContext context, RequestDelegate next, SipService service)
    {
        var requestId = context.Request.Headers["X-Request-ID"].ToString();
        if (requestId.Length == 0) requestId = NewId();
        if (!IdPattern().IsMatch(requestId))
        {
            context.Response.Headers["X-Request-ID"] = NewId();
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new { Success = false, Error = "INVALID_REQUEST", ErrorCode = "INVALID_REQUEST", Message = "The request is invalid.", Retryable = false, SessionId, RequestId = context.Response.Headers["X-Request-ID"].ToString(), ObservedAt = _clock.GetUtcNow() });
            return;
        }
        context.Response.Headers["X-Request-ID"] = requestId;
        var route = context.Request.Path.Value ?? "";
        SipOperation? operation = null;
        var mutation = HttpMethods.IsPost(context.Request.Method) && (route is "/api/v1/profile" or "/api/v1/browser-support");
        if (mutation)
        {
            lock (_lock)
            {
                foreach (var entry in _operations.Values.Where(op => op.Outcome != "running" && _clock.GetUtcNow() - op.StartedAt > TimeSpan.FromDays(14)).ToList()) _operations.Remove(entry.Id);
                while (_operations.Count >= 256)
                {
                    var oldest = _operations.Values.Where(op => op.Outcome != "running").OrderBy(op => op.StartedAt).FirstOrDefault();
                    if (oldest is null) break;
                    _operations.Remove(oldest.Id);
                }
                if (_operations.Count < 256)
                {
                    operation = new SipOperation { Id = NewId(), Kind = route, RequestId = requestId, SessionId = SessionId, StartedAt = _clock.GetUtcNow(), UpdatedAt = _clock.GetUtcNow() };
                    _operations[operation.Id] = operation;
                }
            }
            if (operation is null)
            {
                context.Response.StatusCode = 409;
                context.Response.Headers["Retry-After"] = "1";
                await context.Response.WriteAsJsonAsync(new { RetryAfterSeconds = 1, Success = false, ErrorCode = "BUSY", Message = "Another operation is still running.", Retryable = true, RequestId = requestId, SessionId, ObservedAt = _clock.GetUtcNow() });
                return;
            }
            service.LogOperation(operation);
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        JsonObject payload;
        try
        {
            try { await next(context); }
            catch (OperationCanceledException)
            {
                context.Response.StatusCode = 409;
                buffer.SetLength(0);
                await context.Response.WriteAsJsonAsync(new { Success = false, ErrorCode = "CANCELED" });
            }
            catch (Exception)
            {
                context.Response.StatusCode = 500;
                buffer.SetLength(0);
                await context.Response.WriteAsJsonAsync(new { Success = false, ErrorCode = "INTERNAL_ERROR" });
            }
            buffer.Position = 0;
            if (buffer.Length > 1 << 20) throw new InvalidOperationException("SIP response exceeds limit");
            payload = buffer.Length == 0 ? [] : JsonNode.Parse(buffer)?.AsObject() ?? [];
        }
        finally { context.Response.Body = original; }

        var code = SafeCode(payload["errorCode"]?.GetValue<string>()) ?? CodeForStatus(context.Response.StatusCode);
        if (operation is not null)
        {
            var success = context.Response.StatusCode < 400 && payload["success"]?.GetValue<bool>() != false;
            if (!success && code.Length == 0) code = "INTERNAL_ERROR";
            operation = operation with
            {
                Outcome = success ? "success" : code == "CANCELED" ? "canceled" : context.Response.StatusCode >= 500 ? "unknown" : "failed",
                Phase = "completed", CompletedAt = _clock.GetUtcNow(), UpdatedAt = _clock.GetUtcNow(),
                ErrorCode = code, Message = SafeMessage(code), Retryable = false
            };
            lock (_lock)
            {
                _operations[operation.Id] = operation;
                if (success) _confirmed[route] = operation;
            }
            service.LogOperation(operation);
            if (operation.Outcome == "success") { payload["confirmedAt"] = JsonValue.Create(operation.CompletedAt); payload["confirmedOperationId"] = operation.Id; }
            payload["operationId"] = operation.Id;
            payload["operation"] = JsonSerializer.SerializeToNode(operation, SipHttpApi.JsonOptions);
        }
        payload["requestId"] = requestId;
        payload["sessionId"] = SessionId;
        payload["observedAt"] = JsonValue.Create(_clock.GetUtcNow());
        if (code.Length > 0)
        {
            payload["errorCode"] = code;
            payload["message"] = SafeMessage(code);
            payload["retryable"] = code is "BUSY" or "UNAVAILABLE";
            if (code == "BUSY") { payload["retryAfterSeconds"] = 1; context.Response.Headers["Retry-After"] = "1"; }
        }
        if (route is "/api/v1/status" or "/api/v1/profile/current" or "/api/v1/browser-support")
        {
            var fingerprint = Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
            var kind = route == "/api/v1/browser-support" ? route : "/api/v1/profile";
            lock (_lock)
            {
                if (_snapshots.GetValueOrDefault(route) != fingerprint) { _snapshots[route] = fingerprint; _changes[route] = _clock.GetUtcNow(); }
                payload["updatedAt"] = JsonValue.Create(_changes[route]);
                if (_confirmed.TryGetValue(kind, out var confirmation)) { payload["confirmedAt"] = JsonValue.Create(confirmation.CompletedAt); payload["confirmedOperationId"] = confirmation.Id; }
            }
        }
        context.Response.ContentLength = null;
        await context.Response.WriteAsJsonAsync(payload, SipHttpApi.JsonOptions, CancellationToken.None);
    }

    private static string SafeBuildVersion()
    {
        var value = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(SipObservability).Assembly)?.InformationalVersion ?? "unknown";
        return value.Length <= 100 && Regex.IsMatch(value, "^[A-Za-z0-9+._-]+$", RegexOptions.CultureInvariant) ? value : "unknown";
    }

    private static string? SafeCode(string? value) => value is "INVALID_REQUEST" or "FORBIDDEN" or "NOT_FOUND" or "OPERATION_NOT_FOUND" or "BUSY" or "INTERNAL_ERROR" or "UNAVAILABLE" or "CANCELED" ? value : null;
    private static string CodeForStatus(int status) => status switch { 400 or 405 or 413 or 415 => "INVALID_REQUEST", 403 => "FORBIDDEN", 404 => "NOT_FOUND", 409 or 429 => "BUSY", 503 => "UNAVAILABLE", >= 500 => "INTERNAL_ERROR", _ => "" };
    private static string SafeMessage(string code) => code switch { "" => "Operation completed.", "INVALID_REQUEST" => "The request is invalid.", "FORBIDDEN" => "Local access is required.", "NOT_FOUND" or "OPERATION_NOT_FOUND" => "The requested resource was not found.", "BUSY" => "Another operation is still running.", "UNAVAILABLE" => "A required integration is unavailable.", "CANCELED" => "The operation was canceled.", _ => "The operation could not be confirmed. Inspect application state before retrying." };
}

public sealed record SipOperation
{
    public string Id { get; init; } = "";
    public string Kind { get; init; } = "";
    public string RequestId { get; init; } = "";
    public string SessionId { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public string Phase { get; init; } = "executing";
    public string Outcome { get; init; } = "running";
    public string ErrorCode { get; init; } = "";
    public string Message { get; init; } = "";
    public bool Retryable { get; init; }
}
