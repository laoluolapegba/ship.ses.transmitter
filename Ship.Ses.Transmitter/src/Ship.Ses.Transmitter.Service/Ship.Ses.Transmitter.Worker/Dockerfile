# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/runtime:9.0 AS base
USER $APP_UID
WORKDIR /app


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker/Ship.Ses.Transmitter.Worker.csproj", "Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker/"]
COPY ["Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Infrastructure/Ship.Ses.Transmitter.Infrastructure.csproj", "Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Infrastructure/"]
COPY ["Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Application/Ship.Ses.Transmitter.Application.csproj", "Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Application/"]
COPY ["Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Domain/Ship.Ses.Transmitter.Domain.csproj", "Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Domain/"]
RUN dotnet restore "./Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker/Ship.Ses.Transmitter.Worker.csproj"
COPY . .
WORKDIR "/src/Ship.Ses.Transmitter/src/Ship.Ses.Transmitter.Service/Ship.Ses.Transmitter.Worker"
RUN dotnet build "./Ship.Ses.Transmitter.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Ship.Ses.Transmitter.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ship.Ses.Transmitter.Worker.dll"]