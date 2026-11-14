using MiniWallet.Users.Api.Auth;
using MiniWallet.Users.Api.Endpoints;
using MiniWallet.Users.Application.Features.Profiles;
using MiniWallet.Users.Infrastructure;
using MiniWallet.Users.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
namespace MiniWallet.Users.Api;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var migrate = args.Contains("--migrate");
        var builder = WebApplication.CreateBuilder(args.Where(a => a != "--migrate").ToArray());
        builder.Services.AddPlatformAuthentication(builder.Configuration, builder.Environment);
        builder.Services.AddAuthorization(options => options.FallbackPolicy =
            new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());
        builder.Services.AddApiDocumentation(builder.Configuration);
        builder.Services.AddUsersInfrastructure();
        builder.Services.AddScoped<ProfileRequestValidator>();
        builder.Services.AddScoped<CreateProfile>();
        builder.Services.AddScoped<GetProfile>();
        builder.Services.AddScoped<UpdateProfile>();
        builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);
        builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
        var app = builder.Build();
        if (migrate)
        {
            await using var scope = app.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<UsersDbContext>().Database.MigrateAsync();
            return;
        }
        app.UseApiErrors();
        app.UseApiDocumentation();
        app.UseCors("web");
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" })).AllowAnonymous().ExcludeFromDescription();
        app.MapProfileEndpoints();
        await app.RunAsync();
    }
}
