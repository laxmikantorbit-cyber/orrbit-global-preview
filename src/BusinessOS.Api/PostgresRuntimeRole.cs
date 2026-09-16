using Npgsql;

namespace BusinessOS.Api;

internal static class PostgresRuntimeRole
{
    public static async Task ApplyAsync(
        NpgsqlConnection connection,
        string? runtimeRole,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(runtimeRole))
            return;

        await using var command = new NpgsqlCommand(
            "SET ROLE " + QuoteIdentifier(runtimeRole), connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task ResetAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("RESET ROLE", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    internal static string QuoteIdentifier(string value) =>
        "\"" + value.Trim().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
