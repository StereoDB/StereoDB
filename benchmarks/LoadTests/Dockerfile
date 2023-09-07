FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS restore
WORKDIR /src
COPY [".", "src/LoadTests/"]
RUN dotnet restore "src/LoadTests/LoadTests.csproj"

FROM restore AS publish
RUN dotnet publish "src/LoadTests/LoadTests.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "LoadTests.dll"]