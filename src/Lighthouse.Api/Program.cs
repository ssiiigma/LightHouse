using System.Text;
using FluentValidation;
using Lighthouse.Api.BackgroundJobs;
using Lighthouse.Api.Data;
using Lighthouse.Api.Infrastructure;
using Lighthouse.Api.Options;
using Lighthouse.Api.Services.Auth;
using Lighthouse.Api.Services.Behavior;
using Lighthouse.Api.Services.Calendar;
using Lighthouse.Api.Services.Categories;
using Lighthouse.Api.Services.Habits;
using Lighthouse.Api.Services.Recommendations;
using Lighthouse.Api.Services.Tasks;
using Lighthouse.Core.AI;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Журнал: лише службові події запитів (метод, шлях, статус, час). Тіла запитів, заголовки Authorization,
// паролі й токени не логуються; EnableSensitiveDataLogging для EF Core не вмикається.
builder.Host.UseSerilog((context, services, logger) => logger
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

// ---------- Options ----------
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
if (jwt.Secret.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:Secret must be at least 32 characters. Set it via user-secrets or the Jwt__Secret environment variable.");
}

// ---------- Database ----------
builder.Services.AddDbContext<LighthouseDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// ---------- Data Protection (шифрування токена Google Calendar) ----------
// У Docker каталог ключів має бути на томі (DataProtection__KeysPath), інакше після перезапуску
// збережені токени стануть нечитабельними.
var dataProtection = builder.Services.AddDataProtection().SetApplicationName("Lighthouse");
if (builder.Configuration["DataProtection:KeysPath"] is { Length: > 0 } keysPath)
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

// ---------- Authentication / Authorization ----------
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddLighthouseRateLimiting(builder.Configuration);

// ---------- Web API ----------
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<AppExceptionHandler>();
builder.Services.AddValidatorsFromAssemblyContaining<Program>();

// ---------- Application services ----------
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<ITaskService, TaskService>();
builder.Services.AddScoped<IUserTimeService, UserTimeService>();
builder.Services.AddScoped<IBehaviorRecorder, BehaviorRecorder>();
builder.Services.AddScoped<ICategoryService, CategoryService>();
builder.Services.AddScoped<IHabitService, HabitService>();
builder.Services.AddSingleton<ICalendarTokenProtector, CalendarTokenProtector>();

builder.Services.AddSingleton<IPrioritizer, Prioritizer>();
builder.Services.AddScoped<BanditStore>();
builder.Services.AddScoped<UserHistoryBuilder>();
builder.Services.AddScoped<ModelTrainer>();
builder.Services.AddScoped<IRecommendationService, RecommendationService>();

if (builder.Configuration.GetValue("Recommendations:ModelTrainingEnabled", true))
{
    builder.Services.AddHostedService<ModelTrainingWorker>();
}

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<LighthouseDbContext>().Database.Migrate();
}

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.UseAuthentication();
app.UseRateLimiter(); // після автентифікації: ліміт на користувача знає sub; перед авторизацією: 401-запити теж рахуються
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();

app.Run();

public partial class Program { }