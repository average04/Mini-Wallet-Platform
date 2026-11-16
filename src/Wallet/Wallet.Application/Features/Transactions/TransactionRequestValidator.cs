using FluentValidation;
namespace MiniWallet.Wallet.Application.Features.Transactions;

public sealed class TransactionRequestValidator : AbstractValidator<TransactionRequest>
{
    public TransactionRequestValidator()
    {
        RuleFor(x => x.Type).Must(x => x is "BET" or "WIN" or "BONUS");
        When(x => x.Type is "BET" or "BONUS", () =>
        {
            RuleFor(x => x.Amount).NotEmpty().MaximumLength(64);
            RuleFor(x => x.Currency).Must(x => x is "USD" or "EUR" or "BTC");
            RuleFor(x => x.BetId).Null();
            RuleFor(x => x.Multiplier).Null();
            RuleFor(x => x.BonusAmount).Null();
        });
        When(x => x.Type == "BET", () => RuleFor(x => x.PlayerId).Null());
        When(x => x.Type == "BONUS", () => RuleFor(x => x.PlayerId).Must(x => x.HasValue && x != Guid.Empty));
        When(x => x.Type == "WIN", () =>
        {
            RuleFor(x => x.BetId).Must(x => x.HasValue && x != Guid.Empty);
            RuleFor(x => x.Currency).Null();
            RuleFor(x => x.PlayerId).Null();
            RuleFor(x => x.Amount).Null();
            RuleFor(x => x.Multiplier).MaximumLength(64);
            RuleFor(x => x.BonusAmount).MaximumLength(64);
        });
    }
}
