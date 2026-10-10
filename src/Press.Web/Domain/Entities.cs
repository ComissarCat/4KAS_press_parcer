namespace Press.Web.Domain;

/// <summary>Тип площадки, на которой публикуются материалы.</summary>
public enum ResourceKind
{
    Website = 0,
    Telegram = 1,
    Vk = 2,
    Max = 3,
    Other = 99,
}

/// <summary>Способ автоматического сбора публикаций с ресурса.</summary>
public enum CollectorKind
{
    None = 0,
    /// <summary>Раздел «Пресс-служба» сайта суда на платформе sudrf.ru (ГАС «Правосудие»).</summary>
    SudrfPress = 1,
    /// <summary>Публичная веб-версия Telegram-канала (t.me/s/канал).</summary>
    TelegramPublic = 2,
    /// <summary>Стена сообщества ВКонтакте через официальный API (wall.get).</summary>
    VkWall = 3,
    /// <summary>RSS-лента сайта.</summary>
    Rss = 4,
    /// <summary>HTML-страница со списком публикаций (ссылки выбираются CSS-селектором).</summary>
    HtmlList = 5,
}

public enum RelationStatus
{
    /// <summary>Найдено автоматически, ждёт проверки человеком.</summary>
    Candidate = 0,
    Confirmed = 1,
    Rejected = 2,
}

/// <summary>Площадка: собственная (сайт, канал пресс-службы) или сторонняя (СМИ, канал).</summary>
public class Resource
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public ResourceKind Kind { get; set; }
    /// <summary>Собственный ресурс пресс-службы.</summary>
    public bool IsOwn { get; set; }
    public string? HomeUrl { get; set; }
    /// <summary>Домен (или t.me/канал, vk.com/группа) для определения ресурса по ссылке.</summary>
    public string? UrlPrefix { get; set; }
    public string? Notes { get; set; }

    public CollectorKind CollectorKind { get; set; }
    /// <summary>Параметр сборщика: адрес сайта или ленты, имя канала, короткое имя или id сообщества.</summary>
    public string? CollectorTarget { get; set; }
    /// <summary>Для HTML-ленты: CSS-селектор ссылок на публикации.</summary>
    public string? CollectorSelector { get; set; }
    /// <summary>
    /// Мониторинг: загружать полный текст каждой публикации, а не только тех, что прошли
    /// предварительный отбор по заголовку и анонсу. Имеет смысл для небольших региональных изданий.
    /// </summary>
    public bool FetchFullText { get; set; }
    public bool IsCollectorEnabled { get; set; }
    public int CollectIntervalMinutes { get; set; } = 60;
    public int? DefaultMaterialTypeId { get; set; }
    public MaterialType? DefaultMaterialType { get; set; }
    public DateTime? LastCollectedAt { get; set; }
    public DateTime? LastCollectSucceededAt { get; set; }
    public string? LastCollectMessage { get; set; }
    public bool LastCollectFailed { get; set; }
}

/// <summary>Вид материала: пресс-релиз, статья, аналитика и т.д.</summary>
public class MaterialType
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SortOrder { get; set; }
    public ICollection<Material> Materials { get; set; } = new List<Material>();
}

