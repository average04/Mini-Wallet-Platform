using System.Globalization;

namespace MiniWallet.Wallet.Domain;

public sealed class WalletRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record Money
{
    public const decimal Maximum = 99999999999999999999.99999999m;
    public string Currency { get; }
    public decimal Amount { get; }

    private Money(string currency, decimal amount) => (Currency, Amount) = (currency, amount);

    public static Money Parse(string currency, string input)
    {
        var scale = currency switch { "USD" or "EUR" => 2, "BTC" => 8, _ => -1 };
        if (scale < 0 || string.IsNullOrEmpty(input) || input.Length > 64)
            throw new WalletRuleException("invalid_amount");
        var parts = input.Split('.');
        if (parts.Length > 2 || parts.Any(p => p.Length == 0 || p.Any(c => !char.IsAsciiDigit(c)))
            || (parts.Length == 2 && parts[1].Length > scale)
            || !decimal.TryParse(input, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount)
            || amount <= 0 || amount > Maximum)
            throw new WalletRuleException("invalid_amount");
        return new Money(currency, amount);
    }
}

public sealed class BalanceAccount
{
    public Guid PlayerId { get; private set; }
    public string Currency { get; private set; } = "";
    public decimal Balance { get; private set; }

    private BalanceAccount() { }

    public BalanceAccount(Guid playerId, string currency)
    {
        if (playerId == Guid.Empty || currency is not ("USD" or "EUR" or "BTC"))
            throw new WalletRuleException("invalid_wallet");
        (PlayerId, Currency) = (playerId, currency);
    }

    public void Credit(Money amount)
    {
        RequireCurrency(amount);
        if (amount.Amount > Money.Maximum - Balance) throw new WalletRuleException("balance_limit");
        Balance += amount.Amount;
    }

    public void Debit(Money amount)
    {
        RequireCurrency(amount);
        if (amount.Amount > Balance) throw new WalletRuleException("insufficient_funds");
        Balance -= amount.Amount;
    }

    private void RequireCurrency(Money amount)
    {
        if (amount.Currency != Currency) throw new WalletRuleException("currency_mismatch");
    }
}

