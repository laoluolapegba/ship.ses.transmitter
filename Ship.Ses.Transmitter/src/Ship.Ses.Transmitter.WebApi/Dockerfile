FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/Ship.Ses.Transmitter.Domain/Ship.Ses.Transmitter.Domain.csproj", "src/Ship.Ses.Transmitter.Domain/"]
COPY ["src/Ship.Ses.Transmitter.Application/Ship.Ses.Transmitter.Application.csproj", "src/Ship.Ses.Transmitter.Application/"]
COPY ["src/Ship.Ses.Transmitter.Infrastructure/Ship.Ses.Transmitter.Infrastructure.csproj", "src/Ship.Ses.Transmitter.Infrastructure/"]
COPY ["src/Ship.Ses.Transmitter.WebApi/Ship.Ses.Transmitter.WebApi.csproj", "src/Ship.Ses.Transmitter.WebApi/"]

RUN dotnet restore "./src/Ship.Ses.Transmitter.WebApi/Ship.Ses.Transmitter.WebApi.csproj" --disable-parallel
COPY . .
WORKDIR "src/Ship.Ses.Transmitter.WebApi"

RUN dotnet publish "./Ship.Ses.Transmitter.WebApi.csproj" -c release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER app
WORKDIR /app
COPY --from=build /app ./

EXPOSE 8080
EXPOSE 8081

ENTRYPOINT ["dotnet", "Ship.Ses.Transmitter.WebApi.dll"]