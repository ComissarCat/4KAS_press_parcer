# Сборка
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Press.Web/Press.Web.csproj src/Press.Web/
RUN dotnet restore src/Press.Web/Press.Web.csproj
COPY src/Press.Web/ src/Press.Web/
# без --no-restore: SDK подключает пакет со скриптами Blazor (_framework/blazor.web.js), только когда видит
# Razor-компоненты, а при восстановлении выше в папке был один .csproj — иначе скрипт не попадёт в образ (404)
RUN dotnet publish src/Press.Web/Press.Web.csproj -c Release -o /app /p:UseAppHost=false

# Запуск
FROM mcr.microsoft.com/dotnet/aspnet:10.0
# библиотека Kerberos нужна драйверу PostgreSQL — без неё в журнале сообщение libgssapi_krb5
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app .
ENV Urls=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# непривилегированный пользователь из базового образа
USER $APP_UID
ENTRYPOINT ["dotnet", "Press.Web.dll"]
