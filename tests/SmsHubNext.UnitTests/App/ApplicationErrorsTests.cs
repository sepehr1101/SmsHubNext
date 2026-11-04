using System.Globalization;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;
using Serilog.Parsing;
using SmsHubNext.Features.Administration;
using SmsHubNext.Shared.Results;
using SmsHubNext.Shared.Ui;
using Xunit;

namespace SmsHubNext.UnitTests.App;

public sealed class ApplicationErrorsTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "SmsHubNext-log-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task Reads_actual_serilog_files_with_inclusive_utc_bounds_and_multiline_details()
    {
        Directory.CreateDirectory(_directory);
        DateTimeOffset start = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        string path = Path.Combine(_directory, "errors-test.jsonl");
        using (Logger logger = new LoggerConfiguration().WriteTo.File(new JsonFormatter(renderMessage: true), path).CreateLogger())
        {
            Write(logger, start.AddSeconds(-1), LogEventLevel.Error, "before");
            Write(logger, start, LogEventLevel.Error, "شروع فارسی", new InvalidOperationException("خطای آزمایشی\nsecond line"));
            Write(logger, start.AddMinutes(1), LogEventLevel.Warning, "warning");
            Write(logger, start.AddMinutes(2), LogEventLevel.Fatal, "end");
            Write(logger, start.AddMinutes(2).AddTicks(1), LogEventLevel.Error, "after");
        }
        await File.AppendAllTextAsync(path, "{incomplete\n");

        Result<ApplicationErrorResults> result = await Handler().Handle(start.UtcDateTime, start.AddMinutes(2).UtcDateTime, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(["end", "شروع فارسی"], result.Value.Items.Select(item => item.Message));
        Assert.Contains("second line", result.Value.Items[1].Exception);
        Assert.Equal("test-source", result.Value.Items[1].Source);
        Assert.Equal(1, result.Value.SkippedLines);
        Assert.False(result.Value.Truncated);
    }

    [Fact]
    public async Task Returns_newest_thousand_across_files_and_reports_truncation()
    {
        Directory.CreateDirectory(_directory);
        DateTimeOffset start = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        for (int file = 0; file < 2; file++)
        {
            using Logger logger = new LoggerConfiguration().WriteTo.File(new JsonFormatter(renderMessage: true),
                Path.Combine(_directory, $"errors-{file}.jsonl")).CreateLogger();
            for (int index = file; index < 1002; index += 2)
                Write(logger, start.AddSeconds(index), LogEventLevel.Error, index.ToString(CultureInfo.InvariantCulture));
        }
        Result<ApplicationErrorResults> result = await Handler().Handle(start.UtcDateTime, start.AddHours(1).UtcDateTime, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Truncated);
        Assert.Equal(1000, result.Value.Items.Count);
        Assert.Equal("1001", result.Value.Items[0].Message);
        Assert.Equal("2", result.Value.Items[^1].Message);
    }

    [Fact]
    public async Task Empty_directory_and_invalid_ranges_are_handled()
    {
        DateTime start = new(2026, 10, 18, 0, 0, 0, DateTimeKind.Utc);
        ListApplicationErrorsHandler handler = Handler();
        Assert.Empty((await handler.Handle(start, start, CancellationToken.None)).Value.Items);
        Assert.True((await handler.Handle(start, start.AddMinutes(-1), CancellationToken.None)).IsFailure);
        Assert.True((await handler.Handle(start, start.AddDays(32), CancellationToken.None)).IsFailure);
        Assert.True((await handler.Handle(DateTime.SpecifyKind(start, DateTimeKind.Unspecified), start, CancellationToken.None)).IsFailure);
    }

    [Fact]
    public void Persian_input_is_interpreted_as_tehran_wall_time()
    {
        Assert.True(PersianDisplay.TryParseDateTime("۱۴۰۵/۰۷/۲۶ ۱۲:۳۰", out DateTime? parsed));
        DateTime utc = ApplicationLogTime.ToUtc(parsed!.Value);
        Assert.Equal(DateTimeKind.Utc, utc.Kind);
        Assert.Equal(9, utc.Hour);
        Assert.Equal(0, utc.Minute);
        Assert.Equal(parsed.Value.Ticks, ApplicationLogTime.ToTehran(new DateTimeOffset(utc)).Ticks);
    }

    private ListApplicationErrorsHandler Handler() => new(
        Options.Create(new ApplicationLogOptions { DirectoryPath = _directory }), new TestEnvironment());

    [Fact]
    public async Task Message_lookup_uses_structured_ids_before_truncation_and_ignores_text_and_batch_matches()
    {
        Directory.CreateDirectory(_directory);
        DateTimeOffset start = new(2026, 10, 18, 0, 0, 0, TimeSpan.Zero);
        string path = Path.Combine(_directory, "errors-correlated.jsonl");
        using (Logger logger = new LoggerConfiguration().WriteTo.File(new JsonFormatter(renderMessage: true), path).CreateLogger())
        {
            logger.ForContext("MessageId", 123L).Error(new InvalidOperationException("synthetic exception"), "matching message");
            logger.ForContext("MessageIds", new long[] { 123, 124 }).Error("matching chunk");
            logger.ForContext("MessageId", 1234L).Error("another message");
            logger.ForContext("BatchId", 123L).Error("batch only");
            for (int index = 0; index < 1002; index++) logger.Error("123 appears only in text");
        }
        Result<ApplicationErrorResults> result = await Handler().HandleForMessage(start.UtcDateTime,
            start.AddYears(1).UtcDateTime, 123, CancellationToken.None);
        Assert.True(result.IsFailure); // The usual bounded-range validation also applies to correlated queries.
        DateTime now = TimeProvider.System.GetUtcNow().UtcDateTime;
        result = await Handler().HandleForMessage(now.AddMinutes(-2), now.AddMinutes(2), 123, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.False(result.Value.Truncated);
        Assert.Contains(result.Value.Items, item => item.Exception?.Contains("synthetic exception", StringComparison.Ordinal) == true);
        Assert.All(result.Value.Items, item => Assert.StartsWith("matching", item.Message));
    }

    private static void Write(Logger logger, DateTimeOffset instant, LogEventLevel level, string message, Exception? exception = null)
    {
        LogEvent entry = new(instant, level, exception, new MessageTemplateParser().Parse("{Message:l}"),
            [new LogEventProperty("Message", new ScalarValue(message)), new LogEventProperty("SourceContext", new ScalarValue("test-source"))]);
        logger.Write(entry);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "SmsHubNext";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
