namespace SmsHubNext.Features.DatabaseMonitoring;

internal static class DatabaseMonitoringSql
{
    internal const string Overview = """
        SELECT DB_NAME() AS Name, CONCAT(N'SQL Server ', CONVERT(NVARCHAR(128),SERVERPROPERTY('ProductVersion')), N' · ', CONVERT(NVARCHAR(128),SERVERPROPERTY('Edition'))) AS Detail,
            SUM(CASE WHEN type=0 THEN CAST(size AS BIGINT) ELSE 0 END)/128.0 AS Metric1,
            SUM(CASE WHEN type=1 THEN CAST(size AS BIGINT) ELSE 0 END)/128.0 AS Metric2,
            (SELECT CAST(used_log_space_in_percent AS DECIMAL(9,4)) FROM sys.dm_db_log_space_usage) AS Metric3,
            CONCAT((SELECT CASE state_desc WHEN 'ONLINE' THEN N'در دسترس' ELSE state_desc END FROM sys.databases WHERE database_id=DB_ID()), N' · ',
                (SELECT CASE recovery_model_desc WHEN 'FULL' THEN N'بازیابی کامل' WHEN 'SIMPLE' THEN N'بازیابی ساده' WHEN 'BULK_LOGGED' THEN N'ثبت حداقلی حجیم' ELSE recovery_model_desc END FROM sys.databases WHERE database_id=DB_ID()), N' · انتظار آزادسازی لاگ: ',
                (SELECT CASE log_reuse_wait_desc WHEN 'NOTHING' THEN N'بدون انتظار' WHEN 'LOG_BACKUP' THEN N'نیاز به پشتیبان‌گیری لاگ' WHEN 'ACTIVE_TRANSACTION' THEN N'تراکنش فعال' WHEN 'CHECKPOINT' THEN N'در انتظار نقطه وارسی' ELSE log_reuse_wait_desc END FROM sys.databases WHERE database_id=DB_ID())) AS Status
        FROM sys.database_files;
        """;

    internal const string Tables = """
        SELECT QUOTENAME(s.name)+N'.'+QUOTENAME(t.name) AS Name, t.object_id AS ObjectId,
            CAST(SUM(CASE WHEN p.index_id IN (0,1) THEN p.row_count ELSE 0 END) AS DECIMAL(19,0)) AS Metric1,
            SUM(CASE WHEN p.index_id IN (0,1) THEN p.reserved_page_count ELSE 0 END)/128.0 AS Metric2,
            SUM(CASE WHEN p.index_id > 1 THEN p.reserved_page_count ELSE 0 END)/128.0 AS Metric3
        FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
        JOIN sys.dm_db_partition_stats p ON p.object_id=t.object_id
        WHERE t.is_ms_shipped=0
        GROUP BY s.name,t.name,t.object_id ORDER BY Metric2 DESC;
        """;

    internal const string Indexes = """
        SELECT QUOTENAME(OBJECT_SCHEMA_NAME(i.object_id))+N'.'+QUOTENAME(OBJECT_NAME(i.object_id)) AS Name,
            i.name AS Detail, i.object_id AS ObjectId, i.index_id AS IndexId, ps.partition_number AS PartitionNumber,
            CAST(ps.avg_fragmentation_in_percent AS DECIMAL(9,4)) AS Metric1,
            CAST(ps.page_count AS DECIMAL(19,0)) AS Metric2,
            CAST(ps.avg_page_space_used_in_percent AS DECIMAL(9,4)) AS Metric3,
            N'بررسی سبک؛ تراکم صفحات در این حالت در دسترس نیست' AS Status
        FROM sys.dm_db_index_physical_stats(DB_ID(),NULL,NULL,NULL,'LIMITED') ps
        JOIN sys.indexes i ON i.object_id=ps.object_id AND i.index_id=ps.index_id
        JOIN sys.tables t ON t.object_id=i.object_id
        WHERE t.is_ms_shipped=0 AND i.type IN (1,2) AND ps.index_level=0 AND ps.alloc_unit_type_desc='IN_ROW_DATA'
        ORDER BY ps.page_count DESC;
        """;

