using System.Data;
using System.Globalization;
using Dapper;

namespace Daycord.Infrastructure.Persistence;

public static class SqliteTypeHandlers
{
    static SqliteTypeHandlers()
    {
        SqlMapper.AddTypeHandler(new GuidHandler());
        SqlMapper.AddTypeHandler(new DateOnlyHandler());
        SqlMapper.AddTypeHandler(new UtcDateTimeHandler());
    }

    public static void Register()
    {
        // Accessing this method triggers the static constructor exactly once.
    }

    private sealed class GuidHandler : SqlMapper.TypeHandler<Guid>
    {
        public override Guid Parse(object value) => value switch
        {
            Guid guid => guid,
            string text => Guid.ParseExact(text, "D"),
            _ => throw new DataException("Unexpected SQLite GUID representation.")
        };

        public override void SetValue(IDbDataParameter parameter, Guid value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("D");
        }
    }

    private sealed class DateOnlyHandler : SqlMapper.TypeHandler<DateOnly>
    {
        public override DateOnly Parse(object value) => value switch
        {
            string text => DateOnly.ParseExact(
                text, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            _ => throw new DataException("Unexpected SQLite date representation.")
        };

        public override void SetValue(IDbDataParameter parameter, DateOnly value)
        {
            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }

    private sealed class UtcDateTimeHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override DateTime Parse(object value)
        {
            if (value is not string text ||
                !DateTime.TryParseExact(
                    text, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var result) ||
                result.Kind != DateTimeKind.Utc)
            {
                throw new DataException("Expected an ISO 8601 UTC timestamp from SQLite.");
            }

            return result;
        }

        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            if (value.Kind != DateTimeKind.Utc)
            {
                throw new DataException("SQLite timestamps must be in UTC.");
            }

            parameter.DbType = DbType.String;
            parameter.Value = value.ToString("O", CultureInfo.InvariantCulture);
        }
    }
}