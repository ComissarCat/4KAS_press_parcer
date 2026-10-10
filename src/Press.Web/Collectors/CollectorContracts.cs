using Press.Web.Domain;

namespace Press.Web.Collectors;

/// <summary>Публикация, найденная сборщиком.</summary>
public sealed record CollectedItem
{
    public required string Url { get; init; }
    public string? Title { get; init; }
    public string? Text { get; init; }
    public DateTime? PublishedAt { get; init; }
    public IReadOnlyList<string> OutLinks { get; init; } = [];
    /// <summary>Перепечатка/пересылка чужого материала.</summary>
    public bool IsRepost { get; init; }
    public string? RepostSource { get; init; }
    /// <summary>В <see cref="Text"/> только анонс из ленты, полный текст нужно загружать со страницы.</summary>
    public bool IsSummaryOnly { get; init; }
}

/// <param name="Resource">Ресурс, с которого собираем.</param>
/// <param name="KnownKeys">Ключи уже сохранённых ссылок — их не нужно загружать повторно.</param>
/// <param name="Since">Если задано — загрузить архив начиная с этой даты (UTC); иначе только свежие публикации.</param>
public sealed record CollectRequest(Resource Resource, IReadOnlySet<string> KnownKeys, DateTime? Since);

public interface ICollector
{
    CollectorKind Kind { get; }
    Task<IReadOnlyList<CollectedItem>> CollectAsync(CollectRequest request, CancellationToken ct);
}

public static class CollectorText
{
    /// <summary>Первая непустая строка текста — заголовок для публикаций без отдельного заголовка.</summary>
    public static string? FirstLine(string? text, int maxLength = 300)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var line = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        if (line == null) return null;
        return line.Length <= maxLength ? line : line[..maxLength].TrimEnd() + "…";
    }

    /// <summary>Схлопывает пробелы внутри строк и лишние пустые строки.</summary>
    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var lines = text.Replace("\r", "").Replace('\u00a0', ' ').Replace('\t', ' ')
            .Split('\n')
            .Select(l => string.Join(' ', l.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        var result = new List<string>();
        foreach (var l in lines)
        {
            if (l.Length == 0 && (result.Count == 0 || result[^1].Length == 0)) continue;
            result.Add(l);
        }
        return string.Join('\n', result).Trim();
    }
}
