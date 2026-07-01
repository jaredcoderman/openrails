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

                List<TrackNode> nodes = track.Build();

                using (var writer = new STFWriter(tdbPath))
                {
                    writer.WriteBlockStart("trackdb");
                    writer.WriteBlockStart("tracknodes", nodes.Count);

                    foreach (TrackNode node in nodes)
                    {
                        TDBWriter.WriteTrackNode(writer, node);
                    }

                    writer.WriteBlockEnd();
                    writer.WriteBlockEnd();
                }

                Console.WriteLine("Wrote TrackNodes to: " + tdbPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error writing tdb file: " + ex.Message);
                return 1;
            }
        }
    }
}
