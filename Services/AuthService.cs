using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Cubichi.DataBase;
using Cubichi.Models;
using Cubichi.Models.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Cubichi.Services;

public class AuthService : IAuthService
{
    private readonly IDataBaseInteractor _dataBaseInteractor;

    public AuthService(IDataBaseInteractor dataBaseInteractor)
    {
        _dataBaseInteractor = dataBaseInteractor;
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request)
    {
        // Get the user from the database by username and verify the password
        var user = await _dataBaseInteractor.GetUserAsync(request.UserName);
        if (user == null || !VerifyPasswordHash(request.Password, Encoding.UTF8.GetBytes(user.PasswordHash), Encoding.UTF8.GetBytes(user.PasswordSalt)))
        {
            throw new Exception("Invalid username or password");
        }

        // Generate a JWT token
        var token = GenerateJwtToken(user);

        return new AuthResponse
        {
            Token = (string)token
        };
    }

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request)
    {
        // Check if user with this username or email exists 
        var user = await _dataBaseInteractor.GetUserAsync(request.UserName);
        if (user != null)
        {
            throw new Exception("User with this username already exists");
        }

        user = await _dataBaseInteractor.GetUserAsync(request.Email);
        if (user != null)
        {
            throw new Exception("User with this email already exists");
        }


        // Create a new user
        CreatePasswordHash(request.Password, out var passwordHash, out var passwordSalt);
        user = new User
        {
            UserName = request.UserName,
            Email = request.Email,
            PasswordHash = Encoding.UTF8.GetString(passwordHash),
            PasswordSalt = Encoding.UTF8.GetString(passwordSalt)
        };

        // Save the user to the database
        user = await _dataBaseInteractor.CreateUserAsync(user);

        return new AuthResponse
        {
            Token = (string)GenerateJwtToken(user)
        };

    }

    private static void CreatePasswordHash(string password, out byte[] passwordHash, out byte[] passwordSalt)
    {
        using var hmac = new HMACSHA512();
        passwordSalt = hmac.Key;
        passwordHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
    }

    private static bool VerifyPasswordHash(string password, byte[] passwordHash, byte[] passwordSalt)
    {
        using var hmac = new HMACSHA512(passwordSalt);
        var computedHash = hmac.ComputeHash(Encoding.UTF8.GetBytes(password));
        return computedHash.SequenceEqual(passwordHash);
    }

    private static object GenerateJwtToken(User user)
    {
        List<Claim> claims =
        [
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.UserName)
        ];

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("JWT_SECRET") ?? throw new Exception("JWT secret not found")));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: Environment.GetEnvironmentVariable("JWT_ISSUER") ?? throw new Exception("JWT issuer not found"),
            audience: Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? throw new Exception("JWT audience not found"),
            claims: claims,
            expires: DateTime.Now.AddDays(7),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}