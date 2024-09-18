using FileMoles.Data;
using FileMoles.Internal;
using Microsoft.Extensions.Configuration;

namespace FileMoles;

public class FileMoleBuilder
{
    private FileMoleOptions _options = new();

    public FileMoleBuilder UseConfiguration(IConfiguration configuration)
    {
        configuration.GetSection("FileMole").Bind(_options);
        return this;
    }

    public FileMoleBuilder AddMole(Mole mole)
    {
        _options.Moles.Add(mole);
        return this;
    }

    public FileMoleBuilder AddMole(string path, MoleType type = MoleType.Local, string provider = "Default")
    {
        return AddMole(new Mole { Path = path, Type = type, Provider = provider });
    }

    public FileMoleBuilder SetOptions(FileMoleOptions options)
    {
        _options = options;
        return this;
    }

    public FileMoleBuilder SetOptions(Action<FileMoleOptions> configureOptions)
    {
        configureOptions(_options);
        return this;
    }

    public FileMole Build()
    {
        var fileMole = new FileMole(_options, ResolveDbContextAsync().Result);
        return fileMole;
    }

    private async Task<DbContext> ResolveDbContextAsync()
    {
        var dataPath = _options.GetDataPath();
        var dbPath = Path.Combine(dataPath, Constants.DbFileName);

        if (!Directory.Exists(dataPath))
        {
            Directory.CreateDirectory(dataPath);
        }

        var dbContext = await DbContext.CreateAsync(dbPath);
        return dbContext;
    }
}