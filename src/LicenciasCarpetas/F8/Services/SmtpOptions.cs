using System.ComponentModel.DataAnnotations;

namespace LicenciasCarpetas.F8.Services;

public sealed class SmtpOptions
{
    public const string SectionName = "Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    public bool EnableSsl { get; set; } = true;

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string FromAddress { get; set; } = string.Empty;

    public string FromDisplayName { get; set; } = "F8 Urgentes";
}
