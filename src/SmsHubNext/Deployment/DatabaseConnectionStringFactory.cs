using Microsoft.Data.SqlClient;

namespace SmsHubNext.Deployment;

public static class DatabaseConnectionStringFactory
{
    public static string Create(DatabaseSetupRequest request)
    {
        IReadOnlyList<string> errors = request.Validate();
        if (errors.Count > 0)
            throw new ArgumentException(string.Join(" ", errors), nameof(request));

        SqlConnectionStringBuilder builder = new()
        {
            DataSource = request.Server.Trim(),
            InitialCatalog = request.Database.Trim(),
            IntegratedSecurity = request.Authentication == DatabaseAuthenticationMode.Windows,
            Encrypt = SqlConnectionEncryptOption.Mandatory,
            TrustServerCertificate = request.TrustServerCertificate,
            MultipleActiveResultSets = true,
            ApplicationName = "SmsHubNext",
            ConnectTimeout = request.ConnectTimeoutSeconds,
            PersistSecurityInfo = false,
        };

        if (request.Authentication == DatabaseAuthenticationMode.SqlServer)
        {
            builder.UserID = request.Username!.Trim();
            builder.Password = request.Password!;
        }

        return builder.ConnectionString;
    }
}
