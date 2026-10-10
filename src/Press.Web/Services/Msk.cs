using System.Globalization;

namespace Press.Web.Services;

/// <summary>
/// Московское время (UTC+3, без перехода на летнее время с 2014 г.).
/// Используется фиксированное смещение, а не часовой пояс ОС: на Windows Server 2019 нет ICU,
/// и IANA-идентификаторы там работают ненадёжно.
/// </summary>
public static class Msk
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);
    public static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static DateTime ToMsk(this DateTime utc) => DateTime.SpecifyKind(utc + Offset, DateTimeKind.Unspecified);

    public static DateTime ToUtcFromMsk(DateTime msk) => DateTime.SpecifyKind(msk - Offset, DateTimeKind.Utc);

    public static DateTime Now => DateTime.UtcNow.ToMsk();

    public static string Format(DateTime? utc, string format = "dd.MM.yyyy HH:mm") =>
        utc.HasValue ? utc.Value.ToMsk().ToString(format, Ru) : "";

    public static string FormatDate(DateTime? utc) => Format(utc, "dd.MM.yyyy");
}
