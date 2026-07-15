using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;

namespace SmsHubNext.Deployment;

public static class SetupCommandRunner
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public static async Task<int?> TryRunAsync(string[] args, CancellationToken cancellationToken = default)
    {
        if (args.Length == 0 || !string.Equals(args[0], "setup", StringComparison.OrdinalIgnoreCase))
            return null;

        if (args.Length < 2)
            return SetupExitCodes.InvalidArguments;

        string command = args[1].ToLowerInvariant();
        Dictionary<string, string> options;

        try
        {
            options = ParseOptions(args, startIndex: 2);
        }
        catch (ArgumentException exception)
        {
            await WriteFallbackResultAsync(options: null, SetupCommandResult.Failed("setup.invalid_arguments", exception.Message));
            return SetupExitCodes.InvalidArguments;
        }

        return command switch
        {
            "test-database" => await TestDatabaseAsync(options, cancellationToken),
            "configure" => await ConfigureAsync(options, cancellationToken),
            _ => await UnknownCommandAsync(options, command),
        };
    }

    private static async Task<int> TestDatabaseAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        string? resultPath = GetOptionalOption(options, "--result");

        try
        {
            DatabaseSetupRequest request = await ReadRequestAsync(options, cancellationToken);
            DatabaseSetupService service = new();
            await service.TestConnectionAsync(request, cancellationToken);
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Succeeded("database.connection_succeeded", "The SQL Server connection succeeded."));
            return SetupExitCodes.Success;
        }
        catch (ArgumentException exception)
        {
            await WriteResultAsync(resultPath, SetupCommandResult.Failed("setup.invalid_arguments", exception.Message));
            return SetupExitCodes.InvalidArguments;
        }
        catch (InvalidDataException exception)
        {
            await WriteResultAsync(resultPath, SetupCommandResult.Failed("setup.invalid_request", exception.Message));
            return SetupExitCodes.InvalidRequest;
        }
        catch (SqlException exception)
        {
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Failed("database.connection_failed", SanitizeExceptionMessage(exception)));
            return SetupExitCodes.DatabaseConnectionFailed;
        }
        catch (Exception exception)
        {
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Failed("setup.unexpected_failure", SanitizeExceptionMessage(exception)));
            return SetupExitCodes.UnexpectedFailure;
        }
    }

    private static async Task<int> ConfigureAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        string? resultPath = GetOptionalOption(options, "--result");
        SettingsWriteReceipt? receipt = null;
        ProductionSettingsWriter writer = new();

        try
        {
            DatabaseSetupRequest request = await ReadRequestAsync(options, cancellationToken);
            string settingsPath = GetRequiredOption(options, "--settings");
            string keyRingPath = GetRequiredOption(options, "--key-ring");
            string connectionString = DatabaseConnectionStringFactory.Create(request);

            DatabaseSetupService service = new();
            await service.TestConnectionAsync(request, cancellationToken);

            receipt = writer.Write(settingsPath, connectionString, keyRingPath);
            writer.Commit(receipt);
            receipt = null;
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Succeeded(
                    "setup.configuration_succeeded",
                    "The SQL Server connection succeeded and production settings were written."));
            return SetupExitCodes.Success;
        }
        catch (ArgumentException exception)
        {
            RollbackIfNeeded(writer, receipt);
            await WriteResultAsync(resultPath, SetupCommandResult.Failed("setup.invalid_arguments", exception.Message));
            return SetupExitCodes.InvalidArguments;
        }
        catch (InvalidDataException exception)
        {
            RollbackIfNeeded(writer, receipt);
            await WriteResultAsync(resultPath, SetupCommandResult.Failed("setup.invalid_request", exception.Message));
            return SetupExitCodes.InvalidRequest;
        }
        catch (SqlException exception)
        {
            RollbackIfNeeded(writer, receipt);
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Failed("database.connection_failed", SanitizeExceptionMessage(exception)));
            return SetupExitCodes.DatabaseConnectionFailed;
        }
        catch (IOException exception)
        {
            RollbackIfNeeded(writer, receipt);
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Failed("settings.write_failed", SanitizeExceptionMessage(exception)));
            return SetupExitCodes.SettingsWriteFailed;
        }
        catch (UnauthorizedAccessException exception)
        {
            RollbackIfNeeded(writer, receipt);
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Failed("settings.write_failed", SanitizeExceptionMessage(exception)));
            return SetupExitCodes.SettingsWriteFailed;
        }
        catch (Exception exception)
        {
            RollbackIfNeeded(writer, receipt);
            await WriteResultAsync(
                resultPath,
                SetupCommandResult.Failed("setup.unexpected_failure", SanitizeExceptionMessage(exception)));
            return SetupExitCodes.UnexpectedFailure;
        }
    }

    private static async Task<int> UnknownCommandAsync(
        IReadOnlyDictionary<string, string> options,
        string command)
    {
        await WriteResultAsync(
            GetOptionalOption(options, "--result"),
            SetupCommandResult.Failed("setup.unknown_command", $"Unknown setup command '{command}'."));
        return SetupExitCodes.InvalidArguments;
    }

    private static Dictionary<string, string> ParseOptions(string[] args, int startIndex)
    {
        Dictionary<string, string> options = new(StringComparer.OrdinalIgnoreCase);

        for (int index = startIndex; index < args.Length; index += 2)
        {
            string name = args[index];
            if (!name.StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Option '{name}' must have a value.");

            if (!options.TryAdd(name, args[index + 1]))
                throw new ArgumentException($"Option '{name}' was specified more than once.");
        }

        return options;
    }

    private static async Task<DatabaseSetupRequest> ReadRequestAsync(
        IReadOnlyDictionary<string, string> options,
        CancellationToken cancellationToken)
    {
        string requestPath = GetRequiredOption(options, "--request");
        await using FileStream stream = File.OpenRead(requestPath);
        DatabaseSetupRequest? request = await JsonSerializer.DeserializeAsync<DatabaseSetupRequest>(
            stream,
            JsonOptions,
            cancellationToken);

        if (request is null)
            throw new InvalidDataException("The setup request is empty.");

        IReadOnlyList<string> errors = request.Validate();
        if (errors.Count > 0)
            throw new InvalidDataException(string.Join(" ", errors));

        return request;
    }

    private static string GetRequiredOption(IReadOnlyDictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out string? value) || string.IsNullOrWhiteSpace(value))
            throw new ArgumentException($"Required option '{name}' is missing.");

        return value;
    }

    private static string? GetOptionalOption(IReadOnlyDictionary<string, string> options, string name) =>
        options.TryGetValue(name, out string? value) ? value : null;

    private static async Task WriteResultAsync(string? resultPath, SetupCommandResult result)
    {
        if (string.IsNullOrWhiteSpace(resultPath))
        {
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptions));
            return;
        }

        string fullResultPath = Path.GetFullPath(resultPath);
        string? directory = Path.GetDirectoryName(fullResultPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        string temporaryPath = fullResultPath + $".{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine;
        await File.WriteAllTextAsync(
            temporaryPath,
            json,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        File.Move(temporaryPath, fullResultPath, overwrite: true);
    }

    private static Task WriteFallbackResultAsync(
        IReadOnlyDictionary<string, string>? options,
        SetupCommandResult result) =>
        WriteResultAsync(options is null ? null : GetOptionalOption(options, "--result"), result);

    private static void RollbackIfNeeded(ProductionSettingsWriter writer, SettingsWriteReceipt? receipt)
    {
        if (receipt is not null)
            writer.Rollback(receipt);
    }

    private static string SanitizeExceptionMessage(Exception exception)
    {
        string message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(message) ? "The operation failed." : message;
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
