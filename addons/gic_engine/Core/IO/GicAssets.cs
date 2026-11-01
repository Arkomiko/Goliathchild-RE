using System.Collections.Generic;
using Godot;

namespace GicEngine.IO;

/// <summary>
/// Загрузка картинок и звуков по пути из сценария (уже разрешённому через StoryProject.ResolveAsset).
/// Встроенные ассеты берутся из res:// через импорт Godot, файлы фанатских историй — напрямую с диска.
/// Результат кэшируется: одна и та же картинка в окне просмотра не грузится заново.
/// </summary>
public static class GicAssets
{
    private static readonly Dictionary<string, Texture2D> Textures = new();
    private static readonly Dictionary<string, AudioStream> Audio = new();

    /// <summary>Картинка или null, если файла нет.</summary>
    public static Texture2D LoadTexture(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Textures.TryGetValue(path, out var cached)) return cached;

        Texture2D texture = null;
        if (path.StartsWith("res://"))
        {
            if (ResourceLoader.Exists(path)) texture = GD.Load<Texture2D>(path);
        }
        else if (FileAccess.FileExists(path))
        {
            var image = Image.LoadFromFile(path);
            if (image != null) texture = ImageTexture.CreateFromImage(image);
        }
        if (texture == null) GD.PushWarning($"Картинка не найдена: {path}");
        Textures[path] = texture;
        return texture;
    }

    /// <summary>Звук или null. loop включает зацикливание у копии потока, чтобы не менять общий ресурс.</summary>
    public static AudioStream LoadAudio(string path, bool loop)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (!Audio.TryGetValue(path, out var stream))
        {
            if (path.StartsWith("res://"))
            {
                if (ResourceLoader.Exists(path)) stream = GD.Load<AudioStream>(path);
            }
            else if (FileAccess.FileExists(path) && path.GetExtension().ToLowerInvariant() == "ogg")
            {
                stream = AudioStreamOggVorbis.LoadFromFile(path);
            }
            if (stream == null) GD.PushWarning($"Звук не найден или формат не поддерживается: {path}");
            Audio[path] = stream;
        }
        if (stream == null) return null;

        // Один и тот же файл может играть и как зацикленная музыка, и как разовый звук.
        var copy = (AudioStream)stream.Duplicate();
        switch (copy)
        {
            case AudioStreamOggVorbis ogg: ogg.Loop = loop; break;
            case AudioStreamMP3 mp3: mp3.Loop = loop; break;
            case AudioStreamWav wav: wav.LoopMode = loop ? AudioStreamWav.LoopModeEnum.Forward : AudioStreamWav.LoopModeEnum.Disabled; break;
        }
        return copy;
    }

    /// <summary>Сбросить кэш (редактор вызывает после замены файлов ассетов).</summary>
    public static void ClearCache()
    {
        Textures.Clear();
        Audio.Clear();
    }
}
