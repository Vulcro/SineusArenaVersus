using SineusArenaVersus.Net;
using Xunit;

namespace SineusArenaVersus.Tests;

public class SerializerTests
{
    [Fact]
    public void QueueSendMsg_round_trips()
    {
        var original = new QueueSendMsg(1001UL, 2002UL, "swarm_cheap", 5);
        var bytes = VersusSerializer.Serialize(original);
        var restored = VersusSerializer.DeserializeQueueSend(bytes);
        Assert.Equal(original, restored);
        Assert.Equal(VersusOpcode.QueueSend, VersusSerializer.GetOpcode(bytes));
    }

    [Fact]
    public void MatchStartMsg_round_trips()
    {
        var original = new MatchStartMsg(42UL, 20f, new[] { 1UL, 2UL, 3UL });
        var bytes = VersusSerializer.Serialize(original);
        var restored = VersusSerializer.DeserializeMatchStart(bytes);
        Assert.Equal(original.LobbyId, restored.LobbyId);
        Assert.Equal(original.WaveInterval, restored.WaveInterval);
        Assert.Equal(original.Peers, restored.Peers);
        Assert.Equal(VersusOpcode.MatchStart, VersusSerializer.GetOpcode(bytes));
    }

    [Fact]
    public void WaveTickMsg_round_trips()
    {
        var original = new WaveTickMsg(7, 123.45f);
        var bytes = VersusSerializer.Serialize(original);
        var restored = VersusSerializer.DeserializeWaveTick(bytes);
        Assert.Equal(original, restored);
        Assert.Equal(VersusOpcode.WaveTick, VersusSerializer.GetOpcode(bytes));
    }

    [Fact]
    public void RivalSnapMsg_round_trips()
    {
        var original = new RivalSnapMsg(999UL, 0.75f, true);
        var bytes = VersusSerializer.Serialize(original);
        var restored = VersusSerializer.DeserializeRivalSnap(bytes);
        Assert.Equal(original, restored);
        Assert.Equal(VersusOpcode.RivalSnap, VersusSerializer.GetOpcode(bytes));
    }

    [Fact]
    public void VpReportMsg_round_trips()
    {
        var original = new VpReportMsg(999UL, 42);
        var bytes = VersusSerializer.Serialize(original);
        var restored = VersusSerializer.DeserializeVpReport(bytes);
        Assert.Equal(original, restored);
        Assert.Equal(VersusOpcode.VpReport, VersusSerializer.GetOpcode(bytes));
    }

    [Theory]
    [InlineData(VersusOpcode.StrongholdDown)]
    [InlineData(VersusOpcode.Winner)]
    [InlineData(VersusOpcode.Ready)]
    [InlineData(VersusOpcode.Refund)]
    public void PeerMsg_round_trips(VersusOpcode opcode)
    {
        var original = new PeerMsg(555UL);
        var bytes = VersusSerializer.SerializePeer(opcode, original);
        var restored = VersusSerializer.DeserializePeer(bytes);
        Assert.Equal(original, restored);
        Assert.Equal(opcode, VersusSerializer.GetOpcode(bytes));
    }

    [Fact]
    public void Opcodes_match_spec()
    {
        Assert.Equal(1, (byte)VersusOpcode.MatchStart);
        Assert.Equal(2, (byte)VersusOpcode.WaveTick);
        Assert.Equal(3, (byte)VersusOpcode.QueueSend);
        Assert.Equal(4, (byte)VersusOpcode.RivalSnap);
        Assert.Equal(5, (byte)VersusOpcode.StrongholdDown);
        Assert.Equal(6, (byte)VersusOpcode.Winner);
        Assert.Equal(7, (byte)VersusOpcode.Ready);
        Assert.Equal(8, (byte)VersusOpcode.Refund);
        Assert.Equal(9, (byte)VersusOpcode.VpReport);
    }
}
