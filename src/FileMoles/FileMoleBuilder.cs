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

    public FileMole Build()
    {
        var fileMole = new FileMole(_options);
        fileMole.StartInitialScan();
        return fileMole;
    }
}