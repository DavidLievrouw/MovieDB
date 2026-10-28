using System.Security.Cryptography.X509Certificates;

namespace MovieDB.Web;

/// <summary>
/// Settings for the X.509 certificate that encrypts the Data Protection key ring before it is written to the database.
/// The certificate must be stored outside the database (file or secret mount) so a database copy alone is not enough
/// to recover the keys that protect the authentication cookie.
/// </summary>
internal sealed class KeyRingProtectionOptions
{
    public const string Section = "DataProtection";

    /// <summary>Path to a PKCS#12 (.pfx) file containing the certificate and its private key.</summary>
    public string? CertificatePath { get; set; }

    /// <summary>Password of the PKCS#12 file, if any.</summary>
    public string? CertificatePassword { get; set; }
}

internal static class KeyRingProtection
{
    /// <summary>
    /// Loads the key-ring protection certificate. Fails closed when none is configured, except in development
    /// where the key ring may remain unencrypted for local use.
    /// </summary>
    /// <param name="options">The key-ring protection options.</param>
    /// <param name="allowUnprotected">Whether running without a certificate is allowed (development).</param>
    /// <exception cref="InvalidOperationException">
    /// No certificate is configured while one is required, the certificate file does not exist, or the certificate
    /// lacks an RSA public and private key.
    /// </exception>
    public static X509Certificate2? LoadCertificate(KeyRingProtectionOptions options, bool allowUnprotected)
    {
        if (string.IsNullOrWhiteSpace(options.CertificatePath))
        {
            return allowUnprotected
                ? null
                : throw new InvalidOperationException(
                    $"{KeyRingProtectionOptions.Section}:{nameof(KeyRingProtectionOptions.CertificatePath)} must be configured "
                        + "so the Data Protection key ring is encrypted at rest."
                );
        }

        if (!File.Exists(options.CertificatePath))
        {
            throw new InvalidOperationException(
                $"The Data Protection key-ring certificate file '{options.CertificatePath}' does not exist."
            );
        }

        var certificate = X509CertificateLoader.LoadPkcs12FromFile(
            options.CertificatePath,
            string.IsNullOrEmpty(options.CertificatePassword) ? null : options.CertificatePassword
        );

        using var publicKey = certificate.GetRSAPublicKey();
        using var privateKey = certificate.GetRSAPrivateKey();
        if (publicKey is null || privateKey is null)
        {
            certificate.Dispose();
            throw new InvalidOperationException(
                "The Data Protection key-ring certificate must contain an RSA public and private key."
            );
        }

        return certificate;
    }
}
