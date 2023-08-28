FROM mcr.microsoft.com/dotnet/runtime:7.0 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:7.0 AS build
WORKDIR /src
COPY ["examples/Demo/Demo.csproj", "examples/Demo/"]
RUN dotnet restore "examples/Demo/Demo.csproj"
COPY . .
WORKDIR "/src/examples/Demo"
RUN dotnet build "Demo.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Demo.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Demo.dll"]
