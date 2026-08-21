namespace LicenciasCarpetas.Dashboard.Auth;

/// <summary>
/// Qué pantallas puede abrir cada cuenta. Administrador y Jefatura difieren solo en Usuarios;
/// Coordinador pierde además Importar Excel; Administrativo queda solo con Casos, y su acceso a
/// los módulos externos (Cambio de Domicilio, F8 Urgentes) se decide persona por persona, no por
/// el rol — dos Administrativos pueden ver módulos distintos.
/// </summary>
public enum UserRole
{
    Administrador,
    Jefatura,
    Coordinador,
    Administrativo
}

public static class UserRoleCatalog
{
    public static string Display(UserRole role) => role switch
    {
        UserRole.Administrador => "Administrador",
        UserRole.Jefatura => "Jefatura",
        UserRole.Coordinador => "Coordinador",
        UserRole.Administrativo => "Administrativo",
        _ => role.ToString()
    };

    /// <summary>Roles con acceso incondicional a los módulos externos — para Administrativo se
    /// decide aparte, por persona.</summary>
    public static bool HasFullModuleAccess(UserRole role) => role != UserRole.Administrativo;
}
