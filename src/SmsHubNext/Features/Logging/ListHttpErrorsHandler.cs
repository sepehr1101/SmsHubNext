using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SmsHubNext.Features.Administration;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Logging;

public sealed class ListHttpErrorsHandler(IOptions<ApplicationLogOptions> options, IHostEnvironment environment)
{
    public const int MaximumResults = 1000;

    public async Task<Result<HttpErrorResults>> Handle(HttpErrorQuery query, CancellationToken token)
    {
        Result validation = query.Validate();
        if (validation.IsFailure) return validation.Error!;
        string directory = options.Value.ResolveDirectory(environment);
        if (!Directory.Exists(directory)) return new HttpErrorResults([], false, 0);

        PriorityQueue<HttpErrorEntry, DateTimeOffset> latest = new();
        int matching = 0;
        int skipped = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "http-errors-*.jsonl"))
        {
            token.ThrowIfCancellationRequested();
            FileStream stream;
            try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (FileNotFoundException) { continue; }
            await using (stream)
            {
                long remainingBytes = stream.Length;
                using StreamReader reader = new(stream);
                while (remainingBytes > 0 && await reader.ReadLineAsync(token) is { } line)
                {
                    remainingBytes -= Encoding.UTF8.GetByteCount(line) + 1;
                    token.ThrowIfCancellationRequested();
                    HttpErrorEntry? entry;
                    try { entry = Parse(line); }
                    catch (JsonException) { skipped++; continue; }
                    if (entry is null || !query.Matches(ApplicationLogTime.ToTehran(entry.Timestamp), entry.StatusCode)) continue;
                    matching++;
                    latest.Enqueue(entry, entry.Timestamp);
                    if (latest.Count > MaximumResults) latest.Dequeue();
                }
            }
        }

        HttpErrorEntry[] items = latest.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(item => item.Timestamp).ToArray();
        return new HttpErrorResults(items, matching > MaximumResults, skipped);
    }

    private static HttpErrorEntry? Parse(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("Timestamp", out JsonElement timestamp) || timestamp.ValueKind != JsonValueKind.String ||
            !timestamp.TryGetDateTimeOffset(out DateTimeOffset instant) ||
            !root.TryGetProperty("Properties", out JsonElement properties) || properties.ValueKind != JsonValueKind.Object ||
            !properties.TryGetProperty("StatusCode", out JsonElement code) || code.ValueKind != JsonValueKind.Number ||
            !code.TryGetInt32(out int statusCode) || statusCode is < 400 or > 599) return null;
        return new HttpErrorEntry(instant, statusCode, Text(properties, "RequestMethod"),
            Text(properties, "RequestPath"), Text(properties, "TraceId"));
    }

    private static string Text(JsonElement properties, string name) =>
        properties.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}
