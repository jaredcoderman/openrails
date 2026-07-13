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

                var sections = node.Sections != null && node.Sections.Count > 0
                    ? node.Sections
                    : new System.Collections.Generic.List<TrVectorSection> { node.Section };
                writer.WriteBlockStart("trvectorsections", sections.Count);

                foreach (var section in sections)
                {
                    writer.WriteNoLabel(section.ToTdbString());
                }

                writer.WriteBlockEnd();

                writer.WriteBlockStart("tritemrefs", 0);
                writer.WriteBlockEnd();

                writer.WriteBlockEnd();
            }

            // Write TrPins in side order. Inpins/Outpins identify the parent
            // node's side; TrPin.Direction identifies the side on the linked
            // node and must not be used to count these entries.
            if (node.Pins != null && node.Pins.Count > 0)
            {
                writer.WriteBlockStart("trpins", "1 1");
                
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

            // Write TrPins - End nodes should have exactly 1 input pin and 0 output pins
            writer.WriteBlockStart("trpins", "1 0");
            
            if (node.Pins != null && node.Pins.Count > 0)
            {
                foreach (var pin in node.Pins)
                {
                    writer.WriteProperty("TrPin", pin.Node.ToString() + " " + pin.Pin.ToString());
                }
            }

            writer.WriteBlockEnd();

            writer.WriteBlockEnd();
        }
    }
}
