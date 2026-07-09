using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Orts.Formats.Msts;
using Orts.Parsers.Msts;

namespace TdbDump
{
    class TDBWriter
    {
        public static void WriteVectorNode(
            STFWriter writer,
            TrackNode node)
        {
            writer.WriteBlockStart("tracknode", node.Id);

            // Write TrVectorNode if section exists
            if (node.Section != null)
            {
                writer.WriteBlockStart("trvectornode");

                writer.WriteBlockStart("trvectorsections", 1);

                writer.WriteNoLabel(node.Section.ToTdbString());

                writer.WriteBlockEnd();

                writer.WriteBlockEnd();
            }

            // Write TrPins with actual pin data
            // Count input and output pins: input pins come first, then output pins
            // For a vector node, typically: 1 input, 1 output (or similar pattern)
            if (node.Pins != null && node.Pins.Count > 0)
            {
                // Separate pins into input and output
                // Based on typical track node patterns, first pin is usually input, rest are output
                int inPinCount = 1;
                int outPinCount = node.Pins.Count - 1;
                
                writer.WriteBlockStart("trpins", inPinCount.ToString() + " " + outPinCount.ToString());
                
                foreach (var pin in node.Pins)
                {
                    writer.WriteProperty("TrPin", pin.Node.ToString() + " " + pin.Pin.ToString());
                }

                writer.WriteBlockEnd();
            }
            else
            {
                // No pins - write empty pins block if needed
                writer.WriteBlockStart("trpins", "0 0");
                writer.WriteBlockEnd();
            }

            writer.WriteBlockEnd();
        }

        public static void WriteEndNode(
            STFWriter writer,
            TrEndNode node)
        {
            writer.WriteBlockStart("tracknode", node.Id);

            writer.WriteNoLabel("trendnode ( 0 )");

            // Write UiD: WorldTileX WorldTileZ WorldId Unknown TileX TileZ X Y Z AX AY AZ
            writer.WriteBlockStart("uid");
            writer.WriteNoLabel(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} {1} {2} 1 {3} {4} {5} {6} {7} {8} {9} {10}",
                node.TileX,      // WorldTileX
                node.TileZ,      // WorldTileZ
                node.Id,         // WorldId (use node ID)
                node.TileX,      // TileX
                node.TileZ,      // TileZ
                node.X.ToString(System.Globalization.CultureInfo.InvariantCulture),
                node.Y.ToString(System.Globalization.CultureInfo.InvariantCulture),
                node.Z.ToString(System.Globalization.CultureInfo.InvariantCulture),
                node.AX.ToString(System.Globalization.CultureInfo.InvariantCulture),
                node.AY.ToString(System.Globalization.CultureInfo.InvariantCulture),
                node.AZ.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            writer.WriteBlockEnd();

            // Write TrPins
            if (node.Pins != null && node.Pins.Count > 0)
            {
                int inPinCount = Math.Min(1, node.Pins.Count);  // End nodes typically have 1 input pin
                int outPinCount = Math.Max(0, node.Pins.Count - inPinCount);
                
                writer.WriteBlockStart("trpins", inPinCount.ToString() + " " + outPinCount.ToString());
                
                foreach (var pin in node.Pins)
                {
                    writer.WriteProperty("TrPin", pin.Node.ToString() + " " + pin.Pin.ToString());
                }

                writer.WriteBlockEnd();
            }
            else
            {
                writer.WriteBlockStart("trpins", "0 0");
                writer.WriteBlockEnd();
            }

            writer.WriteBlockEnd();
        }
    }
}
