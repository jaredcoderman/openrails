using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;
using static System.Collections.Specialized.BitVector32;

namespace TdbDump
{


    internal class Program
    {

        static int Main(string[] args)
        {
            string basePath = @"C:\Users\jared\ORRoutes\BNSF Starter Route - Copy\ROUTES\BNSF_Scenic";
            string tsectionPath = Path.Combine(basePath, "tsection.dat");
            string tdbPath = Path.Combine(basePath, "BNSF_Scenic.tdb");
            const string pathId = "TestPat";
            const string serviceId = "TestSRV";

            TrackBuilder track = new TrackBuilder();

            // Write TSectionDat to separate file
            try
            {
                using (var writer = new STFWriter(tsectionPath))
                {
                    TSectionWriter.UpdateTSectionDat(writer, track.Primitives.ToArray());
                    writer.WriteBlockEnd();
                }
                Console.WriteLine("Wrote TrackSections to: " + tsectionPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing tsection.dat: " + ex.Message);
                return 1;
            }
        
            // Build the complete node list once so both the TDB and PAT
            // writers use the same generated endpoints.
            List<object> allNodes = null;

            // Write TrackNodes to TDB file
            try
            {
                // Add track nodes
                foreach (var primitive in track.Primitives)
                {
                    if (primitive.Type == "straight")
                    {
                        track.AddStraight(primitive.SectionIndex);
                    }
                    else if (primitive.Type == "curve")
                    {
                        track.AddCurve(primitive.SectionIndex);
                    }
                }

                // Get all nodes (vector nodes + end nodes)
                allNodes = track.BuildAllNodes();

                using (var writer = new STFWriter(tdbPath))
                {
                    writer.WriteBlockStart("trackdb");
                    writer.WriteBlockStart("tracknodes", allNodes.Count);

                    foreach (object node in allNodes)
                    {
                        if (node is TrEndNode endNode)
                        {
                            TDBWriter.WriteEndNode(writer, endNode);
                        }
                        else if (node is TrackNode vectorNode)
                        {
                            TDBWriter.WriteVectorNode(writer, vectorNode);
                        }
                    }

                    writer.WriteBlockEnd();
                    
                    // Write empty tritemtable
                    writer.WriteBlockStart("tritemtable", 0);
                    writer.WriteBlockEnd();

                    writer.WriteBlockEnd();
                }


                Console.WriteLine("Wrote TrackNodes to: " + tdbPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing tdb file: " + ex.Message);
                return 1;
            }

            // Write a player path through the generated track sections.
            try
            {
                string pathsDirectory = Path.Combine(basePath, "PATHS");
                string patPath = Path.Combine(pathsDirectory, "TestPat.pat");
                TrackNode[] sectionNodes = track.Build().ToArray();
                TrEndNode endNode = allNodes.OfType<TrEndNode>().Last();

                PATWriter.Write(
                    patPath,
                    sectionNodes,
                    endNode,
                    pathId,
                    "Test Track",
                    "Start",
                    "End");

                Console.WriteLine("Wrote path to: " + patPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing pat file: " + ex.Message);
                return 1;
            }

            // Write the service that references the generated PAT file.
            try
            {
                string servicesDirectory = Path.Combine(basePath, "SERVICES");
                string srvPath = Path.Combine(servicesDirectory, serviceId + ".srv");

                SRVWriter.Write(
                    srvPath,
                    "Test Track",
                    "BNSF Manifest (60 cars)",
                    pathId);

                Console.WriteLine("Wrote service to: " + srvPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing srv file: " + ex.Message);
                return 1;
            }

            // Write an activity that uses the generated service and TDB
            // endpoints for its restricted speed zone.
            try
            {
                string activitiesDirectory = Path.Combine(basePath, "ACTIVITIES");
                string actPath = Path.Combine(activitiesDirectory, "TestActivity.act");
                TrEndNode startNode = allNodes.OfType<TrEndNode>().First();
                TrEndNode endNode = allNodes.OfType<TrEndNode>().Last();

                ACTWriter.Write(
                    actPath,
                    startNode,
                    endNode,
                    "BNSF_Scenic",
                    "Test Track AUTO",
                    serviceId,
                    "TesawdawdtTrack");

                Console.WriteLine("Wrote activity to: " + actPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing act file: " + ex.Message);
                return 1;
            }

            // Write DynamicTracks to World Files
            try
            {
                // Get the vector nodes (before end nodes are added) for DynamicTrack creation
                List<TrackNode> vectorNodes = track.Build();
               var dynamicTracks = DynamicTrack.MakeDynamicTrackObjects(
                    vectorNodes,
                    track.Primitives);
                WorldWriter.WriteWorldFiles(dynamicTracks);
                Console.WriteLine(dynamicTracks.Count + " dynamic tracks written");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing world files: " + ex.Message);
                return 1;
            }
        }
    }
}
