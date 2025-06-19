using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;
using Microsoft.OpenApi.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Ship.Ses.Transmitter.WebApi.Installers
{
    public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
    {
        private readonly IApiVersionDescriptionProvider _provider;
        public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider) => _provider = provider;

        public void Configure(SwaggerGenOptions options)
        {
            // Add a swagger document for each discovered API version
            // NOTE: "ApiVersion" is not set in the OpenApiInfo if there's only one version.
            foreach (var description in _provider.ApiVersionDescriptions)
            {
                options.SwaggerDoc(description.GroupName, CreateInfoForApiVersion(description));
            }
        }

        private static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
        {
            var info = new OpenApiInfo()
            {
                Title = $"SHIP SES FHIR Ingest API {description.ApiVersion}",
                Version = description.ApiVersion.ToString(),
                Description = "API for receiving FHIR-compliant data from EMRs.",
                Contact = new OpenApiContact { Name = "Interswitch", Email = "contact@interswitchgroup.com" },
                //License = new OpenApiLicense { Name = "MIT License", Url = new Uri("https://opensource.org/licenses/MIT") } // Example license
            };

            if (description.IsDeprecated)
            {
                info.Description += " This API version has been deprecated.";
            }

            return info;
        }
        
    }
}
