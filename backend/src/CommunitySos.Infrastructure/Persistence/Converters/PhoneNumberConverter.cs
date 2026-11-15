using CommunitySos.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CommunitySos.Infrastructure.Persistence.Converters;

public sealed class PhoneNumberConverter() : ValueConverter<PhoneNumber, string>(
    v => v.Value,
    v => PhoneNumber.FromPersistence(v));
