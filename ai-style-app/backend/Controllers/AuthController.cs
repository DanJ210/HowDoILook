using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace AiStyleApp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public AuthController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("token")]
    [AllowAnonymous]
    public ActionResult<object> CreateToken([FromBody] DevTokenRequest request)
    {
        var authMode = (_configuration["Auth:Mode"] ?? "dev").Trim().ToLowerInvariant();
        if (authMode != "dev")
        {
            return NotFound();
        }

        var jwtKey = _configuration["Jwt:Key"];
        var issuer = _configuration["Jwt:Issuer"];
        var audience = _configuration["Jwt:Audience"];

        if (string.IsNullOrWhiteSpace(jwtKey) || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
        {
            return StatusCode(500, "JWT configuration is missing.");
        }

        var username = string.IsNullOrWhiteSpace(request.Username) ? "dev-user" : request.Username.Trim();
        var expiresMinutes = request.ExpiresInMinutes is > 0 and <= 1440 ? request.ExpiresInMinutes : 60;

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, username),
            new Claim(JwtRegisteredClaimNames.UniqueName, username),
            new Claim(ClaimTypes.Name, username),
            new Claim(ClaimTypes.Role, "User")
        };

        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var expiresAt = DateTime.UtcNow.AddMinutes(expiresMinutes);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            expires: expiresAt,
            signingCredentials: credentials);

        var tokenValue = new JwtSecurityTokenHandler().WriteToken(token);

        return Ok(new
        {
            accessToken = tokenValue,
            tokenType = "Bearer",
            expiresAtUtc = expiresAt
        });
    }

    public sealed class DevTokenRequest
    {
        public string? Username { get; set; }
        public int ExpiresInMinutes { get; set; } = 60;
    }
}
