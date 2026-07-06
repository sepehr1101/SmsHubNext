namespace SmsHubNext.Features.Providers;

public interface ISecretProtector
{
    byte[] Protect(string secret);

    string Unprotect(byte[] cipher);
}
