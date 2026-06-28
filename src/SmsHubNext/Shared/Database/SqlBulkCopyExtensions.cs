using System.Data;
using Microsoft.Data.SqlClient;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// A thin wrapper over <see cref="SqlBulkCopy"/> for the high-volume insert paths (message
/// accept, delivery-report apply). It always enlists in the caller's <see cref="SqlTransaction"/>
/// so the bulk insert is atomic with the surrounding work (ACID), and always sets
/// <see cref="SqlBulkCopyOptions.CheckConstraints"/> — by default SqlBulkCopy skips FK/CHECK
/// constraints, which we never want for the fact tables; <see cref="SqlBulkCopyOptions.KeepNulls"/>
/// keeps caller-supplied nulls instead of substituting column defaults.
/// </summary>
public static class SqlBulkCopyExtensions
{
    public static async Task BulkInsertAsync(
        this SqlConnection connection,
        SqlTransaction transaction,
        string destinationTable,
        DataTable rows,
        CancellationToken cancellationToken)
    {
        using SqlBulkCopy bulk = new(
            connection,
            SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.KeepNulls,
            transaction)
        {
            DestinationTableName = destinationTable,
        };

        // Map by name so the destination's identity/extra columns (e.g. Message.Id) are left for
        // the server to assign rather than aligned by ordinal position.
        foreach (DataColumn column in rows.Columns)
            bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);

        await bulk.WriteToServerAsync(rows, cancellationToken);
    }
}
