using MediatR;
using MiniWallet.Users.Api.Auth;
using MiniWallet.Users.Application.Features.Profiles;
namespace MiniWallet.Users.Api.Endpoints;

public static class ProfilesEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/v1/profiles").RequireAuthorization().WithTags("Profiles");
        group.MapPost("", async (ProfileRequest request, HttpContext context, ISender sender, CancellationToken ct) =>
        {
            var result = await sender.Send(new CreateProfileCommand(ActorMapper.Map(context.User), request), ct);
            context.Response.Headers.Location = "/api/v1/profiles/me";
            return result.Created ? Results.Created("/api/v1/profiles/me", result.Profile) : Results.Ok(result.Profile);
        }).WithSummary("Create your verified player's profile")
            .WithDescription("Create-if-absent. Repeating this call returns the existing profile without changing it or emitting another event. No Idempotency-Key is needed. Wallets are provisioned automatically in the background.")
            .Produces<ProfileView>(201).Produces<ProfileView>(200).ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(503);
        group.MapGet("/me", async (HttpContext context, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetProfileQuery(ActorMapper.Map(context.User)), ct)))
            .WithSummary("Read your profile").Produces<ProfileView>().ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);
        group.MapPut("/me", async (ProfileRequest request, HttpContext context, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new UpdateProfileCommand(ActorMapper.Map(context.User), request), ct)))
            .WithSummary("Replace your editable profile fields").Produces<ProfileView>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(404).ProducesProblem(503);
        group.MapGet("/{playerId:guid}", async (Guid playerId, HttpContext context, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new GetProfileQuery(ActorMapper.Map(context.User), playerId), ct)))
            .WithSummary("Read a player's profile (admin)").Produces<ProfileView>().ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
        group.MapPut("/{playerId:guid}", async (Guid playerId, ProfileRequest request, HttpContext context, ISender sender, CancellationToken ct) =>
            Results.Ok(await sender.Send(new UpdateProfileCommand(ActorMapper.Map(context.User), request, playerId), ct)))
            .WithSummary("Replace a player's editable profile fields (admin)").Produces<ProfileView>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(404).ProducesProblem(503);
    }
}
