﻿using Ship.Ses.Transmitter.Infrastructure.Settings;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using StackExchange.Redis;

namespace Ship.Ses.Transmitter.Infrastructure.Installers
{
    public static class RedisInstaller
    {
        public static ConnectionMultiplexer InstallRedis(this WebApplicationBuilder builder)
        {
            var redisSettings = builder.Configuration.GetSection(nameof(AppSettings)).Get<AppSettings>()!.Redis;
            var db = ConnectionMultiplexer.Connect(new ConfigurationOptions
            {
                EndPoints = { $"{redisSettings.Host}:{redisSettings.Port}" },
                AbortOnConnectFail = false,
                Ssl = false,
                Password = redisSettings.Password

            });
            return db;
        }
    }
}
