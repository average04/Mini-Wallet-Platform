namespace MiniWallet.Users.Application;
public enum FailureKind { Invalid, Forbidden, NotFound, Conflict }
public sealed class RequestFailure(string code, FailureKind kind) : Exception(code)
{
    public string Code { get; } = code;
    public FailureKind Kind { get; } = kind;
}
