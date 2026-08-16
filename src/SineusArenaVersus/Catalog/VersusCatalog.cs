using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace SineusArenaVersus.Catalog;

public sealed class VersusCatalog
{
    private const string EmbeddedResourceName = "SineusArenaVersus.Catalog.catalog.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly Dictionary<string, SendOffering> _byId;
    private readonly List<SendOffering> _all;

    private VersusCatalog(IEnumerable<SendOffering> offerings)
    {
        _byId = new Dictionary<string, SendOffering>(System.StringComparer.OrdinalIgnoreCase);
        _all = new List<SendOffering>();

        foreach (var offering in offerings)
        {
            if (string.IsNullOrWhiteSpace(offering.Id))
                throw new InvalidDataException("Offering id must not be empty.");

            if (offering.Cost <= 0)
                throw new InvalidDataException($"Offering '{offering.Id}' must have positive cost.");

            if (offering.Count <= 0)
                throw new InvalidDataException($"Offering '{offering.Id}' must have positive count.");

            if (_byId.ContainsKey(offering.Id))
                throw new InvalidDataException($"Duplicate offering id '{offering.Id}'.");

            _byId[offering.Id] = offering;
            _all.Add(offering);
        }
    }

    public IReadOnlyList<SendOffering> All => _all;

    public bool TryGet(string id, out SendOffering offering) =>
        _byId.TryGetValue(id, out offering!);

    public static VersusCatalog LoadFromEmbeddedDefault() =>
        LoadInternal(null);

    public static VersusCatalog Load()
    {
        var overridePath = VersusConfig.CatalogOverridePath?.Value;
        return LoadInternal(string.IsNullOrWhiteSpace(overridePath) ? null : overridePath);
    }

    public static VersusCatalog Load(string? overridePath) =>
        LoadInternal(overridePath);

    private static VersusCatalog LoadInternal(string? overridePath)
    {
        var json = !string.IsNullOrWhiteSpace(overridePath)
            ? ReadOverrideFile(overridePath!)
            : ReadEmbeddedDefault();

        var document = JsonSerializer.Deserialize<CatalogDocument>(json, JsonOptions)
            ?? throw new InvalidDataException("Catalog JSON was empty or invalid.");

        return new VersusCatalog(document.Offerings ?? new List<SendOffering>());
    }

    private static string ReadOverrideFile(string overridePath)
    {
        if (!File.Exists(overridePath))
            throw new FileNotFoundException($"Catalog override not found: {overridePath}", overridePath);

        return File.ReadAllText(overridePath);
    }

    private static string ReadEmbeddedDefault()
    {
        var assembly = typeof(VersusCatalog).Assembly;
        using var stream = assembly.GetManifestResourceStream(EmbeddedResourceName)
            ?? throw new InvalidDataException($"Embedded resource '{EmbeddedResourceName}' not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private sealed class CatalogDocument
    {
        public List<SendOffering> Offerings { get; set; } = new();
    }
}
