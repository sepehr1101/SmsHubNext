namespace SmsHubNext.Features.Reports;

internal static class ReportsSql
{
    private const string Filters =
        """
        WHERE m.SubmitDateJalali >= @FromJalali
          AND m.SubmitDateJalali <= @ToJalali
          AND (@CustomerId IS NULL OR m.CustomerId = @CustomerId)
          AND (@ProviderId IS NULL OR m.ProviderId = @ProviderId)
          AND (@MessageTypeId IS NULL OR m.MessageTypeId = @MessageTypeId)
          AND (@GeoSectionId IS NULL OR filterGeo.Id IS NOT NULL)
        """;

    private const string GeoFilterJoin =
        """
        LEFT JOIN dbo.GeoSection messageGeo ON messageGeo.Id = m.GeoSectionId
        LEFT JOIN dbo.GeoSection filterGeo
            ON filterGeo.Id = @GeoSectionId
           AND messageGeo.Path LIKE filterGeo.Path + '%'
        """;

    private const string StatSelect =
        """
        COUNT_BIG(*) AS MessageCount,
        COALESCE(SUM(CAST(m.SegmentCount AS BIGINT)), 0) AS SegmentCount,
        COALESCE(SUM(m.TotalCost), 0) AS TotalCost,
        SUM(CASE WHEN m.DeliveryStatus = 1 THEN 1 ELSE 0 END) AS PendingCount,
        SUM(CASE WHEN m.DeliveryStatus = 2 THEN 1 ELSE 0 END) AS DeliveredCount,
        SUM(CASE WHEN m.DeliveryStatus = 3 THEN 1 ELSE 0 END) AS UndeliveredCount,
        SUM(CASE WHEN m.DeliveryStatus = 4 THEN 1 ELSE 0 END) AS ExpiredCount,
        SUM(CASE WHEN m.DeliveryStatus = 5 THEN 1 ELSE 0 END) AS UnknownCount,
        CAST(
            CASE WHEN COUNT_BIG(*) = 0
                 THEN 0
                 ELSE (SUM(CASE WHEN m.DeliveryStatus = 2 THEN 1 ELSE 0 END) * 100.0) / COUNT_BIG(*)
            END AS DECIMAL(9,4)) AS DeliveryRate
        """;

    public const string Summary =
        $"""
        SELECT {StatSelect}
        FROM dbo.Message m
        {GeoFilterJoin}
        {Filters};
        """;

    public const string ByProvider =
        $"""
        SELECT
            m.ProviderId,
            p.Code AS ProviderCode,
            p.Name AS ProviderName,
            {StatSelect}
        FROM dbo.Message m
        INNER JOIN dbo.Provider p ON p.Id = m.ProviderId
        {GeoFilterJoin}
        {Filters}
        GROUP BY m.ProviderId, p.Code, p.Name
        ORDER BY TotalCost DESC, MessageCount DESC, p.Name;
        """;

    public const string ByMessageType =
        $"""
        SELECT
            m.MessageTypeId,
            mt.Code AS MessageTypeCode,
            mt.Name AS MessageTypeName,
            {StatSelect}
        FROM dbo.Message m
        INNER JOIN dbo.MessageType mt ON mt.Id = m.MessageTypeId
        {GeoFilterJoin}
        {Filters}
        GROUP BY m.MessageTypeId, mt.Code, mt.Name
        ORDER BY TotalCost DESC, MessageCount DESC, mt.Name;
        """;

    public const string ByGeo =
        $"""
        SELECT
            m.GeoSectionId,
            messageGeo.SectionType,
            messageGeo.Code AS GeoSectionCode,
            COALESCE(messageGeo.Name, N'Unspecified') AS GeoSectionName,
            {StatSelect}
        FROM dbo.Message m
        {GeoFilterJoin}
        {Filters}
        GROUP BY m.GeoSectionId, messageGeo.SectionType, messageGeo.Code, messageGeo.Name
        ORDER BY TotalCost DESC, MessageCount DESC, GeoSectionName;
        """;

    public const string ByJalaliMonth =
        $"""
        SELECT
            LEFT(m.SubmitDateJalali, 7) AS JalaliMonth,
            {StatSelect}
        FROM dbo.Message m
        {GeoFilterJoin}
        {Filters}
        GROUP BY LEFT(m.SubmitDateJalali, 7)
        ORDER BY JalaliMonth;
        """;

    public const string ByGeoRollup =
        $"""
        SELECT
            rollupGeo.Id AS GeoSectionId,
            rollupGeo.SectionType,
            rollupGeo.Code AS GeoSectionCode,
            COALESCE(rollupGeo.Name, N'Unspecified') AS GeoSectionName,
            {StatSelect}
        FROM dbo.Message m
        {GeoFilterJoin}
        LEFT JOIN dbo.GeoSection rollupGeo ON messageGeo.Path LIKE rollupGeo.Path + '%'
        {Filters}
        GROUP BY rollupGeo.Id, rollupGeo.SectionType, rollupGeo.Code, rollupGeo.Name
        ORDER BY rollupGeo.SectionType, TotalCost DESC, MessageCount DESC, GeoSectionName;
        """;

    public const string ByProviderMessageTypeGeo =
        $"""
        SELECT
            m.ProviderId,
            p.Code AS ProviderCode,
            p.Name AS ProviderName,
            m.MessageTypeId,
            mt.Code AS MessageTypeCode,
            mt.Name AS MessageTypeName,
            m.GeoSectionId,
            messageGeo.SectionType,
            messageGeo.Code AS GeoSectionCode,
            COALESCE(messageGeo.Name, N'Unspecified') AS GeoSectionName,
            {StatSelect}
        FROM dbo.Message m
        INNER JOIN dbo.Provider p ON p.Id = m.ProviderId
        INNER JOIN dbo.MessageType mt ON mt.Id = m.MessageTypeId
        {GeoFilterJoin}
        {Filters}
        GROUP BY
            m.ProviderId, p.Code, p.Name,
            m.MessageTypeId, mt.Code, mt.Name,
            m.GeoSectionId, messageGeo.SectionType, messageGeo.Code, messageGeo.Name
        ORDER BY TotalCost DESC, MessageCount DESC, p.Name, mt.Name, GeoSectionName;
        """;
}
