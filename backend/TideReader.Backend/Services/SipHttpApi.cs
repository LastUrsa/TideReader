using System.Text.Json;
using System.Text.Json.Serialization;

namespace TideReader.Backend.Services;

public static class SipHttpApi
{
    internal const long MaxRequestBodyBytes = 4096;

    internal static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static void Configure(WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'; base-uri 'none'";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Frame-Options"] = "DENY";

            if (!IsLocalHost(context.Request.Host.Host))
            {
                await WriteErrorAsync(context, StatusCodes.Status403Forbidden, "Forbidden");
                return;
            }

            await next(context);
        });

        app.MapGet("/api/v1/app", (SipService sip) => Results.Json(sip.App(), JsonOptions));
        app.MapGet("/api/v1/health", (SipService sip) => Results.Json(sip.Health(), JsonOptions));
        app.MapGet("/api/v1/capabilities", (SipService sip) => Results.Json(sip.Capabilities(), JsonOptions));
        app.MapGet("/api/v1/status", (SipService sip) => Results.Json(sip.Status(), JsonOptions));
        app.MapGet("/api/v1/browser-support", (SipService sip) => Results.Json(sip.BrowserSupport(), JsonOptions));
        app.MapPost("/api/v1/browser-support", SetBrowserSupportAsync);
        app.MapGet("/api/v1/profiles", (SipService sip) => Results.Json(sip.Profiles(), JsonOptions));
        app.MapGet("/api/v1/profile/current", (SipService sip) => Results.Json(sip.CurrentProfile(), JsonOptions));
        app.MapPost("/api/v1/profile", ActivateProfileAsync);
    }

    internal static async Task<IResult> SetBrowserSupportAsync(HttpContext context, SipService sip, CancellationToken cancellationToken)
    {
        if (IsOversizedRequest(context.Request))
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = "InvalidRequest" },
                JsonOptions,
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!IsJsonRequest(context.Request))
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = "InvalidRequest" },
                JsonOptions,
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        SipBrowserSupportRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SipBrowserSupportRequest>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = "InvalidRequest" },
                JsonOptions,
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var response = await sip.SetBrowserSupportAsync(request?.Enabled, cancellationToken);
            return Results.Json(response, JsonOptions);
        }
        catch (SipException ex)
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = ex.Message },
                JsonOptions,
                statusCode: ex.StatusCode);
        }
    }

    internal static async Task<IResult> ActivateProfileAsync(HttpContext context, SipService sip, CancellationToken cancellationToken)
    {
        if (IsOversizedRequest(context.Request))
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = "InvalidRequest" },
                JsonOptions,
                statusCode: StatusCodes.Status413PayloadTooLarge);
        }

        if (!IsJsonRequest(context.Request))
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = "InvalidRequest" },
                JsonOptions,
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        SipActivateProfileRequest? request;
        try
        {
            request = await context.Request.ReadFromJsonAsync<SipActivateProfileRequest>(JsonOptions, cancellationToken);
        }
        catch (JsonException)
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = "InvalidRequest" },
                JsonOptions,
                statusCode: StatusCodes.Status400BadRequest);
        }

        try
        {
            var response = await sip.ActivateProfileAsync(request?.Profile ?? "", cancellationToken);
            return Results.Json(response, JsonOptions);
        }
        catch (SipException ex)
        {
            return Results.Json(
                new SipErrorResponse { Success = false, Error = ex.Message },
                JsonOptions,
                statusCode: ex.StatusCode);
        }
    }

    internal static bool IsJsonRequest(HttpRequest request)
    {
        var contentType = request.ContentType;
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return false;
        }

        var mediaType = contentType.Split(';', 2)[0].Trim();
        return string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsOversizedRequest(HttpRequest request) =>
        request.ContentLength is > MaxRequestBodyBytes;

    internal static bool IsLocalHost(string host)
    {
        host = host.Trim().Trim('[', ']').ToLowerInvariant();
        return host is "127.0.0.1" or "localhost" or "::1";
    }

    internal static async Task WriteErrorAsync(HttpContext context, int statusCode, string error)
    {
        context.Response.StatusCode = statusCode;
        await context.Response.WriteAsJsonAsync(new SipErrorResponse { Success = false, Error = error }, JsonOptions);
    }
}
