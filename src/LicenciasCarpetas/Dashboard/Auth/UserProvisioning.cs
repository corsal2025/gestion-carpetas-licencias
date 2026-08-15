namespace LicenciasCarpetas.Dashboard.Auth;

public enum ProvisioningResult
{
    Created,
    UsernameInvalid,
    UsernameTaken,
    UserNotFound,
    PasswordTooShort,
    PasswordMismatch
}

/// <summary>
/// The single place that creates accounts and sets passwords, so the console and the dashboard
/// cannot disagree about the rules (the CLI used to accept passwords the dashboard rejected).
/// </summary>
public sealed class UserProvisioning(IUserRepository users)
{
    public const int MinimumPasswordLength = 8;

    public bool HasNoUsers() => users.Count() == 0;

    public ProvisioningResult Create(string? username, string? password, string? confirmation)
    {
        var normalized = Normalize(username);
        if (normalized is null)
        {
            return ProvisioningResult.UsernameInvalid;
        }

        if (Validate(password, confirmation) is { } failure)
        {
            return failure;
        }

        if (users.FindByUsername(normalized) is not null)
        {
            return ProvisioningResult.UsernameTaken;
        }

        var (hash, salt, iterations) = PasswordHasher.Hash(password!);
        users.Insert(new DashboardUser
        {
            Username = normalized,
            PasswordHash = hash,
            PasswordSalt = salt,
            Iterations = iterations
        });

        return ProvisioningResult.Created;
    }

    /// <summary>Replaces a password and lifts any lockout — recovering an account must not leave
    /// the operator waiting out the fifteen minutes that locked them out.</summary>
    public ProvisioningResult SetPassword(string? username, string? password, string? confirmation)
    {
        var normalized = Normalize(username);
        if (normalized is null)
        {
            return ProvisioningResult.UsernameInvalid;
        }

        if (Validate(password, confirmation) is { } failure)
        {
            return failure;
        }

        if (users.FindByUsername(normalized) is not { } user)
        {
            return ProvisioningResult.UserNotFound;
        }

        var (hash, salt, iterations) = PasswordHasher.Hash(password!);
        users.ResetPassword(user.Id, hash, salt, iterations);
        return ProvisioningResult.Created;
    }

    /// <summary>Usernames are stored lower-cased and trimmed: the operator typing "Operador" at
    /// the login screen must not end up locked out of the account they created as "operador".</summary>
    private static string? Normalize(string? username)
    {
        var trimmed = username?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private static ProvisioningResult? Validate(string? password, string? confirmation)
    {
        if (password is null || password.Trim().Length < MinimumPasswordLength)
        {
            return ProvisioningResult.PasswordTooShort;
        }

        return password != confirmation ? ProvisioningResult.PasswordMismatch : null;
    }

    public static string Describe(ProvisioningResult result) => result switch
    {
        ProvisioningResult.Created => "Listo.",
        ProvisioningResult.UsernameInvalid => "Escriba un nombre de usuario.",
        ProvisioningResult.UsernameTaken => "Ese usuario ya existe.",
        ProvisioningResult.UserNotFound => "Ese usuario no existe.",
        ProvisioningResult.PasswordTooShort => $"La contraseña debe tener al menos {MinimumPasswordLength} caracteres.",
        ProvisioningResult.PasswordMismatch => "Las dos contraseñas no coinciden.",
        _ => "No se pudo completar la operación."
    };
}
