using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using MiniWallet.Wallet.Application.Features.Transactions;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace MiniWallet.Wallet.Api;

public sealed class TransactionSchema : ISchemaFilter
{
    public void Apply(IOpenApiSchema candidate, SchemaFilterContext context)
    {
        if (context.Type != typeof(TransactionRequest) || candidate is not OpenApiSchema schema) return;
        schema.Required = new HashSet<string> { "type" };
        schema.Description = "Required and permitted fields depend on type; see the transaction rules above. Unknown properties are rejected.";
        Configure("type", "Required: BET, WIN or BONUS. WIN and BONUS require an admin.", property =>
        {
            property.Type = JsonSchemaType.String;
            property.Enum = [JsonValue.Create("BET"), JsonValue.Create("WIN"), JsonValue.Create("BONUS")];
        });
        Configure("amount", "BET/BONUS only; required positive decimal string. USD/EUR: at most 2 fractional digits; BTC: 8. WIN calculates its payout from the original bet and rejects this field.", DecimalString);
        Configure("currency", "BET/BONUS only; required. WIN derives currency from the original bet and rejects this field.", property =>
            property.Enum = [JsonValue.Create("USD"), JsonValue.Create("EUR"), JsonValue.Create("BTC")]);
        Configure("playerId", "BONUS only; required nonempty player UUID. BET uses the authenticated player; WIN uses the original bet owner. Omit for BET/WIN.");
        Configure("betId", "WIN only; required nonempty UUID of an earlier successful BET's transactionId. A bet can be settled once. Omit for BET/BONUS.");
        Configure("multiplier", "WIN only; optional, default 2 (2x). Positive decimal string with at most 8 fractional digits. Omitted/null uses the default. Total payout includes the stake. Omit for BET/BONUS.", DecimalString);
        Configure("bonusAmount", "WIN only; optional, default 0. Nonnegative decimal string, added AFTER multiplying the stake. Uses the bet's currency and precision (USD/EUR: 2, BTC: 8). Omitted/null uses the default. Omit for BET/BONUS.", DecimalString);

        void Configure(string name, string description, Action<OpenApiSchema>? configure = null)
        {
            if (schema.Properties?[name] is not OpenApiSchema property) return;
            property.Description = description;
            configure?.Invoke(property);
        }
    }

    private static void DecimalString(OpenApiSchema property)
    {
        property.MinLength = 1;
        property.MaxLength = 64;
        property.Pattern = "^[0-9]+(?:\\.[0-9]{1,8})?$";
    }
}
