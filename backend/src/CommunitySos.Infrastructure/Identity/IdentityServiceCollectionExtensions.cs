using CommunitySos.Application.Abstractions;
using CommunitySos.Application.Common.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CommunitySos.Infrastructure.Identity;

public static class IdentityServiceCollectionExtensions
{
    public static IServiceCollection AddIdentityServices(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<FirebaseOptions>().Bind(configuration.GetSection(FirebaseOptions.SectionName));
        services.AddSingleton<FirebaseAppProvider>();

        services.AddSingleton<JwtSigningKeys>();
        services.AddSingleton<ITokenService, JwtTokenService>();
        services.AddMemoryCache();
        services.AddScoped<IUserStatusReader, UserStatusReader>();

        var useDevVerifier = configuration.GetValue<bool>($"{FirebaseOptions.SectionName}:{nameof(FirebaseOptions.UseDevelopmentVerifier)}");
        if (useDevVerifier && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Firebase:UseDevelopmentVerifier may only be enabled in the Development environment.");
        }

        if (useDevVerifier)
        {
            services.AddSingleton<IFirebaseAuthService, DevelopmentFirebaseAuthService>();
        }
        else
        {
            services.AddSingleton<IFirebaseAuthService, FirebaseAuthService>();
        }

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>, JwtSigningKeys>((bearer, jwt, keys) =>
            {
                bearer.MapInboundClaims = false; // keep "sub"/"role" as-is
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = jwt.Value.Issuer,
                    ValidateAudience = true,
                    ValidAudience = jwt.Value.Audience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = keys.SigningKey,
                    ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AppClaimTypes.Subject,
                    RoleClaimType = AppClaimTypes.Role,
                };
                bearer.Events = new JwtBearerEvents
                {
                    // Reject tokens of suspended/deleted accounts before their 15-minute expiry.
                    OnTokenValidated = async context =>
                    {
                        var sub = context.Principal?.FindFirst(AppClaimTypes.Subject)?.Value;
                        if (!Guid.TryParse(sub, out var userId))
                        {
                            context.Fail("Token has no valid subject.");
                            return;
                        }

                        var statusReader = context.HttpContext.RequestServices.GetRequiredService<IUserStatusReader>();
                        if (!await statusReader.IsActiveAsync(userId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("Account is not active.");
                        }
                    },
                };
            });

        return services;
    }
}
