using MiniWallet.Wallet.Domain;
namespace MiniWallet.Wallet.Tests.Unit;

public sealed class MoneyTests
{
    [Theory]
    [InlineData("USD", "1.25", "1.25")]
    [InlineData("EUR", "10", "10")]
    [InlineData("BTC", "0.00000001", "0.00000001")]
    [InlineData("USD", "1.0", "1")]
    [InlineData("USD", "1.00", "1")]
    public void Parses_exact_positive_currency_amounts(string currency, string input, string expected)
        => Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), Money.Parse(currency, input).Amount);

    [Theory]
    [InlineData("USD", "0")]
    [InlineData("USD", "-1")]
    [InlineData("USD", "1.001")]
    [InlineData("EUR", "1.000")]
    [InlineData("BTC", "0.000000001")]
    [InlineData("USD", "1e2")]
    [InlineData("USD", "1,000")]
    [InlineData("USD", " 10")]
    [InlineData("usd", "1")]
    [InlineData("JPY", "1")]
    [InlineData("USD", "100000000000000000000")]
    public void Rejects_unsupported_precision_format_or_range(string currency, string input)
        => Assert.Throws<WalletRuleException>(() => Money.Parse(currency, input));

    [Fact]
    public void Debit_cannot_overdraw_or_mutate_on_rejection()
    {
        var wallet = new BalanceAccount(Guid.NewGuid(), "USD");
        wallet.Credit(Money.Parse("USD", "100"));
        wallet.Debit(Money.Parse("USD", "80"));
        var error = Assert.Throws<WalletRuleException>(() => wallet.Debit(Money.Parse("USD", "80")));
        Assert.Equal("insufficient_funds", error.Code);
        Assert.Equal(20m, wallet.Balance);
    }

    [Fact]
    public void Credit_cannot_exceed_storage_limit()
    {
        var wallet = new BalanceAccount(Guid.NewGuid(), "BTC");
        wallet.Credit(Money.Parse("BTC", "99999999999999999999.99999999"));
        var error = Assert.Throws<WalletRuleException>(() => wallet.Credit(Money.Parse("BTC", "0.00000001")));
        Assert.Equal("balance_limit", error.Code);
        Assert.Equal(99999999999999999999.99999999m, wallet.Balance);
    }

    [Fact]
    public void Cannot_apply_a_different_currency()
    {
        var wallet = new BalanceAccount(Guid.NewGuid(), "EUR");
        Assert.Throws<WalletRuleException>(() => wallet.Credit(Money.Parse("USD", "1")));
        Assert.Equal(0m, wallet.Balance);
    }
}

