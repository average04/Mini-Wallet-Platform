using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using MiniWallet.Users.Application.Features.Outbox;
using MiniWallet.Users.Infrastructure;
using MiniWallet.Users.Infrastructure.Messaging;
using MiniWallet.Users.Worker;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddUsersInfrastructure();
builder.Services.AddScoped<DispatchOutbox>();
builder.Services.AddSingleton(_ => new RabbitPublisher(RabbitTopology.ConnectionString(builder.Configuration)));
builder.Services.AddSingleton<IEventPublisher>(p => p.GetRequiredService<RabbitPublisher>());
builder.Services.AddHostedService<OutboxWorker>();
await builder.Build().RunAsync();

