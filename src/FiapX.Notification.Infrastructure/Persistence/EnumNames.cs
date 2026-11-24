using System.Text;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FiapX.Notification.Infrastructure.Persistence;

/// <summary>Stores enums as UPPER_SNAKE_CASE text (VideoFailed → VIDEO_FAILED), as in db/schema.sql.</summary>
internal static class EnumNames
{
    public static ValueConverter<TEnum, string> Converter<TEnum>()
        where TEnum : struct, Enum =>
        new(value => ToDatabase(value), text => FromDatabase<TEnum>(text));

    public static string ToDatabase<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        var name = value.ToString();
        var builder = new StringBuilder(name.Length + 4);
        for (var i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]))
            {
                builder.Append('_');
            }

            builder.Append(char.ToUpperInvariant(name[i]));
        }

        return builder.ToString();
    }

    public static TEnum FromDatabase<TEnum>(string text)
        where TEnum : struct, Enum =>
        Enum.Parse<TEnum>(text.Replace("_", string.Empty, StringComparison.Ordinal), ignoreCase: true);
}
