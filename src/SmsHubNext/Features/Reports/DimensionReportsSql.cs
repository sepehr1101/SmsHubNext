namespace SmsHubNext.Features.Reports;

internal static class DimensionReportsSql
{
    public const string Options = """
        SELECT CAST(Id AS INT) AS Id, Name + CASE WHEN DeletedAtUtc IS NULL THEN N'' ELSE N' (حذف‌شده)' END AS Name FROM dbo.Customer WHERE IsSystem = 0 ORDER BY Name;
        SELECT CAST(Id AS INT) AS Id, Name + CASE WHEN DeletedAtUtc IS NULL THEN N'' ELSE N' (حذف‌شده)' END AS Name FROM dbo.Provider ORDER BY Name;
        SELECT CAST(Id AS INT) AS Id, Name + CASE WHEN DeletedAtUtc IS NULL THEN N'' ELSE N' (حذف‌شده)' END AS Name FROM dbo.MessageType ORDER BY Name;
        SELECT node.Id, STRING_AGG(CAST(ancestor.Name AS NVARCHAR(MAX)), N' / ') WITHIN GROUP (ORDER BY ancestor.SectionType)
            + CASE WHEN node.DeletedAtUtc IS NULL THEN N'' ELSE N' (حذف‌شده)' END AS Name
        FROM dbo.GeoSection node JOIN dbo.GeoSection ancestor ON node.Path LIKE ancestor.Path + '%'
        GROUP BY node.Id, node.Path, node.DeletedAtUtc ORDER BY node.Path;
        """;

    public static string Build(DimensionReportKind kind)
    {
        bool geography = kind is DimensionReportKind.Geography or DimensionReportKind.GeographyMessageType or DimensionReportKind.GeographyMonth;
        bool messageType = kind is DimensionReportKind.MessageType or DimensionReportKind.GeographyMessageType or DimensionReportKind.MessageTypeMonth;
        bool month = kind is DimensionReportKind.GeographyMonth or DimensionReportKind.MessageTypeMonth;
        string geoColumns = geography ? "g.Id AS GeoSectionId, COALESCE(g.FullName, N'فاقد منطقه در سطح انتخاب‌شده') AS GeoName" : "CAST(NULL AS INT) AS GeoSectionId, N'' AS GeoName";
        string typeColumns = messageType ? "m.MessageTypeId, mt.Name AS MessageTypeName" : "CAST(NULL AS TINYINT) AS MessageTypeId, N'' AS MessageTypeName";
        string monthColumn = month ? "LEFT(m.SubmitDateJalali, 7)" : "CAST('' AS VARCHAR(7))";
        List<string> groups = [];
        if (geography) groups.Add("g.Id, g.FullName");
        if (messageType) groups.Add("m.MessageTypeId, mt.Name");
        if (month) groups.Add("LEFT(m.SubmitDateJalali, 7)");
        // Only trusted enum-selected fragments enter SQL. Aggregate current fact rows;
        // never join delivery history, resolve tariffs or sum multiple hierarchy levels.
        return $"""
            WITH Geography AS (
                SELECT node.Id, node.Path,
                    STRING_AGG(CAST(ancestor.Name AS NVARCHAR(MAX)), N' / ') WITHIN GROUP (ORDER BY ancestor.SectionType) AS FullName
                FROM dbo.GeoSection node
                JOIN dbo.GeoSection ancestor ON node.Path LIKE ancestor.Path + '%'
                WHERE node.SectionType = @GeographyLevel
                GROUP BY node.Id, node.Path
            )
            SELECT {geoColumns}, {typeColumns}, {monthColumn} AS JalaliMonth,
                COUNT_BIG(*) AS MessageCount,
                SUM(CAST(m.SegmentCount AS BIGINT)) AS SegmentCount,
                SUM(m.TotalCost) AS TotalCost,
                SUM(CAST(CASE WHEN m.Status IN (2,3) THEN 1 ELSE 0 END AS BIGINT)) AS AcceptedCount,
                SUM(CAST(CASE WHEN m.Status IN (2,3) AND m.DeliveryStatus = 2 THEN 1 ELSE 0 END AS BIGINT)) AS DeliveredCount,
                SUM(CAST(CASE WHEN m.Status IN (2,3) AND m.DeliveryStatus IN (3,4) THEN 1 ELSE 0 END AS BIGINT)) AS UndeliveredCount,
                SUM(CAST(CASE WHEN m.Status IN (2,3) AND m.DeliveryStatus = 1 THEN 1 ELSE 0 END AS BIGINT)) AS PendingCount,
                SUM(CAST(CASE WHEN m.Status IN (2,3) AND m.DeliveryStatus = 5 THEN 1 ELSE 0 END AS BIGINT)) AS UnknownCount
            FROM dbo.Message m
            LEFT JOIN dbo.GeoSection assigned ON assigned.Id = m.GeoSectionId
            {(geography ? "LEFT JOIN Geography g ON assigned.Path LIKE g.Path + '%'" : "")}
            {(messageType ? "JOIN dbo.MessageType mt ON mt.Id = m.MessageTypeId" : "")}
            WHERE m.SubmitDateJalali >= @FromJalali AND m.SubmitDateJalali <= @ToJalali
                AND (@CustomerId IS NULL OR m.CustomerId = @CustomerId)
                AND (@ProviderId IS NULL OR m.ProviderId = @ProviderId)
                AND (@MessageTypeId IS NULL OR m.MessageTypeId = @MessageTypeId)
                AND (@GeoSectionId IS NULL OR EXISTS (
                    SELECT 1 FROM dbo.GeoSection selected WHERE selected.Id = @GeoSectionId AND assigned.Path LIKE selected.Path + '%'))
            GROUP BY {string.Join(", ", groups)}
            ORDER BY {(month ? "JalaliMonth, " : "")}TotalCost DESC, GeoSectionId, MessageTypeId
            OPTION (RECOMPILE);
            """;
    }
}

