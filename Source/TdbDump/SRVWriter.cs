using System;
using Orts.Parsers.Msts;

namespace TdbDump
{
    public static class SRVWriter
    {
        public static void Write(
            string filePath,
            string serviceName,
            string trainConfig,
            string pathId)
        {
            if (string.IsNullOrWhiteSpace(serviceName))
                throw new ArgumentException("A service name is required.", nameof(serviceName));
            if (string.IsNullOrWhiteSpace(trainConfig))
                throw new ArgumentException("A train configuration is required.", nameof(trainConfig));
            if (string.IsNullOrWhiteSpace(pathId))
                throw new ArgumentException("A path ID is required.", nameof(pathId));

            using (var writer = new STFWriter(filePath, "v0t"))
            {
                writer.WriteBlockStart("Service_Definition");
                writer.WriteProperty("Serial", 4);
                writer.WriteProperty("Name", Quote(serviceName));
                writer.WriteProperty("Train_Config", Quote(trainConfig));
                writer.WriteProperty("PathID", pathId);
                writer.WriteProperty("MaxWheelAcceleration", 0);
                writer.WriteNoLabel("Efficiency ( 0.95 )");

                writer.WriteBlockStart("TimeTable");
                writer.WriteProperty("StartingSpeed", 9);
                writer.WriteProperty("EndingSpeed", 0);
                writer.WriteProperty("StartInWorld", 0);
                writer.WriteProperty("EndInWorld", 0);
                writer.WriteBlockEnd();

                writer.WriteBlockEnd();
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
