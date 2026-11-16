using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;
namespace MiniWallet.Wallet.Api;

public sealed class TransactionExamples : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.HttpMethod != "POST" || context.ApiDescription.RelativePath != "api/v1/transactions"
            || operation.RequestBody?.Content?.TryGetValue("application/json", out var media) != true || media is null) return;
        media.Examples = new Dictionary<string, IOpenApiExample>
        {
            ["BET"] = Example("Player debits their own USD wallet", """{"type":"BET","currency":"USD","amount":"10"}"""),
            ["WIN"] = Example("Admin pays 2x the original BET; multiplier defaults to 2 and bonusAmount to 0", """{"type":"WIN","betId":"00000000-0000-0000-0000-000000000001","multiplier":"2","bonusAmount":"0"}"""),
            ["WIN with bonus"] = Example("Admin adds a bonus after multiplication; a 10-unit BET credits 25", """{"type":"WIN","betId":"00000000-0000-0000-0000-000000000001","multiplier":"2","bonusAmount":"5"}"""),
            ["BONUS"] = Example("Admin funds a player; replace playerId from profile response", """{"type":"BONUS","playerId":"00000000-0000-0000-0000-000000000001","currency":"USD","amount":"100"}""")
        };
        if (operation.Responses?.TryGetValue("400", out var invalid) == true
            && invalid.Content?.TryGetValue("application/problem+json", out var problem) == true)
        {
            problem.Examples = new Dictionary<string, IOpenApiExample>
            {
                ["invalid_request"] = Problem("invalid_request", "Missing, conflicting or unknown fields; for example amount supplied for WIN"),
                ["invalid_amount"] = Problem("invalid_amount", "BET/BONUS amount is zero, malformed, too precise or out of range"),
                ["invalid_multiplier"] = Problem("invalid_multiplier", "WIN multiplier must be a positive decimal string with at most 8 fractional digits"),
                ["invalid_bonus_amount"] = Problem("invalid_bonus_amount", "WIN bonus must be nonnegative and obey the bet's currency precision and range"),
                ["invalid_payout_precision"] = Problem("invalid_payout_precision", "Calculated payout has fractional currency units, for example a USD 0.01 stake at 1.5x"),
                ["invalid_payout_amount"] = Problem("invalid_payout_amount", "Calculated payout exceeds the supported money range"),
                ["invalid_idempotency_key"] = Problem("invalid_idempotency_key", "Idempotency key is empty, over 128 characters or contains unsupported characters")
            };
        }
    }
    private static OpenApiExample Example(string summary, string json) => new() { Summary = summary, Value = JsonNode.Parse(json) };
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
