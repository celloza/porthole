using System.Text.Json;

namespace porthole_cli.State;

internal sealed record StoredContainerRecord(
    string Id,
    string? Name,
    string InspectJson,
    DateTimeOffset UpdatedAtUtc);

internal static class ContainerStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNamingPolicy = null,
    };

    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Porthole",
        "DevContainers",
        "containers.json");

    public static void Upsert(StoredContainerRecord record)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);

        var all = LoadAllInternal();
        int existingIndex = all.FindIndex(x => string.Equals(x.Id, record.Id, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            all[existingIndex] = record;
        }
        else
        {
            all.Add(record);
        }

        File.WriteAllText(StatePath, JsonSerializer.Serialize(all, JsonOptions));
    }

    public static bool TryGet(string idOrName, out StoredContainerRecord? record)
    {
        record = null;
        if (string.IsNullOrWhiteSpace(idOrName) || !File.Exists(StatePath))
        {
            return false;
        }

        string trimmed = idOrName.Trim();
        foreach (StoredContainerRecord item in LoadAllInternal())
        {
            if (string.Equals(item.Id, trimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(item.Name)
                    && string.Equals(item.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            {
                record = item;
                return true;
            }
        }

        return false;
    }

    public static List<StoredContainerRecord> GetAll()
    {
        return LoadAllInternal();
    }

    private static List<StoredContainerRecord> LoadAllInternal()
    {
        if (!File.Exists(StatePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(StatePath);
            return JsonSerializer.Deserialize<List<StoredContainerRecord>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
