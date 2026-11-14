using MiniWallet.Users.Application;
using MiniWallet.Users.Domain;
using MiniWallet.Users.Infrastructure;

namespace MiniWallet.Users.Api;

public static class ApiErrors
{
    public static IResult Problem(int status, string code, HttpContext context)
    {
        if (code == "player_not_provisioned") context.Response.Headers.RetryAfter = "2";
        return Results.Problem(statusCode: status, title: code.Replace('_', ' '),
            extensions: new Dictionary<string, object?> { ["code"] = code, ["traceId"] = context.TraceIdentifier });
    }

    public static void UseApiErrors(this WebApplication app) => app.Use(async (context, next) =>
    {
        try { await next(context); }
        catch (RequestFailure error)
        {
            var status = error.Kind switch { FailureKind.Invalid => 400, FailureKind.Forbidden => 403, FailureKind.NotFound => 404, _ => 409 };
            await Problem(status, error.Code, context).ExecuteAsync(context);
        }
        catch (BadHttpRequestException)
        {
            await Problem(400, "invalid_request", context).ExecuteAsync(context);
        }
        catch (ProfileRuleException error)
        {
            await Problem(400, error.Code, context).ExecuteAsync(context);
        }
        catch (Exception error) when (PersistenceErrors.IsFailure(error))
        {
            app.Logger.LogError(error, "Persistence failed for request {TraceId}", context.TraceIdentifier);
            context.Response.Headers.RetryAfter = "2";
            await Problem(503, "storage_unavailable", context).ExecuteAsync(context);
        }
        catch (Exception error) when (error is not OperationCanceledException || !context.RequestAborted.IsCancellationRequested)
        {
            app.Logger.LogError(error, "Request failed {TraceId}", context.TraceIdentifier);
            await Problem(500, "server_error", context).ExecuteAsync(context);
        }
    });
}
