using System;
using System.Collections.Generic;
using System.IO;
using SineusArenaVersus.Catalog;

namespace SineusArenaVersus.Game;

public sealed class EnemyKeyResolver
{
    private readonly Dictionary<string, string> _spawnIds;

    public EnemyKeyResolver(IEnumerable<KeyValuePair<string, string>> mappings)
    {
        _spawnIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key) || string.IsNullOrWhiteSpace(mapping.Value))
                continue;

            if (_spawnIds.ContainsKey(mapping.Key))
                throw new InvalidDataException($"Duplicate enemy key '{mapping.Key}'.");

            _spawnIds.Add(mapping.Key, mapping.Value);
        }
    }

    public bool TryResolve(string enemyKey, out string spawnId)
    {
        if (string.IsNullOrWhiteSpace(enemyKey))
        {
            spawnId = "";
            return false;
        }

        return _spawnIds.TryGetValue(enemyKey, out spawnId!);
    }

    public static EnemyKeyResolver FromOfferings(IEnumerable<SendOffering> offerings)
    {
        if (offerings is null)
            throw new ArgumentNullException(nameof(offerings));

        var mappings = new List<KeyValuePair<string, string>>();
        foreach (var offering in offerings)
            mappings.Add(new KeyValuePair<string, string>(offering.EnemyKey, offering.SpawnId));

        return new EnemyKeyResolver(mappings);
    }
}
