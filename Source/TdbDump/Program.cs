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
                List<object> allNodes = track.BuildAllNodes();

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