    internal const string IndexExists = "SELECT CAST(CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i JOIN sys.tables t ON t.object_id=i.object_id JOIN sys.partitions p ON p.object_id=i.object_id AND p.index_id=i.index_id WHERE i.object_id=@ObjectId AND i.index_id=@IndexId AND p.partition_number=@PartitionNumber AND i.type IN (1,2) AND t.is_ms_shipped=0) THEN 1 ELSE 0 END AS BIT)";

    internal const string IndexDetail = """
        SELECT QUOTENAME(OBJECT_SCHEMA_NAME(i.object_id))+N'.'+QUOTENAME(OBJECT_NAME(i.object_id)) AS Name,
            i.name AS Detail, i.object_id AS ObjectId, i.index_id AS IndexId, ps.partition_number AS PartitionNumber,
            CAST(ps.avg_fragmentation_in_percent AS DECIMAL(9,4)) AS Metric1,
            CAST(ps.page_count AS DECIMAL(19,0)) AS Metric2,
            CAST(ps.avg_page_space_used_in_percent AS DECIMAL(9,4)) AS Metric3,
            N'بررسی نمونه‌ای؛ ایندکس‌های کوچک ممکن است کامل خوانده شوند' AS Status
        FROM sys.dm_db_index_physical_stats(DB_ID(),@ObjectId,@IndexId,@PartitionNumber,'SAMPLED') ps
        JOIN sys.indexes i ON i.object_id=ps.object_id AND i.index_id=ps.index_id
        WHERE ps.index_level=0 AND ps.alloc_unit_type_desc='IN_ROW_DATA' AND i.type IN (1,2);
        """;

    internal const string Statistics = """
        SELECT QUOTENAME(OBJECT_SCHEMA_NAME(s.object_id))+N'.'+QUOTENAME(OBJECT_NAME(s.object_id)) AS Name,
            s.name AS Detail, CAST(p.rows AS DECIMAL(19,0)) AS Metric1,
            CAST(p.modification_counter AS DECIMAL(19,0)) AS Metric2,
            CAST(100.0*p.rows_sampled/NULLIF(p.rows,0) AS DECIMAL(9,4)) AS Metric3,
            DATEADD(MINUTE,DATEDIFF(MINUTE,SYSDATETIME(),SYSUTCDATETIME()),p.last_updated) AS AtUtc,
            CASE WHEN p.last_updated IS NULL THEN N'هنوز آمار ساخته نشده' ELSE N'آمار ثبت‌شده' END AS Status
        FROM sys.stats s JOIN sys.tables t ON t.object_id=s.object_id
        OUTER APPLY sys.dm_db_stats_properties(s.object_id,s.stats_id) p
        WHERE t.is_ms_shipped=0 ORDER BY p.modification_counter DESC;
        """;

    internal const string ServerVisibility = """
        SELECT CAST(COALESCE(HAS_PERMS_BY_NAME(NULL,NULL,
            CASE WHEN CAST(SERVERPROPERTY('ProductMajorVersion') AS INT)>=16 THEN 'VIEW SERVER PERFORMANCE STATE' ELSE 'VIEW SERVER STATE' END),0) AS BIT);
        """;

    internal const string Requests = """
        SELECT CONCAT(N'نشست ',r.session_id) AS Name, CONCAT(r.command,N' · ',r.wait_type) AS Detail,
            CAST(r.total_elapsed_time/1000.0 AS DECIMAL(19,4)) AS Metric1,
            CAST(r.wait_time/1000.0 AS DECIMAL(19,4)) AS Metric2,
            CAST(NULLIF(r.blocking_session_id,0) AS DECIMAL(19,0)) AS Metric3,
            CASE WHEN r.blocking_session_id>0 THEN N'مسدودشده' ELSE N'در حال اجرا' END AS Status
        FROM sys.dm_exec_requests r WHERE r.database_id=DB_ID() AND r.session_id<>@@SPID
        ORDER BY r.total_elapsed_time DESC;
        """;

