using System.Text.Json;
using System.Text;
using Microsoft.Extensions.Options;
using SmsHubNext.Shared.Results;

namespace SmsHubNext.Features.Administration;

public sealed class ListApplicationErrorsHandler(IOptions<ApplicationLogOptions> options, IHostEnvironment environment)
{
    public const int MaximumResults = 1000;

    public async Task<Result<ApplicationErrorResults>> Handle(DateTime fromUtc, DateTime toUtc, CancellationToken token)
    {
        if (fromUtc.Kind != DateTimeKind.Utc || toUtc.Kind != DateTimeKind.Utc || fromUtc > toUtc)
            return Error.Validation("logs.invalid_range", "بازه تاریخ و ساعت معتبر نیست؛ زمان شروع باید پیش از پایان باشد.");
        if (toUtc - fromUtc > TimeSpan.FromDays(31))
            return Error.Validation("logs.range_too_large", "بازه جست‌وجو حداکثر ۳۱ روز است.");

        string directory = options.Value.ResolveDirectory(environment);
        if (!Directory.Exists(directory))
            return new ApplicationErrorResults([], false, 0);

        PriorityQueue<ApplicationError, DateTimeOffset> latest = new();
        int matching = 0;
        int skipped = 0;
        foreach (string path in Directory.EnumerateFiles(directory, "errors-*.jsonl"))
        {
            token.ThrowIfCancellationRequested();
            // Rolling retention may remove a file between enumeration and opening.
            FileStream stream;
            try { stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete); }
            catch (FileNotFoundException) { continue; }
            await using (stream)
            {
                // Bound the scan even if errors keep being appended to the active file.
                long remainingBytes = stream.Length;
                using StreamReader reader = new(stream);
                while (remainingBytes > 0 && await reader.ReadLineAsync(token) is { } line)
                {
                    remainingBytes -= Encoding.UTF8.GetByteCount(line) + 1;
                    token.ThrowIfCancellationRequested();
                    ApplicationError? entry;
                    try { entry = Parse(line); }
                    catch (JsonException) { skipped++; continue; }
                    if (entry is null || entry.Timestamp.UtcDateTime < fromUtc || entry.Timestamp.UtcDateTime > toUtc)
                        continue;
                    matching++;
                    latest.Enqueue(entry, entry.Timestamp);
                    if (latest.Count > MaximumResults) latest.Dequeue();
                }
            }
        }

        ApplicationError[] items = latest.UnorderedItems.Select(item => item.Element)
            .OrderByDescending(item => item.Timestamp).ToArray();
        return new ApplicationErrorResults(items, matching > MaximumResults, skipped);
    }

    private static ApplicationError? Parse(string line)
    {
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object) return null;
        string level = Text(root, "Level");
        if (level is not ("Error" or "Fatal")) return null;
        if (!root.TryGetProperty("Timestamp", out JsonElement timestamp) || timestamp.ValueKind != JsonValueKind.String ||
            !timestamp.TryGetDateTimeOffset(out DateTimeOffset instant))
            throw new JsonException("Missing log timestamp.");
        JsonElement properties = root.TryGetProperty("Properties", out JsonElement values) ? values : default;
        return new ApplicationError(instant, level, Text(root, "RenderedMessage"), Text(properties, "SourceContext"),
            Text(root, "Exception"), Text(root, "TraceId") is { Length: > 0 } traceId ? traceId : Text(properties, "TraceId"));
    }

    private static string Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;
}
