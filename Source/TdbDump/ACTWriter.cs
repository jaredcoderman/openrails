using System;
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
            // startNode / endNode kept for API compatibility with ScenarioWriter;
            // player placement comes from the path, not from restricted zones.
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
                // No ActivityRestrictedSpeedZones: OR places those via Traveller
                // and crashes if the coords are off the TDB (common with stale
                // test activities). Speed zones are optional for a player run.

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