    internal const string QueryStoreState = "SELECT actual_state_desc FROM sys.database_query_store_options;";
    internal const string Queries = """
        SELECT TOP (50) CONCAT(N'کوئری ',q.query_id) AS Name,
            CONCAT(N'اجرا: ',SUM(rs.count_executions),N' · متن کوئری برای حفاظت از اطلاعات نمایش داده نمی‌شود') AS Detail,
            CAST(SUM(rs.avg_duration*rs.count_executions)/NULLIF(SUM(rs.count_executions),0)/1000.0 AS DECIMAL(19,4)) AS Metric1,
            CAST(SUM(rs.avg_cpu_time*rs.count_executions)/1000000.0 AS DECIMAL(19,4)) AS Metric2,
            CAST(SUM(rs.avg_logical_io_reads*rs.count_executions)*8.0/1024 AS DECIMAL(19,4)) AS Metric3,
            CAST(MAX(rs.last_execution_time) AT TIME ZONE 'UTC' AS DATETIME2) AS AtUtc
        FROM sys.query_store_query q JOIN sys.query_store_plan p ON p.query_id=q.query_id
        JOIN sys.query_store_runtime_stats rs ON rs.plan_id=p.plan_id
        JOIN sys.query_store_runtime_stats_interval ri ON ri.runtime_stats_interval_id=rs.runtime_stats_interval_id
        WHERE ri.end_time>=DATEADD(HOUR,-24,SYSUTCDATETIME()) AND rs.execution_type=0
        GROUP BY q.query_id ORDER BY Metric2 DESC;
        """;

    internal const string Operations = """
        SELECT QUOTENAME(OBJECT_SCHEMA_NAME(object_id))+N'.'+QUOTENAME(OBJECT_NAME(object_id)) AS Name,
            name AS Detail, CAST(percent_complete AS DECIMAL(9,4)) AS Metric1,
            CAST(total_execution_time AS DECIMAL(19,0)) AS Metric2,
            CAST(partition_number AS DECIMAL(19,0)) AS Metric3,
            CASE WHEN state=0 THEN N'در حال اجرا' ELSE N'متوقف‌شده؛ قابل ادامه' END AS Status
        FROM sys.index_resumable_operations;
        """;

    internal const string History = """
        SELECT TOP (500) TableName AS Name, IndexName AS Detail, PercentComplete AS Metric1,
            CAST(ExecutionMinutes AS DECIMAL(19,0)) AS Metric2,
            CAST(DATEDIFF_BIG(SECOND,FirstObservedAtUtc,LastObservedAtUtc) AS DECIMAL(19,0)) AS Metric3,
            LastObservedAtUtc AS AtUtc, FirstObservedAtUtc,
            CONCAT(N'آخرین وضعیت مشاهده‌شده: ',StateDescription) AS Status
        FROM dbo.DatabaseIndexObservation ORDER BY LastObservedAtUtc DESC;
        """;

    internal const string Alerts = """
        SELECT N'مصرف بالای لاگ' AS Name, N'علت انتظار آزادسازی لاگ و زنجیره پشتیبان‌گیری را بررسی کنید.' AS Detail,
            CAST(used_log_space_in_percent AS DECIMAL(19,4)) AS Metric1, N'هشدار' AS Status
        FROM sys.dm_db_log_space_usage WHERE used_log_space_in_percent>=80
        UNION ALL
        SELECT N'انسداد طولانی',CONCAT(N'نشست ',session_id,N' منتظر نشست ',blocking_session_id,N' است؛ قطع خودکار انجام نمی‌شود.'),
            CAST(wait_time/1000.0 AS DECIMAL(19,4)),N'هشدار'
        FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND blocking_session_id>0 AND wait_time>=10000 AND session_id<>@@SPID
        UNION ALL
        SELECT N'درخواست طولانی',CONCAT(N'نشست ',session_id,N' · ',command),CAST(total_elapsed_time/1000.0 AS DECIMAL(19,4)),N'بررسی شود'
        FROM sys.dm_exec_requests WHERE database_id=DB_ID() AND total_elapsed_time>=30000 AND session_id<>@@SPID
        UNION ALL
        SELECT N'عملیات ایندکس متوقف‌شده',name,CAST(percent_complete AS DECIMAL(19,4)),N'بررسی شود'
        FROM sys.index_resumable_operations WHERE state=1
        UNION ALL
        SELECT N'نمونه‌برداری روزانه قدیمی یا ناموجود',N'اجرای برنامه و دسترسی پایش را بررسی کنید؛ زمان آخرین نمونه در صفحه رشد نمایش داده می‌شود.',NULL,N'هشدار'
        WHERE NOT EXISTS (SELECT 1 FROM dbo.DatabaseTableSnapshot WHERE CapturedAtUtc>=DATEADD(HOUR,-26,SYSUTCDATETIME()));
        """;
    internal const string Growth = """
        SELECT CAST(SnapshotDateUtc AS DATETIME2) AS DayUtc, SUM(DataMb) AS DataMb, SUM(IndexMb) AS IndexMb
        FROM dbo.DatabaseTableSnapshot WHERE SnapshotDateUtc>=DATEADD(DAY,-30,CAST(SYSUTCDATETIME() AS DATE))
        GROUP BY SnapshotDateUtc ORDER BY SnapshotDateUtc;
        """;

