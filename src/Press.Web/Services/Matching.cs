namespace Press.Web.Services;

/// <summary>
/// Разбиение текста на «основы» слов: нижний регистр, ё→е, отсечение окончания и первые 6 символов
/// («суда», «суду», «судом» → «суд»; «кассационного» → «кассац»). Числа сохраняются целиком.
/// </summary>
public static class Stemmer
{
    public const int StemLength = 6;

    // от длинных к коротким, чтобы «-ого» отсекалось раньше «-о»
    private static readonly string[] Endings =
    [
        "ами", "ями", "ого", "его", "ому", "ему", "ыми", "ими", "ых", "их",
        "ах", "ях", "ов", "ев", "ом", "ем", "ой", "ей", "ую", "юю", "ая", "яя", "ое", "ее", "ые", "ие", "ый", "ий", "ам", "ям",
        "а", "я", "о", "е", "у", "ю", "ы", "и", "ь", "й",
    ];

    private static string Stem(string word)
    {
        foreach (var e in Endings)
            if (word.Length - e.Length >= 3 && word.EndsWith(e, StringComparison.Ordinal))
            {
                word = word[..^e.Length];
                break;
            }
        return word.Length <= StemLength ? word : word[..StemLength];
    }

    /// <summary>Основы слов по порядку (для поиска фраз).</summary>
    public static List<string> Sequence(string? text)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;
        var lower = text.ToLowerInvariant().Replace('ё', 'е');
        var start = -1;
        for (var i = 0; i <= lower.Length; i++)
        {
            var isWordChar = i < lower.Length && char.IsLetterOrDigit(lower[i]);
            if (isWordChar)
            {
                if (start < 0) start = i;
                continue;
            }
            if (start < 0) continue;
            var w = lower[start..i];
            start = -1;
            var isNumber = w.All(char.IsDigit);
            if (isNumber) result.Add(w);
            else if (w.Length >= 3) result.Add(Stem(w));
        }
        return result;
    }

    public static HashSet<string> Set(string? text) => Sequence(text).ToHashSet();
}

/// <summary>Ключевые фразы: совпадение по основам слов, подряд, без учёта падежей («Четвертого кассационного» = «Четвертый кассационный»).</summary>
public sealed class KeywordMatcher
{
    private readonly List<(string Phrase, List<string> Stems)> _phrases;

    public KeywordMatcher(IEnumerable<string> phrases) =>
        _phrases = phrases.Select(p => p.Trim()).Where(p => p.Length > 0)
            .Select(p => (p, Stemmer.Sequence(p))).Where(x => x.Item2.Count > 0).ToList();

    public static readonly string[] Defaults =
    [
        "Четвертый кассационный суд",
        "4-й кассационный суд",
        "4 КСОЮ",
        "4КСОЮ",
        "Четвертый КСОЮ",
    ];

    /// <summary>Первая найденная фраза или null.</summary>
    public string? FindIn(string? text)
    {
        var seq = Stemmer.Sequence(text);
        foreach (var (phrase, stems) in _phrases)
            for (var i = 0; i + stems.Count <= seq.Count; i++)
                if (!stems.Where((s, j) => seq[i + j] != s).Any())
                    return phrase;
        return null;
    }
}

/// <summary>Собственный материал в индексе для сопоставления.</summary>
public sealed record IndexedMaterial(int Id, string Title, DateTime PublishedAt, HashSet<string> Stems, HashSet<string> LinkKeys);

/// <param name="Score">Итоговая похожесть 0..1 (взвешенный коэффициент Дайса).</param>
/// <param name="ItemCoverage">Какая доля «веса» публикации приходится на общие слова (для отбора по анонсу).</param>
public sealed record MatchResult(IndexedMaterial Material, double Score, double ItemCoverage, int CommonCount, string Info);

/// <summary>
/// Индекс собственных материалов. Слова взвешиваются по редкости (idf), которая считается по нашим материалам
/// и по потоку публикаций отслеживаемых СМИ: общеупотребительные слова и «суд», «Краснодар» почти ничего не весят,
/// а фамилии, названия, суммы и номера — много. Похожесть — взвешенный коэффициент Дайса:
/// 2·Σw(общих) / (Σw(материала) + Σw(публикации)); он мал, если совпала лишь малая часть одного из текстов.
/// </summary>
public sealed class MaterialIndex
{
    /// <summary>Минимум общих значимых слов, чтобы вообще считать публикации похожими.</summary>
    public const int MinCommon = 5;

    /// <summary>Публикация не может выйти раньше нашего материала (с запасом на часовые пояса и неточные даты).</summary>
    private static readonly TimeSpan MaxEarlier = TimeSpan.FromDays(2);
    private static readonly TimeSpan MaxLater = TimeSpan.FromDays(60);

    private readonly List<(IndexedMaterial M, double Weight)> _materials;
    private readonly Dictionary<string, int> _df = new();
    private readonly int _docs;

    /// <param name="materials">Собственные материалы для сопоставления.</param>
    /// <param name="background">Дополнительные тексты (публикации СМИ) — только для оценки редкости слов.</param>
    public MaterialIndex(IEnumerable<IndexedMaterial> materials, IEnumerable<HashSet<string>>? background = null)
    {
        var list = materials.ToList();
        foreach (var stems in list.Select(m => m.Stems).Concat(background ?? []))
        {
            _docs++;
            foreach (var s in stems) _df[s] = _df.GetValueOrDefault(s) + 1;
        }
        _materials = list.Select(m => (m, m.Stems.Sum(Idf))).ToList();
    }

    public int Count => _materials.Count;

    public double Idf(string stem) => Math.Log((_docs + 1.0) / (_df.GetValueOrDefault(stem) + 0.5));

    public MatchResult? Best(string? title, string? text, DateTime? publishedAt, IEnumerable<string>? linkKeys = null)
    {
        var keys = linkKeys?.ToHashSet() ?? [];
        var stems = Stemmer.Set(title + "\n" + text);
        var itemWeight = stems.Sum(Idf);
        MatchResult? best = null;

        foreach (var (m, materialWeight) in _materials)
        {
            if (publishedAt is { } p && (p < m.PublishedAt - MaxEarlier || p > m.PublishedAt + MaxLater)) continue;

            if (keys.Overlaps(m.LinkKeys))
                return new MatchResult(m, 1.0, 1.0, 0, "ссылается на наш материал");

            if (itemWeight <= 0 || materialWeight <= 0) continue;
            var common = stems.Where(m.Stems.Contains).ToList();
            if (common.Count < MinCommon) continue;
            var commonWeight = common.Sum(Idf);
            var score = 2 * commonWeight / (itemWeight + materialWeight);
            if (best == null || score > best.Score)
                best = new MatchResult(m, Math.Min(score, 1.0), commonWeight / itemWeight, common.Count,
                    "общие слова: " + string.Join(", ", common.OrderByDescending(Idf).Take(12)));
        }
        return best;
    }
}
