FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["Whispr_MailboxService.sln", "./"]
COPY ["Application/Application.csproj", "Application/"]
COPY ["Infrastructure.Storage/Infrastructure.Storage.csproj", "Infrastructure.Storage/"]
COPY ["Migrator/Migrator.csproj", "Migrator/"]
COPY ["Services/Services.csproj", "Services/"]
COPY ["Worker/Worker.csproj", "Worker/"]
COPY ["UnitTests/UnitTests.csproj", "UnitTests/"]

RUN dotnet restore "Whispr_MailboxService.sln"

COPY . .
RUN dotnet publish "Services/Services.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Services.dll"]
