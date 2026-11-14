using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MiniWallet.Wallet.Application.Features.Provisioning;
using MiniWallet.Wallet.Infrastructure;
using MiniWallet.Wallet.Infrastructure.Messaging;
using MiniWallet.Wallet.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddWalletInfrastructure();
builder.Services.AddScoped<ProvisionPlayer>();
builder.Services.AddSingleton(p => new PlayerEventConsumer(RabbitTopology.ConnectionString(builder.Configuration), p.GetRequiredService<ILogger<PlayerEventConsumer>>()));
builder.Services.AddHostedService<ProvisioningWorker>();
await builder.Build().RunAsync();
