using System;
using System.Globalization;
using Orts.Parsers.Msts;

namespace TdbDump
{
    public static class ACTWriter
    {
        public static void Write(
            string filePath,
            TrEndNode startNode,
            TrEndNode endNode,
            string routeId,
            string activityName,
            string serviceId,
            string pathId)
        {
            if (startNode == null)
                throw new ArgumentNullException(nameof(startNode));
            if (endNode == null)
                throw new ArgumentNullException(nameof(endNode));

            using (var writer = new STFWriter(filePath, "a0t"))
            {
                writer.WriteBlockStart("Tr_Activity");
                writer.WriteProperty("Serial", 13);

                writer.WriteBlockStart("Tr_Activity_Header");
                writer.WriteProperty("RouteID", routeId);
                writer.WriteProperty("Name", Quote(activityName));
                writer.WriteProperty("Description", Quote("Auto-generated track activity."));
                writer.WriteProperty("Briefing", Quote("Activity generated automatically for the generated track."));
                writer.WriteProperty("CompleteActivity", 1);
                writer.WriteProperty("Type", 0);
                writer.WriteProperty("Mode", 2);
                writer.WriteNoLabel("StartTime ( 8 0 0 )");
                writer.WriteProperty("Season", 1);
                writer.WriteProperty("Weather", 0);
                writer.WriteProperty("PathID", pathId);
                writer.WriteProperty("StartingSpeed", 0);
                writer.WriteNoLabel("Duration ( 0 0 )");
                writer.WriteProperty("Difficulty", 0);
                writer.WriteProperty("FuelWater", 100);
                writer.WriteProperty("FuelCoal", 100);
                writer.WriteProperty("FuelDiesel", 100);
                writer.WriteBlockEnd();

                writer.WriteBlockStart("Tr_Activity_File");
                writer.WriteBlockStart("Player_Service_Definition", serviceId);
                writer.WriteProperty("Player_Traffic_Definition", 79200);
                writer.WriteProperty("UiD", 0);
                writer.WriteBlockEnd();

                writer.WriteProperty("NextServiceUID", 2);
                writer.WriteProperty("NextActivityObjectUID", 32768);
                writer.WriteProperty("ORTSAIHornAtCrossings", 1);
                writer.WriteProperty("ORTSAICrossingHornPattern", "US");

                writer.WriteBlockStart("ActivityRestrictedSpeedZones");
                writer.WriteBlockStart("ActivityRestrictedSpeedZone");
                writer.WriteNoLabel("StartPosition ( " + FormatPosition(startNode) + " )");
                writer.WriteNoLabel("EndPosition ( " + FormatPosition(endNode) + " )");
                writer.WriteBlockEnd();
                writer.WriteBlockEnd();

                writer.WriteBlockEnd();
                writer.WriteBlockEnd();
            }
        }

        private static string FormatPosition(TrEndNode node)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0} {1} {2} {3}",
                node.TileX,
                node.TileZ,
                node.X,
                node.Z);
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
