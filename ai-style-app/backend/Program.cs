using AiStyleApp.Api.Infrastructure;
using AiStyleApp.Api.Services;
using AiStyleApp.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste only the JWT access token returned by POST /api/auth/token."
    });

    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("Bearer", document, null)] = new List<string>()
    });

    options.OperationFilter<SwaggerOperationDefaultsFilter>();
});

// CORS — allow frontend dev origin in development only
builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendDev", policy =>
    {
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is not configured.");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var authorization = context.Request.Headers.Authorization.ToString();

                if (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authorization["Bearer ".Length..].Trim();

                    // Swagger bearer auth expects token-only input; strip an accidental extra prefix.
                    if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                    {
                        token = token["Bearer ".Length..].Trim();
                    }

                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnAuthenticationFailed = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");

                logger.LogWarning(context.Exception,
                    "JWT authentication failed. Issuer={Issuer}; Audience={Audience}",
                    builder.Configuration["Jwt:Issuer"],
                    builder.Configuration["Jwt:Audience"]);

                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                var logger = context.HttpContext.RequestServices
                    .GetRequiredService<ILoggerFactory>()
                    .CreateLogger("JwtAuth");

                if (!string.IsNullOrWhiteSpace(context.ErrorDescription))
                {
                    logger.LogWarning("JWT challenge error: {Error}. {Description}", context.Error, context.ErrorDescription);
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

// Database
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Options
builder.Services.Configure<QueueOptions>(builder.Configuration.GetSection(QueueOptions.Section));
builder.Services.Configure<ReplicateOptions>(builder.Configuration.GetSection(ReplicateOptions.Section));
builder.Services.Configure<BlobStorageOptions>(
    builder.Configuration.GetSection(BlobStorageOptions.Section));

// Application services
builder.Services.AddScoped<IStyleService, StyleService>();
builder.Services.AddScoped<IJobService, JobService>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();
builder.Services.AddScoped<IAnalyticsService, AnalyticsService>();
builder.Services.AddScoped<IReplicateWebhookProcessor, ReplicateWebhookProcessor>();
builder.Services.AddScoped<IQueuePublisher, QueuePublisher>();
builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IGeneratedImageArchiver, GeneratedImageArchiver>();
builder.Services.AddHttpClient<GeneratedImageArchiver>();
builder.Services.AddSingleton<IReplicateSignatureVerifier, ReplicateSignatureVerifier>();
builder.Services.AddSingleton<IMetricsLogger, MetricsLogger>();

var app = builder.Build();

// Auto-migrate on startup in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.UseCors("FrontendDev");
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
