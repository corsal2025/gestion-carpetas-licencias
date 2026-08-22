namespace LicenciasCarpetas.F8.Services;

/// <summary>A file to attach to an outgoing email — FilePath must point at a file already saved
/// on disk (the caller owns cleanup); kept as a path rather than bytes so large attachments never
/// need to be fully loaded into memory just to pass through.</summary>
public sealed record EmailAttachment(string FileName, string ContentType, string FilePath);
