# Сборка
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Press.Web/Press.Web.csproj src/Press.Web/
RUN dotnet restore src/Press.Web/Press.Web.csproj
COPY src/Press.Web/ src/Press.Web/
RUN dotnet publish src/Press.Web/Press.Web.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# Запуск
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV Urls=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
# непривилегированный пользователь из базового образа
USER $APP_UID
ENTRYPOINT ["dotnet", "Press.Web.dll"]
