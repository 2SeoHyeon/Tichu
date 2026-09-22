FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Tichu/Tichu.csproj Tichu/
RUN dotnet restore Tichu/Tichu.csproj

COPY Tichu/ Tichu/
RUN dotnet publish Tichu/Tichu.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

ENTRYPOINT ["dotnet", "Tichu.dll"]
