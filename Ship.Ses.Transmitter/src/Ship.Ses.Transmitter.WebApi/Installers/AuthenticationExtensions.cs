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
                options.Authority = oktaDomain + "/oauth2/default"; // Or your custom authorization server ID: "/oauth2/{yourAuthServerId}"
                options.Audience = oktaAudience;
                options.RequireHttpsMetadata = true; // Always use HTTPS in production
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true
                };
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
