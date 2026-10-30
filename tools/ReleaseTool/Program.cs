using System.Drawing.Imaging;
using Sharelink.Common;
using Sharelink.Update;
using Sharelink.Upload;

// Uso:
//   keygen <UpdatePublicKey.cs>        cria o par de chaves (privada no Gerenciador de Credenciais) e grava a pública no código
//   sign <instalador.exe> <versão>     cria <instalador.exe>.sig
//   export-key <arquivo>               exporta a chave privada para backup (guarde em lugar seguro!)
//   import-key <arquivo>               restaura a chave privada de um backup
//   icon <saida.ico>                   gera o ícone do app
//   check-release                      baixa a release mais recente do GitHub e confere a assinatura (igual ao app)
const string KeyTarget = "Sharelink/ReleaseSigningKey";

try
{
    return args switch
    {
        ["keygen", string output] => KeyGen(output),
        ["sign", string file, string version] => await Sign(file, version),
        ["export-key", string file] => ExportKey(file),
        ["import-key", string file] => ImportKey(file),
        ["icon", string output] => Icon(output),
        ["check-release"] => await CheckRelease(),
        _ => Usage(),
    };
}
catch (Exception ex)
{
    Console.Error.WriteLine($"ERRO: {ex.Message}");
    return 1;
}

static int Usage()
{
    Console.Error.WriteLine("Uso: ReleaseTool keygen <UpdatePublicKey.cs> | sign <arquivo> <versão> | export-key <arquivo> | import-key <arquivo> | icon <saida.ico>");
    return 2;
}

static int KeyGen(string output)
{
    if (CredentialStore.Load(KeyTarget) is not null)
        throw new InvalidOperationException("já existe uma chave de assinatura neste PC. Trocar a chave impede as versões instaladas de aceitarem atualizações.");

    (string privateKey, string publicKey) = UpdateSignature.CreateKeyPair();
    CredentialStore.Save(KeyTarget, "release-signing", privateKey);
    File.WriteAllText(output, $$"""
        namespace Sharelink.Update;

        /// <summary>
        /// Chave PÚBLICA que verifica as atualizações. Gerada por tools/ReleaseTool (keygen).
        /// A chave privada correspondente fica só no Gerenciador de Credenciais de quem publica.
        /// </summary>
        internal static class UpdatePublicKey
        {
            public const string Value = "{{publicKey}}";
        }

        """);
    Console.WriteLine("Chave criada. Privada: Gerenciador de Credenciais do Windows (Sharelink/ReleaseSigningKey).");
    Console.WriteLine($"Pública gravada em {output}.");
    Console.WriteLine("IMPORTANTE: faça um backup com 'export-key' e guarde num lugar seguro (ex.: gerenciador de senhas).");
    return 0;
}

static async Task<int> Sign(string file, string version)
{
    if (!AppVersion.TryParse(version, out Version parsed))
        throw new InvalidOperationException("versão inválida (use X.Y.Z).");
    var key = CredentialStore.Load(KeyTarget) ?? throw new InvalidOperationException("chave de assinatura não encontrada neste PC (rode keygen ou import-key).");

    string versionText = AppVersion.Format(parsed);
    string hash = await UpdateSignature.FileSha256Async(file);
    string signature = UpdateSignature.Sign(key.Secret, versionText, hash);
    if (!UpdateSignature.Verify(UpdatePublicKey.Value, versionText, hash, signature))
        throw new InvalidOperationException("a chave privada deste PC não corresponde à chave pública do app.");

    File.WriteAllText(file + ".sig", UpdateSignature.Serialize(new SignatureFile(versionText, hash, signature)));
    Console.WriteLine($"Assinado: {file}.sig (SHA-256 {hash})");
    return 0;
}

static async Task<int> CheckRelease()
{
    ReleaseInfo release = await GitHubUpdateClient.GetLatestAsync(CancellationToken.None)
                          ?? throw new InvalidOperationException("nenhuma release publicada (ou faltam o instalador/.sig).");
    string dir = Path.Combine(Path.GetTempPath(), "sharelink-check-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        string installer = Path.Combine(dir, GitHubUpdateClient.InstallerName(release.VersionText));
        await GitHubUpdateClient.DownloadAsync(release.SignatureUrl, installer + ".sig", 16 * 1024, CancellationToken.None);
        await GitHubUpdateClient.DownloadAsync(release.InstallerUrl, installer, 400L * 1024 * 1024, CancellationToken.None);
        SignatureFile sig = UpdateSignature.Parse(File.ReadAllText(installer + ".sig")) ?? throw new InvalidOperationException(".sig inválido");
        string hash = await UpdateSignature.FileSha256Async(installer);
        bool ok = sig.Version == release.VersionText && hash == sig.Sha256 &&
                  UpdateSignature.Verify(UpdatePublicKey.Value, release.VersionText, hash, sig.Signature);
        Console.WriteLine($"Release {release.VersionText}: {new FileInfo(installer).Length / 1024 / 1024} MB, assinatura {(ok ? "VÁLIDA" : "INVÁLIDA")}");
        return ok ? 0 : 1;
    }
    finally
    {
        Directory.Delete(dir, recursive: true);
    }
}

static int ExportKey(string file)
{
    var key = CredentialStore.Load(KeyTarget) ?? throw new InvalidOperationException("nenhuma chave neste PC.");
    File.WriteAllText(file, key.Secret);
    Console.WriteLine($"Chave privada exportada para {file}. Guarde em local seguro e apague este arquivo depois.");
    return 0;
}

static int ImportKey(string file)
{
    string key = File.ReadAllText(file).Trim();
    UpdateSignature.Sign(key, "0.0.0", new string('0', 64)); // valida o formato
    CredentialStore.Save(KeyTarget, "release-signing", key);
    Console.WriteLine("Chave importada para o Gerenciador de Credenciais.");
    return 0;
}

/// <summary>Arquivo .ico com várias resoluções (entradas em PNG), desenhado pelo próprio código do app.</summary>
static int Icon(string output)
{
    int[] sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
    var images = sizes.Select(size =>
    {
        using Bitmap bmp = AppIcon.Render(size);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }).ToList();

    using var file = new BinaryWriter(File.Create(output));
    file.Write((short)0);
    file.Write((short)1);
    file.Write((short)sizes.Length);
    int offset = 6 + 16 * sizes.Length;
    for (int i = 0; i < sizes.Length; i++)
    {
        file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        file.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
        file.Write((byte)0);
        file.Write((byte)0);
        file.Write((short)1);
        file.Write((short)32);
        file.Write(images[i].Length);
        file.Write(offset);
        offset += images[i].Length;
    }
    foreach (byte[] image in images)
        file.Write(image);
    Console.WriteLine($"Ícone gerado: {output}");
    return 0;
}
