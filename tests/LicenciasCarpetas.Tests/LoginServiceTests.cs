using LicenciasCarpetas.Dashboard.Auth;

namespace LicenciasCarpetas.Tests;

public class LoginServiceTests
{
    private static (LoginService Login, UserRepository Users) Build(SqliteTestDatabase db, string password = "clave-secreta-1")
    {
        var users = new UserRepository(db.ConnectionString);
        users.EnsureSchema();

        var (hash, salt, iterations) = PasswordHasher.Hash(password);
        users.Insert(new DashboardUser
        {
            Username = "operador",
            PasswordHash = hash,
            PasswordSalt = salt,
            Iterations = iterations
        });

        return (new LoginService(users), users);
    }

    [Fact]
    public async Task Accepts_the_right_password()
    {
        using var db = new SqliteTestDatabase();
        var (login, _) = Build(db);

        Assert.Equal(LoginOutcome.Success, await login.TryLoginAsync("operador", "clave-secreta-1"));
    }

    /// <summary>
    /// Accounts are stored lower-cased, but nobody types their username that carefully — and a
    /// login form that refuses "Operador" for an account created as "operador" reads as a lost
    /// password, not as a capital letter.
    /// </summary>
    [Theory]
    [InlineData("OPERADOR")]
    [InlineData("Operador")]
    [InlineData("  operador  ")]
    public async Task The_username_is_matched_regardless_of_case_and_spacing(string typed)
    {
        using var db = new SqliteTestDatabase();
        var (login, _) = Build(db);

        Assert.Equal(LoginOutcome.Success, await login.TryLoginAsync(typed, "clave-secreta-1"));
    }

    [Fact]
    public async Task Rejects_a_wrong_password_and_an_unknown_user()
    {
        using var db = new SqliteTestDatabase();
        var (login, _) = Build(db);

        Assert.Equal(LoginOutcome.InvalidCredentials, await login.TryLoginAsync("operador", "otra-cosa"));
        Assert.Equal(LoginOutcome.InvalidCredentials, await login.TryLoginAsync("nadie", "clave-secreta-1"));
    }

    /// <summary>Trailing whitespace is the classic paste accident, and it must not silently pass.</summary>
    [Fact]
    public async Task A_password_with_a_trailing_space_is_not_the_same_password()
    {
        using var db = new SqliteTestDatabase();
        var (login, _) = Build(db);

        Assert.Equal(LoginOutcome.InvalidCredentials, await login.TryLoginAsync("operador", "clave-secreta-1 "));
    }

    [Fact]
    public async Task Locks_the_account_after_five_failed_attempts()
    {
        using var db = new SqliteTestDatabase();
        var (login, users) = Build(db);

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            Assert.Equal(LoginOutcome.InvalidCredentials, await login.TryLoginAsync("operador", "mala"));
        }

        Assert.Equal(LoginOutcome.LockedOut, await login.TryLoginAsync("operador", "mala"));
        // Even the right password is refused while the lockout lasts.
        Assert.Equal(LoginOutcome.LockedOut, await login.TryLoginAsync("operador", "clave-secreta-1"));
        Assert.NotNull(users.FindByUsername("operador")!.LockedUntil);
    }

    [Fact]
    public async Task A_successful_login_clears_the_failed_attempt_count()
    {
        using var db = new SqliteTestDatabase();
        var (login, users) = Build(db);

        await login.TryLoginAsync("operador", "mala");
        await login.TryLoginAsync("operador", "clave-secreta-1");

        Assert.Equal(0, users.FindByUsername("operador")!.FailedLoginAttempts);
    }

    /// <summary>What password recovery does underneath: the old password stops working immediately.</summary>
    [Fact]
    public async Task After_a_reset_only_the_new_password_works()
    {
        using var db = new SqliteTestDatabase();
        var (login, users) = Build(db);
        var user = users.FindByUsername("operador")!;

        var (hash, salt, iterations) = PasswordHasher.Hash("clave-nueva-2");
        users.UpdatePassword(user.Id, hash, salt, iterations);

        Assert.Equal(LoginOutcome.Success, await login.TryLoginAsync("operador", "clave-nueva-2"));
        Assert.Equal(LoginOutcome.InvalidCredentials, await login.TryLoginAsync("operador", "clave-secreta-1"));
    }

    /// <summary>A reset also has to lift a lockout, otherwise recovering the password still leaves
    /// the operator locked out for fifteen minutes.</summary>
    [Fact]
    public async Task A_reset_lifts_an_active_lockout()
    {
        using var db = new SqliteTestDatabase();
        var (login, users) = Build(db);
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            await login.TryLoginAsync("operador", "mala");
        }
        var user = users.FindByUsername("operador")!;

        var (hash, salt, iterations) = PasswordHasher.Hash("clave-nueva-2");
        users.ResetPassword(user.Id, hash, salt, iterations);

        Assert.Equal(LoginOutcome.Success, await login.TryLoginAsync("operador", "clave-nueva-2"));
    }
}