    internal const string Snapshot = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        IF NOT EXISTS (SELECT 1 FROM dbo.DatabaseTableSnapshot WITH (UPDLOCK,HOLDLOCK) WHERE SnapshotDateUtc=CAST(@Now AS DATE))
        BEGIN
            INSERT dbo.DatabaseTableSnapshot (SnapshotDateUtc,CapturedAtUtc,ObjectId,TableName,ApproximateRows,DataMb,IndexMb)
            SELECT CAST(@Now AS DATE),@Now,t.object_id,QUOTENAME(s.name)+N'.'+QUOTENAME(t.name),
                SUM(CASE WHEN p.index_id IN (0,1) THEN p.row_count ELSE 0 END),
                SUM(CASE WHEN p.index_id IN (0,1) THEN p.reserved_page_count ELSE 0 END)/128.0,
                SUM(CASE WHEN p.index_id>1 THEN p.reserved_page_count ELSE 0 END)/128.0
            FROM sys.tables t JOIN sys.schemas s ON s.schema_id=t.schema_id
            JOIN sys.dm_db_partition_stats p ON p.object_id=t.object_id WHERE t.is_ms_shipped=0
            GROUP BY t.object_id,s.name,t.name;
        END;
        DELETE dbo.DatabaseTableSnapshot WHERE SnapshotDateUtc<DATEADD(DAY,-90,CAST(@Now AS DATE));
        COMMIT;
        """;

    internal const string Observe = """
        SET XACT_ABORT ON;
        BEGIN TRANSACTION;
        UPDATE h WITH (UPDLOCK,HOLDLOCK) SET LastObservedAtUtc=@Now, StateDescription=CASE WHEN o.state=0 THEN N'در حال اجرا' ELSE N'متوقف‌شده' END,
            PercentComplete=o.percent_complete,ExecutionMinutes=o.total_execution_time
        FROM dbo.DatabaseIndexObservation h JOIN sys.index_resumable_operations o
            ON o.object_id=h.ObjectId AND o.index_id=h.IndexId AND o.start_time=h.StartedAtServerTime;
        INSERT dbo.DatabaseIndexObservation (ObjectId,IndexId,StartedAtServerTime,IndexName,TableName,FirstObservedAtUtc,LastObservedAtUtc,StateDescription,PercentComplete,ExecutionMinutes)
        SELECT o.object_id,o.index_id,o.start_time,o.name,QUOTENAME(OBJECT_SCHEMA_NAME(o.object_id))+N'.'+QUOTENAME(OBJECT_NAME(o.object_id)),@Now,@Now,
            CASE WHEN o.state=0 THEN N'در حال اجرا' ELSE N'متوقف‌شده' END,o.percent_complete,o.total_execution_time
        FROM sys.index_resumable_operations o WHERE NOT EXISTS (SELECT 1 FROM dbo.DatabaseIndexObservation h WITH(UPDLOCK,HOLDLOCK)
            WHERE h.ObjectId=o.object_id AND h.IndexId=o.index_id AND h.StartedAtServerTime=o.start_time);
        DELETE dbo.DatabaseIndexObservation WHERE LastObservedAtUtc<DATEADD(DAY,-90,@Now);
        COMMIT;
        """;
}







