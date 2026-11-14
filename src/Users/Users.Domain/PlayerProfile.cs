namespace MiniWallet.Users.Domain;

public sealed class ProfileRuleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}
public sealed class PlayerProfile
{
    public Guid Id { get; private set; }
    public string DisplayName { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    private PlayerProfile() { }
    public PlayerProfile(Guid id, string displayName, DateTimeOffset now)
        => (Id, DisplayName, CreatedAt, UpdatedAt) = (id, Normalize(displayName), now, now);
    public void Rename(string displayName, DateTimeOffset now)
        => (DisplayName, UpdatedAt) = (Normalize(displayName), now);
    private static string Normalize(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) || displayName.Contains('\0') || displayName.Trim().Length > 100)
            throw new ProfileRuleException("invalid_display_name");
        return displayName.Trim();
    }
}