/// <summary>
/// Материал — как собственный (пресс-службы), так и сторонний (созданный на его основе).
/// Один материал может быть опубликован по нескольким ссылкам и иметь несколько видов.
/// </summary>
public class Material
{
    public int Id { get; set; }
    public string Title { get; set; } = "";
    /// <summary>Дата первой публикации (UTC).</summary>
    public DateTime PublishedAt { get; set; }
    public bool IsOwn { get; set; }
    /// <summary>Перепечатка чужого материала (например, новости ВС РФ или пересылка из другого канала).</summary>
    public bool IsRepost { get; set; }
    /// <summary>Заголовок задан вручную — сборщики его не перезаписывают.</summary>
    public bool TitleIsManual { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    /// <summary>Заголовок в нижнем регистре — для поиска (LIKE в SQLite не умеет кириллицу без учёта регистра).</summary>
    public string SearchKey { get; set; } = "";

    public ICollection<MaterialLink> Links { get; set; } = new List<MaterialLink>();
    public ICollection<MaterialType> Types { get; set; } = new List<MaterialType>();
    /// <summary>Сторонние материалы, сделанные на основе этого (для собственных).</summary>
    public ICollection<MaterialRelation> Derived { get; set; } = new List<MaterialRelation>();
    /// <summary>Собственные материалы, на основе которых сделан этот (для сторонних).</summary>
    public ICollection<MaterialRelation> Sources { get; set; } = new List<MaterialRelation>();
}

/// <summary>Публикация материала на конкретном ресурсе.</summary>
public class MaterialLink
{
    public int Id { get; set; }
    public int MaterialId { get; set; }
    public Material Material { get; set; } = null!;
    public int? ResourceId { get; set; }
    public Resource? Resource { get; set; }

    public string Url { get; set; } = "";
    /// <summary>Нормализованная ссылка — уникальный ключ (см. <see cref="Press.Web.Services.UrlKey"/>).</summary>
    public string UrlKey { get; set; } = "";
    /// <summary>Заголовок этой версии (в Telegram он может отличаться от сайта).</summary>
    public string? Title { get; set; }
    /// <summary>Текст публикации — для сопоставления версий и поиска перепечаток.</summary>
    public string? Text { get; set; }
    public DateTime? PublishedAt { get; set; }
    /// <summary>Нормализованные ключи ссылок из текста публикации, через перевод строки.</summary>
    public string? OutLinkKeys { get; set; }
    public bool IsAutoCollected { get; set; }
    public DateTime AddedAt { get; set; }
}

/// <summary>Связь «собственный материал → сторонний материал, сделанный на его основе».</summary>
public class MaterialRelation
{
    public int Id { get; set; }
    public int SourceMaterialId { get; set; }
    public Material SourceMaterial { get; set; } = null!;
    public int DerivedMaterialId { get; set; }
    public Material DerivedMaterial { get; set; } = null!;
    public RelationStatus Status { get; set; }
    /// <summary>Оценка похожести 0..1 для автоматически найденных кандидатов.</summary>
    public double? Score { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public enum MonitoredItemStatus
{
    /// <summary>Не относится к суду — хранится только чтобы не обрабатывать повторно.</summary>
    Irrelevant = 0,
    /// <summary>Похоже на материал, сделанный на основе нашего, — ждёт проверки.</summary>
    Candidate = 1,
    /// <summary>Упоминает суд, но пару среди наших материалов найти не удалось — ждёт проверки.</summary>
    Mention = 2,
    Confirmed = 3,
    Dismissed = 4,
}

/// <summary>Публикация стороннего ресурса, найденная мониторингом.</summary>
public class MonitoredItem
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = null!;
    public string Url { get; set; } = "";
    public string UrlKey { get; set; } = "";
    public string? Title { get; set; }
    /// <summary>Анонс из ленты.</summary>
    public string? Summary { get; set; }
    /// <summary>Полный текст (загружается только для отобранных публикаций).</summary>
    public string? Text { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime FoundAt { get; set; }
    public MonitoredItemStatus Status { get; set; }
    /// <summary>Наш материал, на основе которого, вероятно, сделана публикация.</summary>
    public int? SuggestedMaterialId { get; set; }
    public Material? SuggestedMaterial { get; set; }
    public double? Score { get; set; }
    /// <summary>Пояснение: почему публикация отобрана (ключевые слова, общие слова, ссылка).</summary>
    public string? MatchInfo { get; set; }
    /// <summary>Созданный при подтверждении сторонний материал.</summary>
    public int? DerivedMaterialId { get; set; }
    public Material? DerivedMaterial { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

/// <summary>Настройки, редактируемые из интерфейса.</summary>
public class AppSetting
{
    public string Key { get; set; } = "";
    public string? Value { get; set; }
}

public class AppUser
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
}
