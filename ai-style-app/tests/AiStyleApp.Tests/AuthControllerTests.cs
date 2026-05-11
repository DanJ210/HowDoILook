using AiStyleApp.Api.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace AiStyleApp.Tests;

public class AuthControllerTests
{
    private const string TestJwtKey = "super-secret-key-that-is-at-least-32-characters-long-for-HS256";
    private const string TestIssuer = "test-issuer";
    private const string TestAudience = "test-audience";

    private static IConfiguration CreateValidConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            { "Jwt:Key", TestJwtKey },
            { "Jwt:Issuer", TestIssuer },
            { "Jwt:Audience", TestAudience }
        };
        var configBuilder = new ConfigurationBuilder().AddInMemoryCollection(dict);
        return configBuilder.Build();
    }

    private static IConfiguration CreateMissingKeyConfiguration()
    {
        var dict = new Dictionary<string, string?>
        {
            { "Jwt:Key", null },
            { "Jwt:Issuer", TestIssuer },
            { "Jwt:Audience", TestAudience }
        };
        var configBuilder = new ConfigurationBuilder().AddInMemoryCollection(dict);
        return configBuilder.Build();
    }

    private static Dictionary<string, object?>? ToJsonDictionary(object? obj)
    {
        if (obj == null) return null;
        var json = JsonSerializer.Serialize(obj);
        return JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
    }

    private static string? GetJsonString(Dictionary<string, object?>? dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out var value)) return null;
        if (value is JsonElement je) return je.GetString();
        return value?.ToString();
    }

    private static DateTime? GetJsonDateTime(Dictionary<string, object?>? dict, string key)
    {
        if (dict == null || !dict.TryGetValue(key, out var value)) return null;
        if (value is JsonElement je)
        {
            if (je.TryGetDateTime(out var dt)) return dt;
            if (DateTime.TryParse(je.GetString(), out var dt2)) return dt2;
        }
        if (value is DateTime dt3) return dt3;
        if (value is string strValue && DateTime.TryParse(strValue, out var dt4)) return dt4;
        return null;
    }

    [Fact]
    public void CreateToken_WithValidRequest_ReturnsOkWithAccessToken()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user", ExpiresInMinutes = 60 };

        // Act
        var result = controller.CreateToken(request);

        // Assert
        Assert.NotNull(result);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        Assert.True(responseData.ContainsKey("accessToken"), "Response should contain accessToken");
        Assert.True(responseData.ContainsKey("tokenType"), "Response should contain tokenType");
        Assert.True(responseData.ContainsKey("expiresAtUtc"), "Response should contain expiresAtUtc");

        var tokenType = GetJsonString(responseData, "tokenType");
        Assert.Equal("Bearer", tokenType);
    }

    [Fact]
    public void CreateToken_WithValidToken_HasCorrectClaims()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "alice", ExpiresInMinutes = 30 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var tokenValue = GetJsonString(responseData, "accessToken");
        Assert.NotNull(tokenValue);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadToken(tokenValue) as JwtSecurityToken;

        Assert.NotNull(token);
        Assert.Equal(TestIssuer, token.Issuer);
        Assert.Equal(TestAudience, token.Audiences.First());
        
        var subClaim = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);
        Assert.NotNull(subClaim);
        Assert.Equal("alice", subClaim.Value);

        var roleClaim = token.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Role);
        Assert.NotNull(roleClaim);
        Assert.Equal("User", roleClaim.Value);
    }

    [Fact]
    public void CreateToken_WithNullUsername_UsesDefaultDevUser()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = null, ExpiresInMinutes = 60 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var tokenValue = GetJsonString(responseData, "accessToken");
        Assert.NotNull(tokenValue);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadToken(tokenValue) as JwtSecurityToken;
        var subClaim = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);

        Assert.NotNull(subClaim);
        Assert.Equal("dev-user", subClaim.Value);
    }

    [Fact]
    public void CreateToken_WithWhitespaceUsername_UsesDefaultDevUser()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "   ", ExpiresInMinutes = 60 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var tokenValue = GetJsonString(responseData, "accessToken");
        Assert.NotNull(tokenValue);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var token = handler.ReadToken(tokenValue) as JwtSecurityToken;
        var subClaim = token.Claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.Sub);

        Assert.Equal("dev-user", subClaim.Value);
    }

    [Fact]
    public void CreateToken_WithDefaultExpiresInMinutes_Uses60Minutes()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user" };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var expiresAtUtc = GetJsonDateTime(responseData, "expiresAtUtc");
        Assert.NotNull(expiresAtUtc);

        // Assert
        var timeUntilExpiry = expiresAtUtc.Value - DateTime.UtcNow;
        Assert.True(timeUntilExpiry.TotalMinutes >= 59 && timeUntilExpiry.TotalMinutes <= 61,
            $"Expected ~60 minutes, got {timeUntilExpiry.TotalMinutes}");
    }

    [Fact]
    public void CreateToken_WithCustomExpiresInMinutes_RespectsValue()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user", ExpiresInMinutes = 120 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var expiresAtUtc = GetJsonDateTime(responseData, "expiresAtUtc");
        Assert.NotNull(expiresAtUtc);

        // Assert
        var timeUntilExpiry = expiresAtUtc.Value - DateTime.UtcNow;
        Assert.True(timeUntilExpiry.TotalMinutes >= 119 && timeUntilExpiry.TotalMinutes <= 121,
            $"Expected ~120 minutes, got {timeUntilExpiry.TotalMinutes}");
    }

    [Fact]
    public void CreateToken_WithExpiresInMinutesExceedingMax_ClampsTo1440()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user", ExpiresInMinutes = 2000 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var expiresAtUtc = GetJsonDateTime(responseData, "expiresAtUtc");
        Assert.NotNull(expiresAtUtc);

        // Assert
        var timeUntilExpiry = expiresAtUtc.Value - DateTime.UtcNow;
        Assert.True(timeUntilExpiry.TotalMinutes >= 1439 && timeUntilExpiry.TotalMinutes <= 1441,
            $"Expected ~1440 minutes (clamped), got {timeUntilExpiry.TotalMinutes}");
    }

    [Fact]
    public void CreateToken_WithZeroExpiresInMinutes_UsesDefault60()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user", ExpiresInMinutes = 0 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var expiresAtUtc = GetJsonDateTime(responseData, "expiresAtUtc");
        Assert.NotNull(expiresAtUtc);

        // Assert
        var timeUntilExpiry = expiresAtUtc.Value - DateTime.UtcNow;
        Assert.True(timeUntilExpiry.TotalMinutes >= 59 && timeUntilExpiry.TotalMinutes <= 61,
            $"Expected ~60 minutes (default), got {timeUntilExpiry.TotalMinutes}");
    }

    [Fact]
    public void CreateToken_WithNegativeExpiresInMinutes_UsesDefault60()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user", ExpiresInMinutes = -10 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var expiresAtUtc = GetJsonDateTime(responseData, "expiresAtUtc");
        Assert.NotNull(expiresAtUtc);

        // Assert
        var timeUntilExpiry = expiresAtUtc.Value - DateTime.UtcNow;
        Assert.True(timeUntilExpiry.TotalMinutes >= 59 && timeUntilExpiry.TotalMinutes <= 61,
            $"Expected ~60 minutes (default), got {timeUntilExpiry.TotalMinutes}");
    }

    [Fact]
    public void CreateToken_WithMissingJwtKey_Returns500Error()
    {
        // Arrange
        var config = CreateMissingKeyConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user" };

        // Act
        var result = controller.CreateToken(request);

        // Assert
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var statusResult = Assert.IsType<ObjectResult>(actionResult.Result);
        Assert.Equal(500, statusResult.StatusCode);
        var errorMessage = statusResult.Value?.ToString();
        Assert.Contains("JWT configuration is missing", errorMessage);
    }

    [Fact]
    public void CreateToken_WithValidToken_TokenIsValidJwt()
    {
        // Arrange
        var config = CreateValidConfiguration();
        var controller = new AuthController(config);
        var request = new AuthController.DevTokenRequest { Username = "test-user", ExpiresInMinutes = 60 };

        // Act
        var result = controller.CreateToken(request);
        var actionResult = Assert.IsType<ActionResult<object>>(result);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var responseData = ToJsonDictionary(okResult.Value);
        Assert.NotNull(responseData);
        var tokenValue = GetJsonString(responseData, "accessToken");
        Assert.NotNull(tokenValue);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var canRead = handler.CanReadToken(tokenValue);
        Assert.True(canRead, "Token should be readable as a valid JWT");

        var token = handler.ReadToken(tokenValue) as JwtSecurityToken;
        Assert.NotNull(token);
        Assert.True(token.ValidFrom <= DateTime.UtcNow, "Token should be valid immediately");
        Assert.True(token.ValidTo > DateTime.UtcNow, "Token should not be expired");
    }
}
