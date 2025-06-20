using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.Filters;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.IO;
using System.Reflection;

namespace Ship.Ses.Transmitter.WebApi.Installers
{
    public static class SwaggerInstaller
    {
        public static void InstallSwagger(this WebApplicationBuilder builder)
        {
            builder.Services.AddControllers();
            //builder.Services.AddSwaggerExamplesFromAssemblyOf<Program>();
            //Configure Swagger/OpenAPI
            builder.Services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>(); // This is key for versioning with Swagger

            builder.Services.AddSwaggerGen(options =>
            {
                options.EnableAnnotations();
                //options.ExampleFilters();
                //Set the comments path for the Swagger JSON and UI.
                var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFilename);
                options.IncludeXmlComments(xmlPath);

                // Add Swagger security definition and requirement here
                options.AddSwaggerSecurityDefinition(); // <--- Call the new extension method

            });
        }
    }

}
