using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TdbDump
{
    public static class ScenarioWriter
    {
        private const string PathId = "TestPat";
        private const string ServiceId = "TestSRV";
        private const string ActivityId = "TestActivity";
        private const string ActivityPathId = "TesawdawdtTrack";

        public static void Write(
            string routeDirectory,
            IReadOnlyList<TrackNode> sectionNodes,
            IReadOnlyList<object> allNodes)
        {
            if (sectionNodes == null)
                throw new ArgumentNullException(nameof(sectionNodes));
            if (allNodes == null)
                throw new ArgumentNullException(nameof(allNodes));

            TrEndNode startNode = allNodes.OfType<TrEndNode>().First();
            TrEndNode endNode = allNodes.OfType<TrEndNode>().Last();

            string pathsDirectory = Path.Combine(routeDirectory, "PATHS");
            string servicesDirectory = Path.Combine(routeDirectory, "SERVICES");
            string activitiesDirectory = Path.Combine(routeDirectory, "ACTIVITIES");

            string patPath = Path.Combine(pathsDirectory, PathId + ".pat");
            PATWriter.Write(
                patPath,
                sectionNodes,
                endNode,
                PathId,
                "Test Track",
                "Start",
                "End");
            Console.WriteLine("Wrote path to: " + patPath);

            string srvPath = Path.Combine(servicesDirectory, ServiceId + ".srv");
            SRVWriter.Write(
                srvPath,
                "Test Track",
                "BNSF Manifest (60 cars)",
                PathId);
            Console.WriteLine("Wrote service to: " + srvPath);

            string actPath = Path.Combine(activitiesDirectory, ActivityId + ".act");
            ACTWriter.Write(
                actPath,
                startNode,
                endNode,
                "BNSF_Scenic",
                "Test Track AUTO",
                ServiceId,
                ActivityPathId);
            Console.WriteLine("Wrote activity to: " + actPath);
        }
    }
}
