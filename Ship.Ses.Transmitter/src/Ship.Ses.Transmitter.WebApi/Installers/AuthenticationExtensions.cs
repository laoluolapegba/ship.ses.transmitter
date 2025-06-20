namespace Ship.Ses.Transmitter.WebApi.Installers
{
    // Ship.Ses.Transmitter.WebApi/Extensions/AuthenticationExtensions.cs

    using Microsoft.AspNetCore.Authentication.JwtBearer;
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.IdentityModel.Tokens;
    using Microsoft.OpenApi.Models; // For Swagger
    using Swashbuckle.AspNetCore.SwaggerGen; // For SwaggerGenOptions

    public static class AuthenticationExtensions
    {
        public static IServiceCollection AddOktaAuthentication(this IServiceCollection services, IConfiguration configuration)
        {
            // Get the Okta configuration section
            var oktaSection = configuration.GetSection("AppSettings:Authentication"); 

            var oktaDomain = oktaSection["Authority"];    
            var oktaAudience = oktaSection["Audience"]; 

            if (string.IsNullOrWhiteSpace(oktaDomain))
            {
                throw new InvalidOperationException("Okta:Domain is not configured in appsettings.json.");
            }
            if (string.IsNullOrWhiteSpace(oktaAudience))
            {
                throw new InvalidOperationException("Okta:Audience is not configured in appsettings.json.");
            }

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.Authority = oktaDomain ;
                options.Audience = oktaAudience;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };

                // --- IMPORTANT: ADD JWT Bearer Events for detailed logging ---
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogError(context.Exception, "Authentication failed.");
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogDebug("Token successfully validated for user: {UserName}", context.Principal?.Identity?.Name);
                        // Log all claims for inspection
                        foreach (var claim in context.Principal?.Claims ?? Array.Empty<System.Security.Claims.Claim>())
                        {
                            logger.LogDebug("Claim - Type: {ClaimType}, Value: {ClaimValue}", claim.Type, claim.Value);
                        }
                        return Task.CompletedTask;
                    },
                    OnForbidden = context =>
                    {
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        logger.LogWarning("Forbidden: The authenticated user is not authorized to access this resource.");
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        // This event is called when authentication fails (e.g., token missing, invalid, or expired)
                        // It's a good place to log why the challenge occurred.
                        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<JwtBearerEvents>>();
                        if (context.AuthenticateFailure != null)
                        {
                            logger.LogError(context.AuthenticateFailure, "Authentication challenge failed: {ErrorMessage}", context.AuthenticateFailure.Message);
                        }
                        else if (!string.IsNullOrEmpty(context.ErrorDescription))
                        {
                            logger.LogWarning("Authentication challenge: {Error} - {ErrorDescription}", context.Error, context.ErrorDescription);
                        }
                        else
                        {
                            logger.LogWarning("Authentication challenge occurred (no specific error details provided).");
                        }
                        return Task.CompletedTask;
                    }
                };
                // --- END JWT Bearer Events ---
            });

            services.AddAuthorization(); // Add authorization services

            return services;
        }

        // This method will be used inside your InstallSwagger extension
        public static void AddSwaggerSecurityDefinition(this SwaggerGenOptions options)
        {
            // Define the OAuth2.0 scheme for Swagger
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                Name = "Authorization",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.Http,
                Scheme = "bearer" // lowercase "bearer" is important for Swagger UI
            });

            // Add a security requirement to all operations (or specific ones if preferred)
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>() // Add specific scopes if your API uses them beyond basic validation
                }
            });
        }
    }
}
