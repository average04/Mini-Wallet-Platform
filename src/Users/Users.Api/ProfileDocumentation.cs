using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using MiniWallet.Users.Application.Features.Profiles;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MiniWallet.Users.Api;

public sealed class ProfileDocumentation : ISchemaFilter, IOperationFilter
{
    public void Apply(IOpenApiSchema candidate, SchemaFilterContext context)
    {
        if (context.Type != typeof(ProfileRequest) || candidate is not OpenApiSchema schema) return;
        schema.Required = new HashSet<string> { "displayName" };
        if (schema.Properties?["displayName"] is not OpenApiSchema name) return;
        name.Type = JsonSchemaType.String;
        name.MinLength = 1;
        // The limit applies after trimming; a raw maxLength would reject valid input.
        name.Description = "Required. Trimmed before saving; must contain 1–100 characters after trimming. Null, blank/whitespace-only names and embedded NUL (\\u0000) are rejected.";
    }

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.HttpMethod is not ("POST" or "PUT")
            || context.ApiDescription.RelativePath?.StartsWith("api/v1/profiles", StringComparison.Ordinal) != true) return;
        operation.Description = (operation.Description is null ? "" : operation.Description + "\n\n")
            + "**Validation:** Send only `displayName`. It is required, trimmed before saving, and must contain 1–100 characters after trimming. Null, blank/whitespace-only names and embedded NUL (`\\u0000`) return **400** `invalid_display_name`. Unknown fields (including IDs, roles and email) or malformed JSON return **400** `invalid_request`. See the response examples for the Problem Details format.";
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var body) == true)
            body.Example = JsonNode.Parse("""{"displayName":"Demo Player"}""");
        if (operation.Responses?.TryGetValue("400", out var invalid) == true
            && invalid.Content?.TryGetValue("application/problem+json", out var problem) == true)
        {
            problem.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["invalid_display_name"] = Problem("invalid_display_name", "Name is missing, blank, longer than 100 trimmed characters or contains NUL"),
                ["invalid_request"] = Problem("invalid_request", "Malformed request or fields other than displayName")
            };
        }
    }

    private static OpenApiExample Problem(string code, string summary) => new()
    {
        Summary = summary,
        Value = new JsonObject
        {
            ["type"] = "https://tools.ietf.org/html/rfc9110#section-15.5.1",
            ["title"] = code.Replace('_', ' '), ["status"] = 400,
            ["code"] = code, ["traceId"] = "example-trace-id"
        }
    };
}
