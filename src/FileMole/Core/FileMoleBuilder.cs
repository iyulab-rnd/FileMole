using Microsoft.Extensions.Configuration;

namespace FileMole.Core;

public class FileMoleBuilder
{
    private FileMoleOptions _options = new FileMoleOptions();

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
        return new FileMole(_options);
    }
}