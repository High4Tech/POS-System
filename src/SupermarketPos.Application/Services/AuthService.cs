using System.Text;
using SupermarketPos.Application.Abstractions;
using SupermarketPos.Domain.Common;
using SupermarketPos.Domain.Entities;

namespace SupermarketPos.Application.Services;

/// <summary>
/// Holds the current session and authenticates against stored BCrypt hashes.
/// Registered as a singleton so the signed-in user is shared across the app.
/// </summary>
public sealed class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IPasswordHasher _hasher;

    public AuthService(IUserRepository users, IPasswordHasher hasher)
    {
        _users = users;
        _hasher = hasher;
    }

    public User? CurrentUser { get; private set; }
    public bool IsLoggedIn => CurrentUser is not null;

    public async Task<User> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            throw new AuthException("Username and password are required.");

        var user = await _users.GetByUsernameAsync(username.Trim(), ct);
        if (user is null || !_hasher.Verify(password, user.PasswordHash))
            throw new AuthException("Invalid username or password.");

        CurrentUser = user;
        return user;
    }

    public void Logout() => CurrentUser = null;

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken ct = default)
    {
        var user = CurrentUser ?? throw new AuthException("Not signed in.");
        if (string.IsNullOrWhiteSpace(newPassword) || Encoding.UTF8.GetByteCount(newPassword) > 72)
            throw new DomainException("Enter a password within BCrypt's 72-byte limit.");

        var previousHash = user.PasswordHash;
        if (string.IsNullOrEmpty(currentPassword) || !_hasher.Verify(currentPassword, previousHash))
            throw new AuthException("The current password is incorrect.");

        user.SetPasswordHash(_hasher.Hash(newPassword));
        try
        {
            await _users.SaveAsync(user, ct);
        }
        catch
        {
            user.SetPasswordHash(previousHash);
            throw;
        }
    }

    public void RequireAdmin()
    {
        if (CurrentUser is null)
            throw new AuthException("Not signed in.");
        if (!CurrentUser.IsAdmin)
            throw new AuthException("Administrator privileges required.");
    }

    public async Task UpdateOperatorNameAsync(string name, CancellationToken ct = default)
    {
        var user = CurrentUser ?? throw new AuthException("Not signed in.");
        var previous = user.DisplayName;
        user.SetDisplayName(name);
        try
        {
            await _users.SaveDisplayNameAsync(user, ct);
        }
        catch
        {
            user.SetDisplayName(previous);
            throw;
        }
    }
}
