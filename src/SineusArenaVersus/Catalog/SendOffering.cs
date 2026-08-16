namespace SineusArenaVersus.Catalog;

public sealed class SendOffering
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public int Cost { get; set; }
    public string EnemyKey { get; set; } = "";
    public string SpawnId { get; set; } = "";
    public int Count { get; set; }
}
