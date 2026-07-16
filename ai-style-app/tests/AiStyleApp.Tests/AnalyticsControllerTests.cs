using AiStyleApp.Backend.Controllers;
using Microsoft.AspNetCore.Authorization;

namespace AiStyleApp.Tests;

public class AnalyticsControllerTests
{
    [Fact]
    public void AnalyticsController_HasAuthorizeAttribute()
    {
        var authorizeAttribute = typeof(AnalyticsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
            .SingleOrDefault();

        Assert.NotNull(authorizeAttribute);
    }
}
