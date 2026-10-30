using System.Text.Json;
using System.Text.Json.Serialization;
using Sharelink.Common;
using Sharelink.Settings;

namespace Sharelink.Upload;

/// <param name="Key">Identificador no destino (para poder apagar).</param>
/// <param name="Destination">Onde o arquivo está. Vazio = Cloudflare R2 (links criados antes da v3).</param>
internal sealed record LinkInfo(string Url, string Key, DateTime UploadedAt, UploadDestination? Destination = null)
{
    [JsonIgnore]
    public UploadDestination EffectiveDestination => Destination ?? UploadDestination.CloudflareR2;
}

/// <summary>Quais arquivos locais têm link, guardado em %AppData%\Sharelink\links.json.</summary>
internal sealed class LinkStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _path;
    private readonly Dictionary<string, LinkInfo> _links;

    public LinkStore(string path)
    {
        _path = path;
        _links = Load(path);
    }

    public static string DefaultPath => Path.Combine(SettingsStore.AppDataFolder, "links.json");

    /// <summary>Disparado depois de qualquer mudança (para a galeria se atualizar).</summary>
    public event Action<string>? Changed;

    public int Count => _links.Count;

    public LinkInfo? Get(string filePath) => _links.GetValueOrDefault(Normalize(filePath));

    public void Set(string filePath, LinkInfo info)
    {
        string key = Normalize(filePath);
        _links[key] = info;
        Save();
        Changed?.Invoke(key);
    }

    public void Remove(string filePath)
    {
        string key = Normalize(filePath);
        if (_links.Remove(key))
        {
            Save();
            Changed?.Invoke(key);
        }
    }

    private static string Normalize(string path) => Path.GetFullPath(path);

    /// <summary>Entradas adulteradas (link não-https ou identificador estranho) são ignoradas.</summary>
    private static bool IsTrustworthy(LinkInfo info) =>
        UploaderFactory.IsValidRemoteKey(info.EffectiveDestination, info.Key) &&
        Uri.TryCreate(info.Url, UriKind.Absolute, out Uri? url) && url.Scheme == Uri.UriSchemeHttps;

    private static Dictionary<string, LinkInfo> Load(string path)
    {
        var empty = new Dictionary<string, LinkInfo>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return empty;
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, LinkInfo>>(File.ReadAllText(path), JsonOptions);
            return data is null
                ? empty
                : new Dictionary<string, LinkInfo>(data.Where(kv => IsTrustworthy(kv.Value)), StringComparer.OrdinalIgnoreCase);
        }
        catch (JsonException ex)
        {
            Log.Error("links.json inválido", ex);
            File.Copy(path, path + ".invalido", overwrite: true);
            return empty;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_links, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }
}
