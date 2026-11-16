using System;
using System.Globalization;
using Newtonsoft.Json;

namespace AmenityExtractor
{
    /// <summary>
    /// Writes the zonemap JSON in the format of the committed zonemaps in PSF-LoginServer: single-precision numbers with 7 significant digits when that
    /// round-trips and 9 otherwise (the way the .NET Framework wrote them), a ".0" on whole numbers, "\n" line endings and a trailing newline.
    /// Newer runtimes print the shortest round-trip form instead, which would change about every number in every file.
    /// </summary>
    public static class ZonemapJson
    {
        public static string Serialize(object value)
        {
            var settings = new JsonSerializerSettings { Formatting = Formatting.Indented };
            settings.Converters.Add(new SinglePrecisionConverter());
            return JsonConvert.SerializeObject(value, settings).Replace("\r\n", "\n") + "\n";
        }

        private class SinglePrecisionConverter : JsonConverter
        {
            public override bool CanRead => false;

            public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) => throw new NotSupportedException();

            public override bool CanConvert(Type objectType) => objectType == typeof(float) || objectType == typeof(double);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
            {
                var f = Convert.ToSingle(value, CultureInfo.InvariantCulture);
                if (float.IsNaN(f) || float.IsInfinity(f))
                {
                    writer.WriteValue(f);
                    return;
                }

                var text = f.ToString("G7", CultureInfo.InvariantCulture);
                if (float.Parse(text, CultureInfo.InvariantCulture) != f) text = f.ToString("G9", CultureInfo.InvariantCulture);
                if (text.IndexOf('E') >= 0)
                {
                    writer.WriteValue(f);
                    return;
                }

                if (text.IndexOf('.') < 0) text += ".0";
                writer.WriteRawValue(text);
            }
        }
    }
}
