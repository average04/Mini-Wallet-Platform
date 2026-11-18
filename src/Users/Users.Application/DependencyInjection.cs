using Microsoft.Extensions.DependencyInjection;
using MiniWallet.Users.Application.Features.Profiles;

namespace MiniWallet.Users.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddUsersApplication(this IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<AssemblyMarker>());
        services.AddScoped<ProfileRequestValidator>();
        return services;
    }
}
