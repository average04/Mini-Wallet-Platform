using System.Globalization;
using MiniWallet.Wallet.Domain;

namespace MiniWallet.Wallet.Tests.Unit;

public sealed class WinPayoutTests
{
    [Theory]
    [InlineData("USD", "10", null, null, "20")]
    [InlineData("USD", "10", "2", "5", "25")]
    [InlineData("EUR", "10", "1.5", "0.25", "15.25")]
    [InlineData("USD", "10", "0.5", "0", "5")]
    [InlineData("BTC", "0.00000002", "1.5", "0.00000001", "0.00000004")]
    [InlineData("BTC", "1", "0.00000001", "0", "0.00000001")]
    [InlineData("USD", "99999999999999999999.99", "1", "0", "99999999999999999999.99")]
    [InlineData("BTC", "99999999999999999999.99999999", "1", "0", "99999999999999999999.99999999")]
    public void Calculates_exact_total_payout(string currency, string stake, string? multiplier, string? bonus, string expected)
    {
        var payout = WinPayout.Calculate(Money.Parse(currency, stake), multiplier, bonus);
        Assert.Equal(currency, payout.Total.Currency);
        Assert.Equal(decimal.Parse(expected, CultureInfo.InvariantCulture), payout.Total.Amount);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("+2")]
    [InlineData("1e2")]
    [InlineData(" 2")]
    [InlineData("2 ")]
    [InlineData("2,0")]
    [InlineData("")]
    [InlineData(".5")]
    [InlineData("2.")]
    [InlineData("1.000000001")]
    [InlineData("NaN")]
    public void Rejects_invalid_multiplier(string multiplier)
        => Assert.Equal("invalid_multiplier", Assert.Throws<WalletRuleException>(() =>
            WinPayout.Calculate(Money.Parse("USD", "10"), multiplier)).Code);

    [Theory]
    [InlineData("USD", "-1")]
    [InlineData("USD", "0.001")]
    [InlineData("EUR", "1.000")]
    [InlineData("BTC", "0.000000001")]
    [InlineData("USD", "")]
    [InlineData("USD", "1e2")]
    [InlineData("USD", " 1")]
    [InlineData("USD", "100000000000000000000")]
    public void Rejects_invalid_bonus(string currency, string bonus)
        => Assert.Equal("invalid_bonus_amount", Assert.Throws<WalletRuleException>(() =>
            WinPayout.Calculate(Money.Parse(currency, "10"), bonusAmount: bonus)).Code);

    [Theory]
    [InlineData("USD", "0.01", "1.5")]
    [InlineData("BTC", "0.00000001", "1.5")]
    // A decimal product could round this down to the currency's precision.
    [InlineData("BTC", "99999999999999999999.99999999", "0.99999999")]
    public void Rejects_fractional_currency_units_instead_of_rounding(string currency, string stake, string multiplier)
        => Assert.Equal("invalid_payout_precision", Assert.Throws<WalletRuleException>(() =>
            WinPayout.Calculate(Money.Parse(currency, stake), multiplier)).Code);

    [Theory]
    [InlineData("USD", "99999999999999999999.99", "2", "0")]
    [InlineData("BTC", "99999999999999999999.99999999", "1", "0.00000001")]
    [InlineData("USD", "10", "9999999999999999999999999999999999999999999999999999999999999999", "0")]
    public void Rejects_out_of_range_total_without_overflow(string currency, string stake, string multiplier, string bonus)
        => Assert.Equal("invalid_payout_amount", Assert.Throws<WalletRuleException>(() =>
            WinPayout.Calculate(Money.Parse(currency, stake), multiplier, bonus)).Code);

    [Fact]
    public void Normalizes_terms_for_equivalent_retries()
    {
        var stake = Money.Parse("USD", "10");
        Assert.Equal(WinPayout.Calculate(stake), WinPayout.Calculate(stake, "002.00000000", "00.00"));
        var payout = WinPayout.Calculate(stake, "01.5000", "005.50");
        Assert.Equal("1.5", payout.Multiplier);
        Assert.Equal("5.5", payout.BonusAmount);
    }

    [Fact]
    public void Rejects_unbounded_input_lengths()
    {
        var stake = Money.Parse("USD", "10");
        Assert.Equal("invalid_multiplier", Assert.Throws<WalletRuleException>(() =>
            WinPayout.Calculate(stake, new string('1', 65))).Code);
        Assert.Equal("invalid_bonus_amount", Assert.Throws<WalletRuleException>(() =>
            WinPayout.Calculate(stake, bonusAmount: new string('0', 65))).Code);
    }
}
