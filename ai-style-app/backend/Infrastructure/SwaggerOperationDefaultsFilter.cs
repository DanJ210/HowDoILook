using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace AiStyleApp.Api.Infrastructure;

public class SwaggerOperationDefaultsFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ApplyAnalyticsParameterExamples(operation, context);
    }

    private static void ApplyAnalyticsParameterExamples(OpenApiOperation operation, OperationFilterContext context)
    {
        if (context.ApiDescription.ActionDescriptor is not ControllerActionDescriptor actionDescriptor)
        {
            return;
        }

        if (!string.Equals(actionDescriptor.ControllerName, "Analytics", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (operation.Parameters is null)
        {
            return;
        }

        foreach (var parameter in operation.Parameters)
        {
            if (parameter is not OpenApiParameter openApiParameter)
            {
                continue;
            }

            if (string.Equals(parameter.Name, "format", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = "Export format. Supported values: json or csv.";
                openApiParameter.Example = JsonValue.Create("json");
                continue;
            }

            if (string.Equals(parameter.Name, "from", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = "Inclusive UTC start date/time in ISO 8601 format. Example: 2026-01-15T10:30:00Z";
                openApiParameter.Example = JsonValue.Create("2026-01-15T10:30:00Z");
                continue;
            }

            if (string.Equals(parameter.Name, "to", StringComparison.OrdinalIgnoreCase))
            {
                parameter.Description = "Inclusive UTC end date/time in ISO 8601 format. Example: 2026-01-31T23:59:59Z";
                openApiParameter.Example = JsonValue.Create("2026-01-31T23:59:59Z");
            }
        }
    }
}
