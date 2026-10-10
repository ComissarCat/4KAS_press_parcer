using Press.Web.Domain;

namespace Press.Web.Components.Shared;

public static class Labels
{
    public static string Of(ResourceKind kind) => kind switch
    {
        ResourceKind.Website => "Сайт",
        ResourceKind.Telegram => "Telegram",
        ResourceKind.Vk => "ВКонтакте",
        ResourceKind.Max => "MAX",
        _ => "Другое",
    };

    public static string Of(CollectorKind kind) => kind switch
    {
        CollectorKind.None => "Нет (ручной ввод)",
        CollectorKind.SudrfPress => "Сайт суда sudrf.ru — пресс-служба",
        CollectorKind.TelegramPublic => "Telegram — публичный канал",
        CollectorKind.VkWall => "ВКонтакте — стена сообщества (API)",
        CollectorKind.Rss => "RSS-лента",
        CollectorKind.HtmlList => "HTML-страница со списком публикаций",
        _ => kind.ToString(),
    };

    public static string Of(RelationStatus s) => s switch
    {
        RelationStatus.Candidate => "На проверке",
        RelationStatus.Confirmed => "Подтверждено",
        RelationStatus.Rejected => "Отклонено",
        _ => s.ToString(),
    };

    public static string CollectorTargetHint(CollectorKind kind) => kind switch
    {
        CollectorKind.SudrfPress => "Адрес сайта суда, например https://4kas.sudrf.ru",
        CollectorKind.TelegramPublic => "Имя канала, например sud_kas_4",
        CollectorKind.VkWall => "Короткое имя или id сообщества, например 4kassud или -217666653",
        CollectorKind.Rss => "Адрес ленты, например https://rg.ru/xml/index.xml",
        CollectorKind.HtmlList => "Адрес страницы со списком публикаций, например https://kuban.bfm.ru/",
        _ => "",
    };
}
