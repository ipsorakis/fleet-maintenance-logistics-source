using FML.Api.Common;
using FML.Api.Data;
using FML.Common.Auth;
using Microsoft.EntityFrameworkCore;

namespace FML.Api.Auth;

/// <summary>
/// Cross-cutting user/authentication module. Both business journeys use it to
/// resolve technicians and planners.
/// </summary>
public class AuthService
{
    private readonly FmlDbContext _db;

    public AuthService(FmlDbContext db)
    {
        _db = db;
    }

    public static string HashPassword(string password) =>
        PasswordHasher.Hash(FmlConfig.PasswordSalt, password);

    public Task<List<User>> GetUsersAsync() =>
        _db.Users.OrderBy(u => u.Username).ToListAsync();

    public Task<User?> GetUserAsync(int id) =>
        _db.Users.FirstOrDefaultAsync(u => u.Id == id);

    public async Task<User> CreateUserAsync(CreateUserRequest request)
    {
        var user = new User
        {
            Username = request.Username,
            Email = request.Email,
            Role = request.Role,
            PasswordHash = HashPassword(request.Password),
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<LoginResponse?> LoginAsync(LoginRequest request)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == request.Username && u.IsActive);
        if (user is null || user.PasswordHash != HashPassword(request.Password))
        {
            return null;
        }

        user.LastLoginUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        var expires = DateTime.UtcNow.AddMinutes(FmlConfig.TokenLifetimeMinutes);
        var token = AuthTokenFactory.Issue(FmlConfig.PasswordSalt, user.Id, user.Username, expires);
        return new LoginResponse(token, user.Username, user.Role, expires);
    }

    /// <summary>Used by MaintenanceScheduling when it needs somebody to assign a work order to.</summary>
    public Task<User?> FindAvailableTechnicianAsync() =>
        _db.Users
            .Where(u => u.IsActive && u.Role == UserRole.Technician)
            .OrderBy(u => u.Id)
            .FirstOrDefaultAsync();

    public async Task<bool> DeactivateAsync(int id)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user is null)
        {
            return false;
        }

        user.IsActive = false;
        await _db.SaveChangesAsync();
        return true;
    }
}
