using AiStyleApp.Data;
using AiStyleApp.Worker;
using AiStyleApp.Worker.Handlers;
using AiStyleApp.Worker.Services;
using Microsoft.EntityFrameworkCore;

var builder = Host.CreateApplicationBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Replicate HTTP client
builder.Services.AddHttpClient<IReplicateWorkerClient, ReplicateWorkerClient>();

builder.Services.AddHostedService<JobWorker>();
builder.Services.AddScoped<IMessageHandler, StyleJobHandler>();

var host = builder.Build();
host.Run();

