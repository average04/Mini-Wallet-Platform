using System.Globalization;
using System.Numerics;

namespace MiniWallet.Wallet.Domain;

public sealed record WinPayout
{
    public Money Total { get; }
    public string Multiplier { get; }
    public string BonusAmount { get; }

    private WinPayout(Money total, string multiplier, string bonusAmount)
        => (Total, Multiplier, BonusAmount) = (total, multiplier, bonusAmount);

    public static WinPayout Calculate(Money stake, string? multiplier = null, string? bonusAmount = null)
    {
        var scale = stake.Currency == "BTC" ? 8 : 2;
        var factor = scale == 8 ? 100000000m : 100m;
        var rate = Parse(multiplier ?? "2", 8, allowZero: false, "invalid_multiplier");
        var bonus = Parse(bonusAmount ?? "0", scale, allowZero: true, "invalid_bonus_amount");
        var bonusUnits = bonus.Units * BigInteger.Pow(10, scale - bonus.Scale);
        var maximumUnits = new BigInteger(Money.Maximum * factor);
        if (bonusUnits > maximumUnits) throw new WalletRuleException("invalid_bonus_amount");

        // Integer arithmetic detects sub-unit payouts even when decimal multiplication
        // would silently round a large, high-precision product. Scaling stored money
        // by its currency factor is exact and remains within decimal's range.
        var stakeUnits = new BigInteger(stake.Amount * factor);
        var payoutUnits = BigInteger.DivRem(stakeUnits * rate.Units,
            BigInteger.Pow(10, rate.Scale), out var remainder);
        if (!remainder.IsZero) throw new WalletRuleException("invalid_payout_precision");
        payoutUnits += bonusUnits;
        if (payoutUnits <= 0 || payoutUnits > maximumUnits)
            throw new WalletRuleException("invalid_payout_amount");

        var digits = payoutUnits.ToString(CultureInfo.InvariantCulture).PadLeft(scale + 1, '0');
        var total = Money.Parse(stake.Currency, digits.Insert(digits.Length - scale, "."));
        return new WinPayout(total, rate.Canonical, bonus.Canonical);
    }

    private static (BigInteger Units, int Scale, string Canonical) Parse(
        string input, int maximumScale, bool allowZero, string errorCode)
    {
        if (input.Length == 0 || input.Length > 64) throw new WalletRuleException(errorCode);
        var parts = input.Split('.');
        if (parts.Length > 2 || parts.Any(p => p.Length == 0 || p.Any(c => !char.IsAsciiDigit(c)))
            || (parts.Length == 2 && parts[1].Length > maximumScale))
            throw new WalletRuleException(errorCode);
        var units = BigInteger.Parse(string.Concat(parts), CultureInfo.InvariantCulture);
        if (!allowZero && units.IsZero) throw new WalletRuleException(errorCode);
        var whole = parts[0].TrimStart('0');
        if (whole.Length == 0) whole = "0";
        var fraction = parts.Length == 2 ? parts[1].TrimEnd('0') : "";
        return (units, parts.Length == 2 ? parts[1].Length : 0,
            fraction.Length == 0 ? whole : $"{whole}.{fraction}");
    }
}
