using LicenciasCarpetas.Dashboard.Auth;

namespace LicenciasCarpetas.Tests;

/// <summary>
/// Creating accounts and setting passwords used to live only in the CLI, and each entry point
/// applied its own rules — the console let through a two-character password the dashboard refused.
/// One place decides now.
/// </summary>
public class UserProvisioningTests
{
    private static (UserProvisioning Provisioning, UserRepository Users) Build(SqliteTestDatabase db)
    {
        var users = new UserRepository(db.ConnectionString);
        users.EnsureSchema();
        return (new UserProvisioning(users), users);
    }

    [Fact]
    public void Creates_a_user_that_can_then_log_in()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, users) = Build(db);

        var result = provisioning.Create("operador", "clave-larga-1", "clave-larga-1");

        Assert.Equal(ProvisioningResult.Created, result);
        var user = users.FindByUsername("operador");
        Assert.NotNull(user);
        Assert.True(PasswordHasher.Verify("clave-larga-1", user.PasswordHash, user.PasswordSalt, user.Iterations));
    }

    [Theory]
    [InlineData("corta1")]
    [InlineData("")]
    [InlineData("       ")]
    public void Refuses_a_password_under_eight_characters(string password)
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, users) = Build(db);

        Assert.Equal(ProvisioningResult.PasswordTooShort, provisioning.Create("operador", password, password));
        Assert.Null(users.FindByUsername("operador"));
    }

    [Fact]
    public void Refuses_when_the_confirmation_does_not_match()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, users) = Build(db);

        Assert.Equal(ProvisioningResult.PasswordMismatch,
            provisioning.Create("operador", "clave-larga-1", "clave-larga-2"));
        Assert.Null(users.FindByUsername("operador"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Refuses_an_empty_username(string username)
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, _) = Build(db);

        Assert.Equal(ProvisioningResult.UsernameInvalid,
            provisioning.Create(username, "clave-larga-1", "clave-larga-1"));
    }

    [Fact]
    public void Refuses_a_username_that_already_exists()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, _) = Build(db);
        provisioning.Create("operador", "clave-larga-1", "clave-larga-1");

        Assert.Equal(ProvisioningResult.UsernameTaken,
            provisioning.Create("operador", "otra-clave-2", "otra-clave-2"));
    }

    [Fact]
    public void The_username_is_trimmed_and_matched_regardless_of_case()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, users) = Build(db);

        provisioning.Create("  Operador  ", "clave-larga-1", "clave-larga-1");

        Assert.NotNull(users.FindByUsername("operador"));
        Assert.Equal(ProvisioningResult.UsernameTaken,
            provisioning.Create("OPERADOR", "clave-larga-1", "clave-larga-1"));
    }

    [Fact]
    public void Setting_a_password_replaces_it_and_clears_any_lockout()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, users) = Build(db);
        provisioning.Create("operador", "clave-larga-1", "clave-larga-1");
        var user = users.FindByUsername("operador")!;
        users.RecordFailedLogin(user.Id, 5, DateTimeOffset.UtcNow.AddMinutes(15));

        var result = provisioning.SetPassword("operador", "clave-nueva-2", "clave-nueva-2");

        Assert.Equal(ProvisioningResult.Created, result);
        var updated = users.FindByUsername("operador")!;
        Assert.True(PasswordHasher.Verify("clave-nueva-2", updated.PasswordHash, updated.PasswordSalt, updated.Iterations));
        Assert.Null(updated.LockedUntil);
        Assert.Equal(0, updated.FailedLoginAttempts);
    }

    [Fact]
    public void Setting_a_password_for_a_user_that_does_not_exist_reports_it()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, _) = Build(db);

        Assert.Equal(ProvisioningResult.UserNotFound,
            provisioning.SetPassword("nadie", "clave-larga-1", "clave-larga-1"));
    }

    /// <summary>The first-run screen is only open while the app has no accounts at all; once one
    /// exists, account creation requires being logged in.</summary>
    [Fact]
    public void Reports_whether_the_app_still_has_no_accounts()
    {
        using var db = new SqliteTestDatabase();
        var (provisioning, _) = Build(db);

        Assert.True(provisioning.HasNoUsers());
        provisioning.Create("operador", "clave-larga-1", "clave-larga-1");
        Assert.False(provisioning.HasNoUsers());
    }
}
