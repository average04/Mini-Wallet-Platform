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
    }
    private static OpenApiExample Example(string summary, string json) => new() { Summary = summary, Value = JsonNode.Parse(json) };
}
