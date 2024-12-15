using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using FileMoles.Cache;
using FileMoles.Core.Interfaces;
using FileMoles.Monitoring;
using FileMoles.Core.Models;
using FileMoles.Security;
using Microsoft.Extensions.Logging;

namespace FileMoles;

public class FileMoleBuilder
{
    private readonly IServiceCollection _services;
    private readonly FileMoleOptions _options;

    public FileMoleBuilder()
    {
        _services = new ServiceCollection();
        _options = new FileMoleOptions();
    }

    public FileMoleBuilder UseCache(Action<CacheOptions> configure)
    {
        configure(_options.Cache);
        return this;
    }

    public FileMoleBuilder UseMonitoring(Action<MonitoringOptions> configure)
    {
        configure(_options.Monitoring);
        return this;
    }

    public FileMoleBuilder ConfigureSecurity(Action<SecurityOptions> configure)
    {
        configure(_options.Security);
        return this;
    }

    public FileMoleBuilder ConfigureLogging(Action<ILoggingBuilder> configure)
    {
        _services.AddLogging(configure);
        return this;
    }

    public FileMoleBuilder AddProvider<TProvider>(TProvider provider) where TProvider : class, IStorageProvider
    {
        _services.AddSingleton<IStorageProvider>(provider);
        return this;
    }

    public FileMoleBuilder AddProvider<TProvider>() where TProvider : class, IStorageProvider
    {
        _services.AddSingleton<IStorageProvider, TProvider>();
        return this;
    }

    public FileMole Build()
    {
        ConfigureServices();
        var serviceProvider = _services.BuildServiceProvider();

        return new FileMole(
            serviceProvider.GetRequiredService<IFileSystemCache>(),
        serviceProvider.GetRequiredService<IFileSystemMonitor>(),
            serviceProvider.GetRequiredService<IFileSystemSecurityManager>(),
            serviceProvider.GetRequiredService<ILogger<FileMole>>(),
            serviceProvider.GetServices<IStorageProvider>());
    }

    private void ConfigureServices()
    {
        // Add options
        _services.Configure<FileMoleOptions>(_ => _options);
        _services.Configure<CacheOptions>(_ => _options.Cache);
        _services.Configure<MonitoringOptions>(_ => _options.Monitoring);
        _services.Configure<SecurityOptions>(_ => _options.Security);

        // Configure cache
        if (_options.Cache.Enabled)
        {
            _services.AddDbContext<CacheDbContext>(options =>
                options.UseSqlite(_options.Cache.ConnectionString));
            _services.AddSingleton<IFileSystemCache, ImprovedSqliteFileSystemCache>();
        }
        else
        {
            _services.AddSingleton<IFileSystemCache, NoOpFileSystemCache>();
        }

        // Configure monitoring
        if (_options.Monitoring.Enabled)
        {
            _services.AddSingleton<IFileSystemMonitor, FileSystemMonitor>();
        }
        else
        {
            _services.AddSingleton<IFileSystemMonitor, NoOpFileSystemMonitor>();
        }

        // Configure security
        _services.AddSingleton<IFileSystemSecurityManager, FileSystemSecurityManager>();

        // Add default services if not configured
        if (!_services.Any(x => x.ServiceType == typeof(ILogger<>)))
        {
            _services.AddLogging(builder => builder.AddConsole());
        }
    }
}