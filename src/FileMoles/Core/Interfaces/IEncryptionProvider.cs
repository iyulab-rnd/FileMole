namespace FileMoles.Core.Interfaces;

public interface IEncryptionProvider
{
    Task<Stream> EncryptAsync(Stream input, string key);
    Task<Stream> DecryptAsync(Stream input, string key);
    Task<string> GetEncryptionKeyHashAsync(string key);
    bool ValidateKeyHash(string key, string hash);
}
