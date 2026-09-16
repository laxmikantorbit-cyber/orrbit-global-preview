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

        var quoted = "\"" + runtimeRole.Trim().Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        await using var command = new NpgsqlCommand("SET ROLE " + quoted, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
