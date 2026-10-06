using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Baba.Infrastructure.Persistence;

/// <summary>Times are stored in UTC and always come back marked as UTC.</summary>
public sealed class UtcDateTimeConverter()
    : ValueConverter<DateTime, DateTime>(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

public sealed class NullableUtcDateTimeConverter()
    : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
