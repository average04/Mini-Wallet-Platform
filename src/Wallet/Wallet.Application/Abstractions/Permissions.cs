namespace MiniWallet.Wallet.Application.Abstractions;

public static class Permissions
{
    public const string PlaceBet = "Wallets.PlaceBet";
    public const string ReadAny = "Wallets.ReadAny";
    public const string RecordWin = "Wallets.RecordWin";
    public const string GrantBonus = "Wallets.GrantBonus";

    public static void Require(Actor actor, string permission)
    {
        if (!actor.Can(permission)) throw new RequestFailure("forbidden", FailureKind.Forbidden);
    }
}

