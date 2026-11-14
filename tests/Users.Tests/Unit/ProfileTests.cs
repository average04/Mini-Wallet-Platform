using MiniWallet.Users.Domain;
namespace MiniWallet.Users.Tests.Unit;

public sealed class ProfileTests
{
    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Empty_name_is_rejected(string name)
        => Assert.Throws<ProfileRuleException>(() => new PlayerProfile(Guid.NewGuid(), name, DateTimeOffset.UtcNow));
    [Fact]
    public void Name_over_100_characters_is_rejected()
        => Assert.Throws<ProfileRuleException>(() => new PlayerProfile(Guid.NewGuid(), new string('x', 101), DateTimeOffset.UtcNow));

    [Fact]
    public void Null_character_is_rejected_when_creating_a_profile()
    {
        var error = Assert.Throws<ProfileRuleException>(() =>
            new PlayerProfile(Guid.NewGuid(), "Al\0ice", DateTimeOffset.UtcNow));
        Assert.Equal("invalid_display_name", error.Code);
    }

    [Fact]
    public void Null_character_is_rejected_without_changing_a_profile_on_rename()
    {
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var profile = new PlayerProfile(Guid.NewGuid(), "Alice", created);
        var error = Assert.Throws<ProfileRuleException>(() => profile.Rename("B\0ob", created.AddHours(1)));
        Assert.Equal("invalid_display_name", error.Code);
        Assert.Equal("Alice", profile.DisplayName);
        Assert.Equal(created, profile.UpdatedAt);
    }

    [Fact]
    public void Names_are_trimmed_and_update_preserves_identity_and_creation_time()
    {
        var created = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
        var updated = created.AddHours(1);
        var id = Guid.NewGuid();
        var profile = new PlayerProfile(id, " Alice ", created);
        Assert.Equal("Alice", profile.DisplayName);
        profile.Rename(" Bob ", updated);
        Assert.Equal("Bob", profile.DisplayName);
        Assert.Equal(id, profile.Id);
        Assert.Equal(created, profile.CreatedAt);
        Assert.Equal(updated, profile.UpdatedAt);
    }
}
