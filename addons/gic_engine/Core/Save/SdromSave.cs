using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GicEngine.Runtime;

namespace GicEngine.Save;

/// <summary>Тип сохранения — как в Dishonored: автоматические, быстрое и ручные без ограничения количества.</summary>
public enum SaveKind
{
    Auto,
    Quick,
    Manual,
}

/// <summary>Заголовок сохранения: всё, что нужно списку сохранений, без распаковки самого прохождения.</summary>
public sealed class SaveInfo
{
    public SaveKind Kind;
    public string Title = "";
    public string Chapter = "";
    public string LastText = "";
    public DateTime Created;
    public double PlayTime;
    public string StoryId = "";
    public string StoryTitle = "";
    public string GameVersion = "";
    // Полный путь к файлу (заполняется при чтении, в файл не пишется).
    public string FilePath;
    // Миниатюра экрана в PNG (может отсутствовать).
    public byte[] Thumbnail;
}

/// <summary>
/// Формат файла сохранения .sdrom_save (ответ 57).
/// Устройство файла (все числа little-endian):
///   "SDROMSAV"                    — 8 байт, сигнатура формата;
///   u16 версия формата;
///   u32 длина + JSON заголовка    — тип, название, глава, дата, время игры (для списка сохранений);
///   u32 длина + PNG миниатюры     — может быть 0;
///   u32 длина + Deflate(JSON)     — само прохождение (RuntimeState);
///   32 байта SHA-256              — контрольная сумма прохождения, чтобы заметить повреждённый файл.
/// Заголовок читается отдельно, поэтому список из сотни сохранений открывается быстро.
/// </summary>
public static class SdromSave
{
    public const string Extension = ".sdrom_save";
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SDROMSAV");
    private const ushort FormatVersion = 1;

    /// <summary>Записывает сохранение в файл. Пишем во временный файл и переименовываем — чтобы сбой не испортил старое сохранение.</summary>
    public static void Write(string path, SaveInfo info, RuntimeState state)
    {
        var body = Encoding.UTF8.GetBytes(state.ToJson().ToJsonString());
        var header = Encoding.UTF8.GetBytes(HeaderToJson(info).ToJsonString());
        var thumb = info.Thumbnail ?? Array.Empty<byte>();

        var tmp = path + ".tmp";
        using (var fs = File.Create(tmp))
        using (var w = new BinaryWriter(fs))
        {
            w.Write(Magic);
            w.Write(FormatVersion);
            WriteBlock(w, header);
            WriteBlock(w, thumb);
            WriteBlock(w, Compress(body));
            w.Write(SHA256.HashData(body));
        }
        File.Move(tmp, path, true);
    }

    /// <summary>Читает только заголовок и миниатюру — для списка сохранений.</summary>
    public static SaveInfo ReadInfo(string path)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);
        CheckMagic(r);
        var info = HeaderFromJson(JsonNode.Parse(Encoding.UTF8.GetString(ReadBlock(r))) as JsonObject);
        info.Thumbnail = ReadBlock(r);
        if (info.Thumbnail.Length == 0) info.Thumbnail = null;
        info.FilePath = path;
        return info;
    }

    /// <summary>Читает прохождение целиком. Бросает InvalidDataException, если файл повреждён.</summary>
    public static RuntimeState ReadState(string path)
    {
        using var fs = File.OpenRead(path);
        using var r = new BinaryReader(fs);
        CheckMagic(r);
        ReadBlock(r); // заголовок
        ReadBlock(r); // миниатюра
        var body = Decompress(ReadBlock(r));
        var hash = r.ReadBytes(32);
        if (!hash.AsSpan().SequenceEqual(SHA256.HashData(body)))
        {
            throw new InvalidDataException("Контрольная сумма не совпала: файл сохранения повреждён");
        }
        return RuntimeState.FromJson(JsonNode.Parse(Encoding.UTF8.GetString(body)) as JsonObject);
    }

    private static void CheckMagic(BinaryReader r)
    {
        var magic = r.ReadBytes(Magic.Length);
        if (!magic.AsSpan().SequenceEqual(Magic)) throw new InvalidDataException("Это не файл сохранения GoliathChild:RE");
        var version = r.ReadUInt16();
        if (version > FormatVersion) throw new InvalidDataException($"Сохранение из более новой версии игры (формат {version})");
    }

    private static void WriteBlock(BinaryWriter w, byte[] data)
    {
        w.Write((uint)data.Length);
        w.Write(data);
    }

    private static byte[] ReadBlock(BinaryReader r)
    {
        var length = r.ReadUInt32();
        // Защита от мусора: блок не может быть больше 256 МБ.
        if (length > 256 * 1024 * 1024) throw new InvalidDataException("Повреждённый блок в файле сохранения");
        return r.ReadBytes((int)length);
    }

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, true)) deflate.Write(data);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var deflate = new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        deflate.CopyTo(output);
        return output.ToArray();
    }

    private static JsonObject HeaderToJson(SaveInfo i) => new()
    {
        ["kind"] = i.Kind.ToString().ToLowerInvariant(),
        ["title"] = i.Title,
        ["chapter"] = i.Chapter,
        ["last_text"] = i.LastText,
        ["created"] = i.Created.ToString("o"),
        ["playtime"] = i.PlayTime,
        ["story"] = i.StoryId,
        ["story_title"] = i.StoryTitle,
        ["game_version"] = i.GameVersion,
    };

    private static SaveInfo HeaderFromJson(JsonObject o)
    {
        var info = new SaveInfo();
        if (o == null) return info;
        info.Kind = (o["kind"]?.GetValue<string>()) switch
        {
            "auto" => SaveKind.Auto,
            "quick" => SaveKind.Quick,
            _ => SaveKind.Manual,
        };
        info.Title = o["title"]?.GetValue<string>() ?? "";
        info.Chapter = o["chapter"]?.GetValue<string>() ?? "";
        info.LastText = o["last_text"]?.GetValue<string>() ?? "";
        info.StoryId = o["story"]?.GetValue<string>() ?? "";
        info.StoryTitle = o["story_title"]?.GetValue<string>() ?? "";
        info.GameVersion = o["game_version"]?.GetValue<string>() ?? "";
        info.PlayTime = o["playtime"]?.GetValue<double>() ?? 0;
        DateTime.TryParse(o["created"]?.GetValue<string>(), null, System.Globalization.DateTimeStyles.RoundtripKind, out info.Created);
        return info;
    }
}
