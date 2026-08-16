using System;
using System.IO;
using System.Text;

namespace SineusArenaVersus.Net;

public static class VersusSerializer
{
    public static VersusOpcode GetOpcode(byte[] packet)
    {
        if (packet == null || packet.Length == 0)
            throw new ArgumentException("Packet is empty.", nameof(packet));
        return (VersusOpcode)packet[0];
    }

    public static byte[] Serialize(MatchStartMsg msg)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)VersusOpcode.MatchStart);
        writer.Write(msg.LobbyId);
        writer.Write(msg.WaveInterval);
        WriteUlongArray(writer, msg.Peers);
        return stream.ToArray();
    }

    public static MatchStartMsg DeserializeMatchStart(byte[] packet)
    {
        ExpectOpcode(packet, VersusOpcode.MatchStart);
        using var reader = CreatePayloadReader(packet);
        return new MatchStartMsg(
            reader.ReadUInt64(),
            reader.ReadSingle(),
            ReadUlongArray(reader));
    }

    public static byte[] Serialize(WaveTickMsg msg)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)VersusOpcode.WaveTick);
        writer.Write(msg.WaveIndex);
        writer.Write(msg.HostTime);
        return stream.ToArray();
    }

    public static WaveTickMsg DeserializeWaveTick(byte[] packet)
    {
        ExpectOpcode(packet, VersusOpcode.WaveTick);
        using var reader = CreatePayloadReader(packet);
        return new WaveTickMsg(reader.ReadInt32(), reader.ReadSingle());
    }

    public static byte[] Serialize(QueueSendMsg msg)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)VersusOpcode.QueueSend);
        writer.Write(msg.From);
        writer.Write(msg.To);
        WriteString(writer, msg.CatalogId);
        writer.Write(msg.Count);
        return stream.ToArray();
    }

    public static QueueSendMsg DeserializeQueueSend(byte[] packet)
    {
        ExpectOpcode(packet, VersusOpcode.QueueSend);
        using var reader = CreatePayloadReader(packet);
        return new QueueSendMsg(
            reader.ReadUInt64(),
            reader.ReadUInt64(),
            ReadString(reader),
            reader.ReadInt32());
    }

    public static byte[] Serialize(RivalSnapMsg msg)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)VersusOpcode.RivalSnap);
        writer.Write(msg.PeerId);
        writer.Write(msg.StrongholdHp01);
        writer.Write(msg.Alive);
        return stream.ToArray();
    }

    public static RivalSnapMsg DeserializeRivalSnap(byte[] packet)
    {
        ExpectOpcode(packet, VersusOpcode.RivalSnap);
        using var reader = CreatePayloadReader(packet);
        return new RivalSnapMsg(
            reader.ReadUInt64(),
            reader.ReadSingle(),
            reader.ReadBoolean());
    }

    public static byte[] SerializePeer(VersusOpcode opcode, PeerMsg msg)
    {
        if (opcode is not (VersusOpcode.StrongholdDown or VersusOpcode.Winner or VersusOpcode.Ready or VersusOpcode.Refund))
            throw new ArgumentOutOfRangeException(nameof(opcode), opcode, "Opcode is not a peer message.");

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write((byte)opcode);
        writer.Write(msg.PeerId);
        return stream.ToArray();
    }

    public static PeerMsg DeserializePeer(byte[] packet)
    {
        var opcode = GetOpcode(packet);
        if (opcode is not (VersusOpcode.StrongholdDown or VersusOpcode.Winner or VersusOpcode.Ready or VersusOpcode.Refund))
            throw new InvalidDataException($"Expected peer opcode, got {opcode}.");

        using var reader = CreatePayloadReader(packet);
        return new PeerMsg(reader.ReadUInt64());
    }

    private static BinaryReader CreatePayloadReader(byte[] packet) =>
        new(new MemoryStream(packet, 1, packet.Length - 1), Encoding.UTF8, leaveOpen: false);

    private static void ExpectOpcode(byte[] packet, VersusOpcode expected)
    {
        var actual = GetOpcode(packet);
        if (actual != expected)
            throw new InvalidDataException($"Expected opcode {expected}, got {actual}.");
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length < 0)
            throw new InvalidDataException("String length is negative.");

        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length)
            throw new EndOfStreamException("Unexpected end of packet while reading string.");

        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteUlongArray(BinaryWriter writer, ulong[]? peers)
    {
        var items = peers ?? Array.Empty<ulong>();
        writer.Write(items.Length);
        foreach (var peer in items)
            writer.Write(peer);
    }

    private static ulong[] ReadUlongArray(BinaryReader reader)
    {
        var count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Peer array length is negative.");

        var peers = new ulong[count];
        for (var i = 0; i < count; i++)
            peers[i] = reader.ReadUInt64();
        return peers;
    }
}
