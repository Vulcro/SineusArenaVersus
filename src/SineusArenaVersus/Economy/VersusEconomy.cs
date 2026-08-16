using System;

namespace SineusArenaVersus.Economy;

public enum KillTier { Trash, Elite, Boss }

public sealed class VersusEconomy
{
    private readonly Func<int> _vpTrash;
    private readonly Func<int> _vpElite;
    private readonly Func<int> _vpBoss;
    private readonly int _passiveBase;
    private readonly int _passivePerSend;

    public VersusEconomy(int passiveBase, int passivePerSend,
        Func<int>? vpTrash = null, Func<int>? vpElite = null, Func<int>? vpBoss = null)
    {
        _passiveBase = passiveBase;
        _passivePerSend = passivePerSend;
        _vpTrash = vpTrash ?? (() => 1);
        _vpElite = vpElite ?? (() => 3);
        _vpBoss = vpBoss ?? (() => 15);
    }

    public int Vp { get; private set; }
    public int SuccessfulSends { get; private set; }
    public int PassiveAmountPerTick => _passiveBase + SuccessfulSends * _passivePerSend;

    public void AddKillVp(KillTier tier) => Vp += tier switch
    {
        KillTier.Elite => _vpElite(),
        KillTier.Boss => _vpBoss(),
        _ => _vpTrash()
    };

    public bool TrySpend(int cost)
    {
        if (cost < 0 || Vp < cost) return false;
        Vp -= cost;
        return true;
    }

    public void Refund(int amount) { if (amount > 0) Vp += amount; }
    public void OnPassiveTick() { if (PassiveAmountPerTick > 0) Vp += PassiveAmountPerTick; }
    public void RegisterSuccessfulSend() => SuccessfulSends++;
}
