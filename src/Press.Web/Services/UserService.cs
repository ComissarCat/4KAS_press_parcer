using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

public class UserService(IDbContextFactory<AppDbContext> dbFactory)
{
    private readonly PasswordHasher<AppUser> _hasher = new();

    public async Task<ClaimsPrincipal?> ValidateAsync(string login, string password, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var norm = login.Trim().ToLowerInvariant();
        var user = await db.Users.FirstOrDefaultAsync(u => u.Login == norm && u.IsActive, ct);
        if (user == null) return null;
        var check = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (check == PasswordVerificationResult.Failed) return null;
        if (check == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
            await db.SaveChangesAsync(ct);
        }
        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName.Length > 0 ? user.DisplayName : user.Login),
        ], "Cookies");
        return new ClaimsPrincipal(identity);
    }

    public async Task<string?> CreateAsync(string login, string displayName, string password, CancellationToken ct = default)
    {
        var norm = login.Trim().ToLowerInvariant();
        if (norm.Length == 0) return "Укажите логин";
        if (password.Length < 6) return "Пароль должен быть не короче 6 символов";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (await db.Users.AnyAsync(u => u.Login == norm, ct)) return "Такой логин уже есть";
        var user = new AppUser { Login = norm, DisplayName = displayName.Trim() };
        user.PasswordHash = _hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> SetPasswordAsync(int userId, string password, CancellationToken ct = default)
    {
        if (password.Length < 6) return "Пароль должен быть не короче 6 символов";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.PasswordHash = _hasher.HashPassword(user, password);
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task<string?> SetActiveAsync(int userId, bool active, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (!active && await db.Users.CountAsync(u => u.IsActive && u.Id != userId, ct) == 0)
            return "Нельзя отключить последнего активного пользователя";
        var user = await db.Users.FirstAsync(u => u.Id == userId, ct);
        user.IsActive = active;
        await db.SaveChangesAsync(ct);
        return null;
    }
}
