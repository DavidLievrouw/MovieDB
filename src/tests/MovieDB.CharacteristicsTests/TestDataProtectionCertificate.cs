using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Hosting;

namespace MovieDB.CharacteristicsTests;

internal sealed class TestDataProtectionCertificate : IDisposable
{
    private const string Password = "test-password";
    private readonly string _path;

    private TestDataProtectionCertificate(string path) => _path = path;

    public static TestDataProtectionCertificate Create()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=MovieDB test key ring",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            TimeProvider.System.GetUtcNow().AddDays(-1),
            TimeProvider.System.GetUtcNow().AddDays(30)
        );
        File.WriteAllBytes(path, certificate.Export(X509ContentType.Pfx, Password));
        return new TestDataProtectionCertificate(path);
    }

    public void Configure(IWebHostBuilder builder)
    {
        builder
            .UseSetting("DataProtection:CertificatePath", _path)
            .UseSetting("DataProtection:CertificatePassword", Password);
    }

    public void Dispose()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
