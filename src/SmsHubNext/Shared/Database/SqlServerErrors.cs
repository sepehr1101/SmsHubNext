using Microsoft.Data.SqlClient;

namespace SmsHubNext.Shared.Database;

/// <summary>
/// Named checks over SQL Server error numbers, so feature code reacts to "a referenced row is
/// missing" or "a unique value already exists" by intent rather than sprinkling magic numbers
/// (547, 2601, 2627) through the handlers. Keep this list small — add a number only when a handler
/// actually needs to branch on it.
/// </summary>
public static class SqlServerErrors
{
    /// <summary>Foreign-key (or CHECK) constraint conflict — a referenced row does not exist.</summary>
    public const int ConstraintConflict = 547;

    /// <summary>Unique index (2601) / unique constraint (2627) violation — a duplicate key.</summary>
    public const int DuplicateUniqueIndex = 2601;
    public const int DuplicateUniqueConstraint = 2627;

    /// <summary>True when the exception is a foreign-key/CHECK conflict (a missing referenced row).</summary>
    public static bool IsConstraintConflict(this SqlException ex) => ex.Number == ConstraintConflict;

    /// <summary>True when the exception is a conflict raised by a known named constraint.</summary>
    public static bool IsConstraintConflict(this SqlException ex, string constraintName) =>
        ex.IsConstraintConflict() &&
        ex.Message.Contains(constraintName, StringComparison.OrdinalIgnoreCase);

    /// <summary>True when the exception is a duplicate-key violation on a unique index or constraint.</summary>
    public static bool IsUniqueViolation(this SqlException ex) =>
        ex.Number is DuplicateUniqueIndex or DuplicateUniqueConstraint;
}
