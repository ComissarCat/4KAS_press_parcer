namespace Press.Web.Services;

/// <summary>Похожесть заголовков: доля общих основ слов (коэффициент Жаккара).</summary>
public static class TitleSimilarity
{
    public static double Score(string? a, string? b)
    {
        var x = Stemmer.Set(a);
        var y = Stemmer.Set(b);
        if (x.Count == 0 || y.Count == 0) return 0;
        var common = x.Count(y.Contains);
        return (double)common / (x.Count + y.Count - common);
    }
}
