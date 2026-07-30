using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace TdbDump
{
    public class TrackBuilder
    {
        private const int BaseTileX = -12842;
        private const int BaseTileZ = 14734;
        private const string NetworkFileName = "bbox_network_local.json";
        private const string LegacyPrimitivesFileName = "primitives.json";

        // Match using true GeoJSON endpoints. Reconstruction drift can be hundreds
        // of meters even when the source polylines meet; AlignLinkedChains then
        // translates pairs/clusters, and only tiny leftovers get short fillers.
        private const float EndpointSnapMeters = 25f;

        // Tips this close are always a real shared vertex. Parallel mains are
        // typically ~12–20 m apart — those must not snap into one rail.
        private const float HardJoinMeters = 3f;
        private const float ParallelHeadingAlign = 0.92f;
        private const float ParallelMaxAlongTrack = 0.45f;

        private float _x;
        private float _z;
        private float _ay;
        private int _nextSectionOrdinal = 1;

        private readonly List<TrackNode> _nodes = new List<TrackNode>();
        private readonly List<FeatureChain> _chains = new List<FeatureChain>();
        private readonly Dictionary<uint, TrackPrimitive> _primitives = new Dictionary<uint, TrackPrimitive>();

        public IReadOnlyCollection<TrackPrimitive> Primitives => _primitives.Values;
        public IReadOnlyList<FeatureChain> Chains => _chains;

        public TrackBuilder()
            : this(FindInputFile(NetworkFileName) ?? FindInputFile(LegacyPrimitivesFileName))
        {
        }

        public TrackBuilder(string inputPath)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                throw new FileNotFoundException(
                    "Could not find bbox_network_local.json or primitives.json.",
                    inputPath);

            if (Path.GetFileName(inputPath).Equals(NetworkFileName, StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(inputPath).IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                BuildFromNetwork(inputPath);
            }
            else
            {
                BuildFromLegacyPrimitives(inputPath);
            }
        }

        public List<TrackNode> Build()
        {
            return _nodes;
        }

        public List<object> BuildAllNodes()
        {
            var allNodes = new List<object>();
            if (_chains.Count == 0)
                return allNodes;

            int worldUiD = 1;
            var activeChains = _chains.Where(c => c.Sections.Count > 0).ToList();

            // Reserve vector-node IDs first so endpoint links can reference them.
            int nextId = 1;
            foreach (var chain in activeChains)
                chain.VectorNodeId = nextId++;

            Dictionary<EndpointKey, EndpointLink> links = FindEndpointLinks(activeChains);
            int translated = AlignLinkedChains(activeChains, links);
            // Tree translate leaves cycle-chord residuals: topology says connected
            // but endpoints can still be hundreds of meters apart. Reseat the last
            // (or first) section onto the partner — never append a reverse or
            // collinear twin straight (those draw as duplicated long straights).
            int linkReseats = CloseLinkedResiduals(activeChains, links);
            int orphanReseats = CloseSmallResidualGaps(activeChains, links);
            Console.WriteLine(
                "Endpoint snap (" + EndpointSnapMeters + "m geo): "
                + links.Count + " links, "
                + translated + " chains translated, "
                + linkReseats + " link reseats, "
                + orphanReseats + " orphan reseats, "
                + activeChains.Count + " features");

            // Junctions reshape tip geometry on chains — must run before we
            // snapshot vector section lists for the TDB write.
            var junctionSides = new Dictionary<EndpointKey, (int JunctionId, int JunctionSide)>();
            int junctionsCreated = CreateJunctionNodes(
                activeChains, links, allNodes, ref nextId, junctionSides);
            if (junctionsCreated > 0)
            {
                Console.WriteLine(
                    "Junctions: " + junctionsCreated + " TrJunctionNode(s) for 3-way clusters");
            }

            // Junction reshape / tip adjust can open holes between consecutive
            // DynTrack sections (right heading, missing abutment). Close those,
            // then re-seat any 1:1 link residuals the reshape disturbed.
            int abutmentFixes = RepairChainAbutments(activeChains);
            int postJunctionReseats = CloseLinkedResiduals(activeChains, links);
            int postJunctionOrphans = CloseSmallResidualGaps(activeChains, links);
            if (abutmentFixes + postJunctionReseats + postJunctionOrphans > 0)
            {
                Console.WriteLine(
                    "Post-junction close: "
                    + abutmentFixes + " abutment fix(es), "
                    + postJunctionReseats + " link reseat(s), "
                    + postJunctionOrphans + " orphan reseat(s)");
            }

            ReportResidualGaps(activeChains, links);

            // WFName + UiD must match the Dyntrack Open Rails loads: world file
            // w{TileX}{TileZ}.w, object UiD. UiDs are unique within each tile.
            var sectionsByTile = activeChains
                .SelectMany(c => c.Sections)
                .Select(n => n.Section)
                .Where(s => s != null)
                .GroupBy(s => (s.TileX, s.TileZ));
            foreach (var tileGroup in sectionsByTile)
            {
                int tileUiD = 1;
                foreach (var section in tileGroup)
                {
                    section.WFNameX = section.TileX.ToString();
                    section.WFNameZ = section.TileZ.ToString();
                    section.WorldFileUiD = tileUiD++;
                    worldUiD++;
                }
            }

            var vectors = new Dictionary<int, TrackNode>();
            foreach (var chain in activeChains)
            {
                var first = chain.Sections[0].Section;
                var vector = new TrackNode
                {
                    Id = chain.VectorNodeId,
                    Section = first,
                    Sections = new List<TrVectorSection>(
                        chain.Sections.ConvertAll(node => node.Section)),
                };
                vectors[chain.VectorNodeId] = vector;
            }

            // Side 0 = start of chain, side 1 = end of chain.
            foreach (var chain in activeChains)
            {
                TrackNode vector = vectors[chain.VectorNodeId];

                EndpointKey startKey = new EndpointKey(chain.ObjectId, isStart: true);
                EndpointKey endKey = new EndpointKey(chain.ObjectId, isStart: false);

                WireVectorSide(
                    chain, vector, startKey, isStart: true,
                    links, junctionSides, allNodes, ref nextId);
                WireVectorSide(
                    chain, vector, endKey, isStart: false,
                    links, junctionSides, allNodes, ref nextId);

                allNodes.Add(vector);
            }

            // Stable TDB order by node id.
            return allNodes.OrderBy(NodeId).ToList();
        }

        private void WireVectorSide(
            FeatureChain chain,
            TrackNode vector,
            EndpointKey key,
            bool isStart,
            Dictionary<EndpointKey, EndpointLink> links,
            Dictionary<EndpointKey, (int JunctionId, int JunctionSide)> junctionSides,
            List<object> allNodes,
            ref int nextId)
        {
            // TrPin.Direction selects which side of the LINKED node holds the
            // reciprocal: OR looks at (Direction == 0 ? 1 : 0). So Direction
            // must be (1 - otherSide).
            if (junctionSides.TryGetValue(key, out var junction))
            {
                // Stem is junction side 0 → Direction 1; outs (1/2) → Direction 0.
                int direction = junction.JunctionSide == 0 ? 1 : 0;
                vector.Pins.Add(new TrPin(junction.JunctionId, direction));
                return;
            }

            if (links.TryGetValue(key, out EndpointLink link))
            {
                vector.Pins.Add(new TrPin(link.OtherVectorId, link.OtherIsStart ? 1 : 0));
                return;
            }

            int endNodeId = nextId++;
            if (isStart)
            {
                var first = chain.Sections[0].Section;
                var startEnd = new TrEndNode
                {
                    Id = endNodeId,
                    TileX = first.TileX,
                    TileZ = first.TileZ,
                    X = first.X,
                    Y = first.Y,
                    Z = first.Z,
                    AY = first.AY,
                };
                startEnd.Pins.Add(new TrPin(chain.VectorNodeId, 1));
                vector.Pins.Add(new TrPin(endNodeId, 1));
                allNodes.Add(startEnd);
            }
            else
            {
                PlaceWorld(chain.EndX, chain.EndZ, out int endTileX, out int endTileZ, out float endLocalX, out float endLocalZ);
                var end = new TrEndNode
                {
                    Id = endNodeId,
                    TileX = endTileX,
                    TileZ = endTileZ,
                    X = endLocalX,
                    Y = chain.Sections[0].Section.Y,
                    Z = endLocalZ,
                    AY = chain.EndAy,
                };
                end.Pins.Add(new TrPin(chain.VectorNodeId, 0));
                vector.Pins.Add(new TrPin(endNodeId, 1));
                allNodes.Add(end);
            }
        }

        /// <summary>
        /// Replace 3-way geo clusters with TrJunctionNodes. Removes any greedy 1:1
        /// links among the cluster so those ends pin through the junction instead.
        /// </summary>
        private int CreateJunctionNodes(
            List<FeatureChain> chains,
            Dictionary<EndpointKey, EndpointLink> links,
            List<object> allNodes,
            ref int nextId,
            Dictionary<EndpointKey, (int JunctionId, int JunctionSide)> junctionSides)
        {
            var clusters = FindGeoEndpointClusters(chains);
            int created = 0;

            foreach (var cluster in clusters)
            {
                if (cluster.Count != 3)
                {
                    Console.WriteLine(
                        "Skipping " + cluster.Count
                        + "-way cluster (only 3-way junctions implemented)");
                    continue;
                }

                // Drop pairwise links inside the cluster — junction owns topology.
                foreach (var ep in cluster)
                {
                    var key = new EndpointKey(ep.ObjectId, ep.IsStart);
                    if (!links.TryGetValue(key, out EndpointLink link))
                        continue;
                    links.Remove(key);
                    links.Remove(new EndpointKey(link.OtherObjectId, link.OtherIsStart));
                }

                if (!AssignJunctionRoles(cluster, out var stem, out var main, out var diverging))
                    continue;

                // Rebuild each leg's tip on the geo heading so the spur keeps
                // its diverge angle. The diverging leg gets a longer rewrite —
                // fitted spur curves often swing into the through line before
                // the tip, which draws as overlapping track at the T.
                //
                // Exception: when main and diverge leave nearly parallel (double
                // track meeting a third rail), a 160 m diverge rewrite chops the
                // parallel and leaves DynTrack gaps — keep approaches short.
                float jx = stem.IsStart ? stem.Chain.StartX : stem.Chain.EndX;
                float jz = stem.IsStart ? stem.Chain.StartZ : stem.Chain.EndZ;
                float mainAy = main.IsStart ? main.Chain.GeoStartAy : main.Chain.GeoEndAy;
                float divAy = diverging.IsStart ? diverging.Chain.GeoStartAy : diverging.Chain.GeoEndAy;
                float mainOutX = main.IsStart ? (float)Math.Sin(mainAy) : -(float)Math.Sin(mainAy);
                float mainOutZ = main.IsStart ? (float)Math.Cos(mainAy) : -(float)Math.Cos(mainAy);
                float divOutX = diverging.IsStart ? (float)Math.Sin(divAy) : -(float)Math.Sin(divAy);
                float divOutZ = diverging.IsStart ? (float)Math.Cos(divAy) : -(float)Math.Cos(divAy);
                bool parallelLegs =
                    Math.Abs(mainOutX * divOutX + mainOutZ * divOutZ) > 0.85f;

                float stemApproach = CapJunctionApproach(stem.Chain, 60f);
                float mainApproach = CapJunctionApproach(main.Chain, parallelLegs ? 40f : 60f);
                float divApproach = CapJunctionApproach(diverging.Chain, parallelLegs ? 40f : 160f);

                ReshapeJunctionApproach(stem.Chain, stem.IsStart, jx, jz,
                    stem.IsStart ? stem.Chain.GeoStartAy : stem.Chain.GeoEndAy,
                    approachMeters: stemApproach);
                ReshapeJunctionApproach(main.Chain, main.IsStart, jx, jz,
                    main.IsStart ? main.Chain.GeoStartAy : main.Chain.GeoEndAy,
                    approachMeters: mainApproach);
                ReshapeJunctionApproach(diverging.Chain, diverging.IsStart, jx, jz,
                    diverging.IsStart ? diverging.Chain.GeoStartAy : diverging.Chain.GeoEndAy,
                    approachMeters: divApproach);

                jx = stem.IsStart ? stem.Chain.StartX : stem.Chain.EndX;
                jz = stem.IsStart ? stem.Chain.StartZ : stem.Chain.EndZ;
                float jay = stem.IsStart ? stem.Chain.StartAy : stem.Chain.EndAy;
                PlaceWorld(jx, jz, out int tileX, out int tileZ, out float localX, out float localZ);

                int junctionId = nextId++;
                var junction = new TrJunctionNode
                {
                    Id = junctionId,
                    ShapeIndex = 1,
                    TileX = tileX,
                    TileZ = tileZ,
                    X = localX,
                    Y = stem.Chain.Sections[0].Section.Y,
                    Z = localZ,
                    AY = jay,
                };

                // Pin order: in (stem), out0 (main), out1 (diverging).
                // Direction = opposite of the linked vector's connection side.
                junction.Pins.Add(new TrPin(stem.Chain.VectorNodeId, stem.IsStart ? 1 : 0));
                junction.Pins.Add(new TrPin(main.Chain.VectorNodeId, main.IsStart ? 1 : 0));
                junction.Pins.Add(new TrPin(diverging.Chain.VectorNodeId, diverging.IsStart ? 1 : 0));
                allNodes.Add(junction);

                junctionSides[new EndpointKey(stem.ObjectId, stem.IsStart)] = (junctionId, 0);
                junctionSides[new EndpointKey(main.ObjectId, main.IsStart)] = (junctionId, 1);
                junctionSides[new EndpointKey(diverging.ObjectId, diverging.IsStart)] = (junctionId, 2);

                Console.WriteLine(
                    "  Junction " + junctionId
                    + ": stem oid " + stem.ObjectId + (stem.IsStart ? "S" : "E")
                    + ", main oid " + main.ObjectId + (main.IsStart ? "S" : "E")
                    + ", div oid " + diverging.ObjectId + (diverging.IsStart ? "S" : "E"));
                created++;
            }

            return created;
        }

        private struct ClusterEndpoint
        {
            public int ObjectId;
            public bool IsStart;
            public FeatureChain Chain;
            public float Gx;
            public float Gz;
        }

        private static List<List<ClusterEndpoint>> FindGeoEndpointClusters(List<FeatureChain> chains)
        {
            var endpoints = new List<ClusterEndpoint>();
            foreach (var chain in chains)
            {
                endpoints.Add(new ClusterEndpoint
                {
                    ObjectId = chain.ObjectId,
                    IsStart = true,
                    Chain = chain,
                    Gx = chain.GeoStartX,
                    Gz = chain.GeoStartZ,
                });
                endpoints.Add(new ClusterEndpoint
                {
                    ObjectId = chain.ObjectId,
                    IsStart = false,
                    Chain = chain,
                    Gx = chain.GeoEndX,
                    Gz = chain.GeoEndZ,
                });
            }

            int n = endpoints.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
            void Union(int i, int j)
            {
                int ri = Find(i), rj = Find(j);
                if (ri != rj)
                    parent[rj] = ri;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (endpoints[i].ObjectId == endpoints[j].ObjectId)
                        continue;
                    if (!ShouldSnapTips(
                            endpoints[i].Chain, endpoints[i].IsStart,
                            endpoints[j].Chain, endpoints[j].IsStart))
                        continue;
                    Union(i, j);
                }
            }

            var groups = new Dictionary<int, List<ClusterEndpoint>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<ClusterEndpoint>();
                    groups[root] = list;
                }
                list.Add(endpoints[i]);
            }

            return groups.Values.Where(g => g.Count >= 3).ToList();
        }

        /// <summary>
        /// Through-route = pair whose outward headings are most opposite; that
        /// pair becomes stem(in)+main(out0), remaining leg is diverging(out1).
        /// </summary>
        private static bool AssignJunctionRoles(
            List<ClusterEndpoint> cluster,
            out ClusterEndpoint stem,
            out ClusterEndpoint main,
            out ClusterEndpoint diverging)
        {
            stem = default;
            main = default;
            diverging = default;
            if (cluster.Count != 3)
                return false;

            float OutX(ClusterEndpoint ep)
            {
                float ay = ep.IsStart ? ep.Chain.StartAy : ep.Chain.EndAy;
                // Start at junction: leaves along StartAy. End at junction: arrived
                // along EndAy, so outward from junction is the reverse.
                return ep.IsStart ? (float)Math.Sin(ay) : -(float)Math.Sin(ay);
            }
            float OutZ(ClusterEndpoint ep)
            {
                float ay = ep.IsStart ? ep.Chain.StartAy : ep.Chain.EndAy;
                return ep.IsStart ? (float)Math.Cos(ay) : -(float)Math.Cos(ay);
            }

            int bestI = 0, bestJ = 1;
            float bestDot = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                for (int j = i + 1; j < 3; j++)
                {
                    float dot = OutX(cluster[i]) * OutX(cluster[j]) + OutZ(cluster[i]) * OutZ(cluster[j]);
                    if (dot < bestDot)
                    {
                        bestDot = dot;
                        bestI = i;
                        bestJ = j;
                    }
                }
            }

            // Through pair is a/b; remaining tip is the diverge.
            ClusterEndpoint a = cluster[bestI];
            ClusterEndpoint b = cluster[bestJ];
            ClusterEndpoint divEp = default;
            for (int i = 0; i < 3; i++)
            {
                if (i != bestI && i != bestJ)
                {
                    divEp = cluster[i];
                    break;
                }
            }

            // Stem must be the facing/points end: arriving from stem, both main
            // and diverge continue forward. Picking the wrong through-end as stem
            // makes MAIN↔DIV the "shortest" graph path to the spur — invalid on a
            // real switch, so OR keeps the default main and never takes the branch.
            float ScoreStem(ClusterEndpoint candidateStem, ClusterEndpoint candidateMain)
            {
                float inX = -OutX(candidateStem);
                float inZ = -OutZ(candidateStem);
                float mainDot = OutX(candidateMain) * inX + OutZ(candidateMain) * inZ;
                float divDot = OutX(divEp) * inX + OutZ(divEp) * inZ;
                return mainDot + divDot;
            }

            if (ScoreStem(a, b) >= ScoreStem(b, a))
            {
                stem = a;
                main = b;
            }
            else
            {
                stem = b;
                main = a;
            }

            diverging = divEp;
            return true;
        }

        /// <summary>
        /// Short crossovers between parallel rails are often a single section
        /// with both tips in junctions. A normal approach rewrite would replace
        /// the whole span with a ~40 m stub that never reaches the other rail.
        /// </summary>
        private float CapJunctionApproach(FeatureChain chain, float desired)
        {
            float len = TotalChainLength(chain);
            if (len < 1f)
                return desired;
            // Keep at least ~55% of the feature as un-rewritten span.
            float maxApproach = Math.Max(8f, len * 0.35f);
            if (chain.Sections.Count <= 2)
                maxApproach = Math.Min(maxApproach, Math.Max(8f, len * 0.2f));
            return Math.Min(desired, maxApproach);
        }

        private float TotalChainLength(FeatureChain chain)
        {
            float sum = 0f;
            foreach (var node in chain.Sections)
                sum += SectionArcLength(node);
            return sum;
        }

        /// <summary>
        /// Pin a short connector's tip to the junction while keeping the far end
        /// (crossovers between parallel rails).
        /// </summary>
        private void SnapShortLegToJunction(
            FeatureChain chain,
            bool isStart,
            float junctionX,
            float junctionZ,
            float travelAy)
        {
            if (chain.Sections.Count == 0)
                return;

            if (isStart)
            {
                UpdateChainEndFromLastSection(chain);
                float endX = chain.EndX;
                float endZ = chain.EndZ;
                if (Distance(junctionX, junctionZ, endX, endZ) < 1f)
                    return;

                if (chain.Sections.Count == 1)
                {
                    TrackNode node = chain.Sections[0];
                    if (!_primitives.TryGetValue(node.Section.SectionIndex, out TrackPrimitive prim))
                        return;
                    ReseatSectionAsStraight(node, prim, junctionX, junctionZ, endX, endZ);
                    chain.StartX = junctionX;
                    chain.StartZ = junctionZ;
                    chain.StartAy = node.Section.AY;
                    chain.EndX = endX;
                    chain.EndZ = endZ;
                    chain.EndAy = node.Section.AY;
                    return;
                }

                if (!AdjustChainStartToTarget(chain, junctionX, junctionZ))
                    PrependFillerStraight(chain, junctionX, junctionZ);
                chain.StartX = junctionX;
                chain.StartZ = junctionZ;
                chain.StartAy = travelAy;
                UpdateChainEndFromLastSection(chain);
                return;
            }

            UpdateChainStartFromFirstSection(chain);
            float startX = chain.StartX;
            float startZ = chain.StartZ;
            if (Distance(junctionX, junctionZ, startX, startZ) < 1f)
                return;

            if (chain.Sections.Count == 1)
            {
                TrackNode node = chain.Sections[0];
                if (!_primitives.TryGetValue(node.Section.SectionIndex, out TrackPrimitive prim))
                    return;
                ReseatSectionAsStraight(node, prim, startX, startZ, junctionX, junctionZ);
                chain.StartX = startX;
                chain.StartZ = startZ;
                chain.StartAy = node.Section.AY;
                chain.EndX = junctionX;
                chain.EndZ = junctionZ;
                chain.EndAy = node.Section.AY;
                return;
            }

            if (!AdjustChainEndToTarget(chain, junctionX, junctionZ))
                AppendFillerStraight(chain, junctionX, junctionZ);
            chain.EndX = junctionX;
            chain.EndZ = junctionZ;
            chain.EndAy = travelAy;
            UpdateChainStartFromFirstSection(chain);
        }

        /// <summary>
        /// Strip tip sections covering <paramref name="approachMeters"/> and
        /// replace them with one straight on the geo approach heading so
        /// turnouts keep their diverge angle (and spur arcs don't cross through).
        /// </summary>
        private void ReshapeJunctionApproach(
            FeatureChain chain,
            bool isStart,
            float junctionX,
            float junctionZ,
            float travelAy,
            float approachMeters)
        {
            if (chain.Sections.Count == 0 || approachMeters < 1f)
                return;

            float chainLen = TotalChainLength(chain);
            // One-section (or tiny) connectors: pin the tip to the junction and
            // keep the far end — never rewrite the whole feature into a stub.
            if (chain.Sections.Count == 1 || chainLen < 100f)
            {
                SnapShortLegToJunction(chain, isStart, junctionX, junctionZ, travelAy);
                return;
            }

            float dirX = (float)Math.Sin(travelAy);
            float dirZ = (float)Math.Cos(travelAy);

            // Drop tip sections until we've cleared the approach window,
            // always leaving at least one section for the far end of the feature.
            float covered = 0f;
            int removeCount = 0;
            if (isStart)
            {
                for (int i = 0; i < chain.Sections.Count - 1; i++)
                {
                    covered += SectionArcLength(chain.Sections[i]);
                    removeCount++;
                    if (covered >= approachMeters)
                        break;
                }
            }
            else
            {
                for (int i = chain.Sections.Count - 1; i >= 1; i--)
                {
                    covered += SectionArcLength(chain.Sections[i]);
                    removeCount++;
                    if (covered >= approachMeters)
                        break;
                }
            }

            if (removeCount == 0)
                return;

            float approachLen = Math.Min(approachMeters, Math.Max(covered, 8f));
            approachLen = Math.Min(approachLen, chainLen * 0.4f);
            if (approachLen < 8f)
            {
                SnapShortLegToJunction(chain, isStart, junctionX, junctionZ, travelAy);
                return;
            }

            if (isStart)
            {
                float tipEndX = junctionX + approachLen * dirX;
                float tipEndZ = junctionZ + approachLen * dirZ;

                TrackNode tipNode = chain.Sections[0];
                if (!_primitives.TryGetValue(tipNode.Section.SectionIndex, out TrackPrimitive tipPrim))
                    return;

                for (int i = 0; i < removeCount; i++)
                    chain.Sections.RemoveAt(0);

                if (chain.Sections.Count == 0)
                {
                    ReseatSectionAsStraight(tipNode, tipPrim, junctionX, junctionZ, tipEndX, tipEndZ);
                    chain.Sections.Add(tipNode);
                    chain.StartX = junctionX;
                    chain.StartZ = junctionZ;
                    chain.StartAy = travelAy;
                    chain.EndX = tipEndX;
                    chain.EndZ = tipEndZ;
                    chain.EndAy = travelAy;
                    return;
                }

                // Removals leave StartX/Z pointing at the old tip — refresh from
                // the new first section or Adjust thinks the gap is already closed
                // and inserts a tiny filler while the remainder starts hundreds
                // of meters away (visible DynTrack holes with correct headings).
                UpdateChainStartFromFirstSection(chain);

                // Remainder starts at the former tip-follow joint; pull it onto
                // the new tip end, then reinsert the tip. If Adjust refuses
                // (curve reverse / long gap), force a filler so we never leave
                // a DynTrack hole between tip and remainder.
                if (!AdjustChainStartToTarget(chain, tipEndX, tipEndZ))
                    PrependFillerStraight(chain, tipEndX, tipEndZ);
                ReseatSectionAsStraight(tipNode, tipPrim, junctionX, junctionZ, tipEndX, tipEndZ);
                chain.Sections.Insert(0, tipNode);
                chain.StartX = junctionX;
                chain.StartZ = junctionZ;
                chain.StartAy = travelAy;
                UpdateChainEndFromLastSection(chain);
                return;
            }

            float tipStartX = junctionX - approachLen * dirX;
            float tipStartZ = junctionZ - approachLen * dirZ;

            TrackNode endTip = chain.Sections[chain.Sections.Count - 1];
            if (!_primitives.TryGetValue(endTip.Section.SectionIndex, out TrackPrimitive endPrim))
                return;

            for (int i = 0; i < removeCount; i++)
                chain.Sections.RemoveAt(chain.Sections.Count - 1);

            if (chain.Sections.Count == 0)
            {
                ReseatSectionAsStraight(endTip, endPrim, tipStartX, tipStartZ, junctionX, junctionZ);
                chain.Sections.Add(endTip);
                chain.StartX = tipStartX;
                chain.StartZ = tipStartZ;
                chain.StartAy = travelAy;
                chain.EndX = junctionX;
                chain.EndZ = junctionZ;
                chain.EndAy = travelAy;
                return;
            }

            UpdateChainEndFromLastSection(chain);
            if (!AdjustChainEndToTarget(chain, tipStartX, tipStartZ))
                AppendFillerStraight(chain, tipStartX, tipStartZ);
            ReseatSectionAsStraight(endTip, endPrim, tipStartX, tipStartZ, junctionX, junctionZ);
            chain.Sections.Add(endTip);
            chain.EndX = junctionX;
            chain.EndZ = junctionZ;
            chain.EndAy = travelAy;
        }

        private float SectionArcLength(TrackNode node)
        {
            if (!_primitives.TryGetValue(node.Section.SectionIndex, out TrackPrimitive prim))
                return 0f;
            return prim.IsCurve ? prim.Radius * Math.Abs(prim.Angle) : prim.Length;
        }

        private void UpdateChainEndFromLastSection(FeatureChain chain)
        {
            if (chain.Sections.Count == 0)
                return;
            TrackNode last = chain.Sections[chain.Sections.Count - 1];
            if (!_primitives.TryGetValue(last.Section.SectionIndex, out TrackPrimitive prim))
                return;
            GetSectionWorldEnd(last, prim, out float ex, out float ez);
            chain.EndX = ex;
            chain.EndZ = ez;
            if (prim.IsCurve)
                chain.EndAy = last.Section.AY + prim.SignedAngle;
            else
                chain.EndAy = last.Section.AY;
        }

        private void UpdateChainStartFromFirstSection(FeatureChain chain)
        {
            if (chain.Sections.Count == 0)
                return;
            TrackNode first = chain.Sections[0];
            SectionWorldStart(first.Section, out float sx, out float sz);
            chain.StartX = sx;
            chain.StartZ = sz;
            chain.StartAy = first.Section.AY;
        }

        private static void GetSectionWorldEnd(
            TrackNode node,
            TrackPrimitive prim,
            out float worldX,
            out float worldZ)
        {
            SectionWorldStart(node.Section, out float sx, out float sz);
            float ay = node.Section.AY;
            if (!prim.IsCurve)
            {
                worldX = sx + prim.Length * (float)Math.Sin(ay);
                worldZ = sz + prim.Length * (float)Math.Cos(ay);
                return;
            }

            float dx =
                prim.LocalEndX * (float)Math.Cos(ay) +
                prim.LocalEndZ * (float)Math.Sin(ay);
            float dz =
               -prim.LocalEndX * (float)Math.Sin(ay) +
                prim.LocalEndZ * (float)Math.Cos(ay);
            worldX = sx + dx;
            worldZ = sz + dz;
        }

        private static int NodeId(object node)
        {
            if (node is TrackNode vector)
                return vector.Id;
            if (node is TrEndNode end)
                return end.Id;
            if (node is TrJunctionNode junction)
                return junction.Id;
            return 0;
        }

        private static Dictionary<EndpointKey, EndpointLink> FindEndpointLinks(List<FeatureChain> chains)
        {
            var endpoints = new List<Endpoint>();
            foreach (var chain in chains)
            {
                endpoints.Add(new Endpoint
                {
                    ObjectId = chain.ObjectId,
                    VectorId = chain.VectorNodeId,
                    IsStart = true,
                    // Match on source polyline ends, not reconstructed ends.
                    X = chain.GeoStartX,
                    Z = chain.GeoStartZ,
                });
                endpoints.Add(new Endpoint
                {
                    ObjectId = chain.ObjectId,
                    VectorId = chain.VectorNodeId,
                    IsStart = false,
                    X = chain.GeoEndX,
                    Z = chain.GeoEndZ,
                });
            }

            var candidates = new List<(float Dist, Endpoint A, Endpoint B)>();
            var byId = chains.ToDictionary(c => c.ObjectId);
            for (int i = 0; i < endpoints.Count; i++)
            {
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    Endpoint a = endpoints[i];
                    Endpoint b = endpoints[j];
                    if (a.ObjectId == b.ObjectId)
                        continue;
                    if (!byId.TryGetValue(a.ObjectId, out FeatureChain chainA)
                        || !byId.TryGetValue(b.ObjectId, out FeatureChain chainB))
                        continue;
                    if (!ShouldSnapTips(chainA, a.IsStart, chainB, b.IsStart))
                        continue;

                    float dist = Distance(a.X, a.Z, b.X, b.Z);
                    candidates.Add((dist, a, b));
                }
            }

            candidates.Sort((left, right) => left.Dist.CompareTo(right.Dist));

            var used = new HashSet<EndpointKey>();
            var links = new Dictionary<EndpointKey, EndpointLink>();

            foreach (var candidate in candidates)
            {
                var keyA = new EndpointKey(candidate.A.ObjectId, candidate.A.IsStart);
                var keyB = new EndpointKey(candidate.B.ObjectId, candidate.B.IsStart);
                if (used.Contains(keyA) || used.Contains(keyB))
                    continue;

                used.Add(keyA);
                used.Add(keyB);

                links[keyA] = new EndpointLink
                {
                    OtherObjectId = candidate.B.ObjectId,
                    OtherVectorId = candidate.B.VectorId,
                    OtherIsStart = candidate.B.IsStart,
                };
                links[keyB] = new EndpointLink
                {
                    OtherObjectId = candidate.A.ObjectId,
                    OtherVectorId = candidate.A.VectorId,
                    OtherIsStart = candidate.A.IsStart,
                };
            }

            return links;
        }

        /// <summary>
        /// Translate chains so reconstructed endpoints coincide at geo-matched joints.
        /// Multi-way clusters (T-junctions) are aligned first so the junction stays
        /// clean; residual cycle error is left for small fillers elsewhere.
        /// </summary>
        private static int AlignLinkedChains(
            List<FeatureChain> chains,
            Dictionary<EndpointKey, EndpointLink> links)
        {
            var byObjectId = chains.ToDictionary(c => c.ObjectId);
            var anchored = new HashSet<int>();
            int translated = 0;

            translated += AlignMultiWayClusters(chains, anchored);

            // Each remaining connected component keeps its longest chain fixed,
            // then walks outward so reconstructed joints land on each other.
            while (anchored.Count < chains.Count)
            {
                FeatureChain seed = chains
                    .Where(c => !anchored.Contains(c.ObjectId))
                    .OrderByDescending(c => c.Sections.Count)
                    .FirstOrDefault();
                if (seed == null)
                    break;
                anchored.Add(seed.ObjectId);

                bool progressed = true;
                while (progressed)
                {
                    progressed = false;
                    foreach (var chain in chains)
                    {
                        if (anchored.Contains(chain.ObjectId))
                            continue;

                        EndpointKey startKey = new EndpointKey(chain.ObjectId, true);
                        EndpointKey endKey = new EndpointKey(chain.ObjectId, false);

                        bool found =
                            TryGetAnchoredPartner(
                                links, startKey, anchored, byObjectId,
                                out FeatureChain partner, out bool partnerIsStart, out bool ourIsStart)
                            || TryGetAnchoredPartner(
                                links, endKey, anchored, byObjectId,
                                out partner, out partnerIsStart, out ourIsStart);

                        if (!found)
                            continue;

                        float targetX = partnerIsStart ? partner.StartX : partner.EndX;
                        float targetZ = partnerIsStart ? partner.StartZ : partner.EndZ;
                        float sourceX = ourIsStart ? chain.StartX : chain.EndX;
                        float sourceZ = ourIsStart ? chain.StartZ : chain.EndZ;
                        float dx = targetX - sourceX;
                        float dz = targetZ - sourceZ;

                        if (Math.Abs(dx) > 0.01f || Math.Abs(dz) > 0.01f)
                        {
                            TranslateChain(chain, dx, dz);
                            translated++;
                        }

                        anchored.Add(chain.ObjectId);
                        progressed = true;
                    }
                }
            }

            return translated;
        }

        /// <summary>
        /// Force all geo-coincident endpoints in 3+ clusters to share one recon point.
        /// </summary>
        private static int AlignMultiWayClusters(List<FeatureChain> chains, HashSet<int> anchored)
        {
            var endpoints = new List<(FeatureChain Chain, bool IsStart, float Gx, float Gz)>();
            foreach (var chain in chains)
            {
                endpoints.Add((chain, true, chain.GeoStartX, chain.GeoStartZ));
                endpoints.Add((chain, false, chain.GeoEndX, chain.GeoEndZ));
            }

            int n = endpoints.Count;
            var parent = Enumerable.Range(0, n).ToArray();
            int Find(int i)
            {
                while (parent[i] != i)
                {
                    parent[i] = parent[parent[i]];
                    i = parent[i];
                }
                return i;
            }
            void Union(int i, int j)
            {
                int ri = Find(i), rj = Find(j);
                if (ri != rj)
                    parent[rj] = ri;
            }

            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    if (endpoints[i].Chain.ObjectId == endpoints[j].Chain.ObjectId)
                        continue;
                    if (!ShouldSnapTips(
                            endpoints[i].Chain, endpoints[i].IsStart,
                            endpoints[j].Chain, endpoints[j].IsStart))
                        continue;
                    Union(i, j);
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!groups.TryGetValue(root, out var list))
                {
                    list = new List<int>();
                    groups[root] = list;
                }
                list.Add(i);
            }

            int translated = 0;
            foreach (var group in groups.Values)
            {
                if (group.Count < 3)
                    continue;

                // Meeting point from the longest chain's endpoint in the cluster.
                int seedIdx = group
                    .OrderByDescending(i => endpoints[i].Chain.Sections.Count)
                    .First();
                var seed = endpoints[seedIdx];
                float tx = seed.IsStart ? seed.Chain.StartX : seed.Chain.EndX;
                float tz = seed.IsStart ? seed.Chain.StartZ : seed.Chain.EndZ;
                anchored.Add(seed.Chain.ObjectId);

                foreach (int i in group)
                {
                    if (i == seedIdx)
                        continue;
                    var ep = endpoints[i];
                    float sx = ep.IsStart ? ep.Chain.StartX : ep.Chain.EndX;
                    float sz = ep.IsStart ? ep.Chain.StartZ : ep.Chain.EndZ;
                    float dx = tx - sx;
                    float dz = tz - sz;
                    if (Math.Abs(dx) > 0.01f || Math.Abs(dz) > 0.01f)
                    {
                        TranslateChain(ep.Chain, dx, dz);
                        translated++;
                    }
                    anchored.Add(ep.Chain.ObjectId);
                }
            }

            return translated;
        }

        private static bool TryGetAnchoredPartner(
            Dictionary<EndpointKey, EndpointLink> links,
            EndpointKey ourKey,
            HashSet<int> anchored,
            Dictionary<int, FeatureChain> byObjectId,
            out FeatureChain partner,
            out bool partnerIsStart,
            out bool ourIsStart)
        {
            partner = null;
            partnerIsStart = false;
            ourIsStart = ourKey.IsStart;

            if (!links.TryGetValue(ourKey, out EndpointLink link))
                return false;
            if (!anchored.Contains(link.OtherObjectId))
                return false;
            if (!byObjectId.TryGetValue(link.OtherObjectId, out partner))
                return false;

            partnerIsStart = link.OtherIsStart;
            return true;
        }

        /// <summary>
        /// After tree alignment, close any remaining gap on an existing 1:1 link
        /// by reseating the last/first section onto the partner. Appending a
        /// reverse or collinear twin straight draws as a duplicated corridor.
        /// </summary>
        private int CloseLinkedResiduals(
            List<FeatureChain> chains,
            Dictionary<EndpointKey, EndpointLink> links)
        {
            var byObjectId = chains.ToDictionary(c => c.ObjectId);
            var done = new HashSet<EndpointKey>();
            int reseats = 0;

            foreach (var kv in links.ToList())
            {
                EndpointKey key = kv.Key;
                if (done.Contains(key))
                    continue;

                EndpointLink link = kv.Value;
                var otherKey = new EndpointKey(link.OtherObjectId, link.OtherIsStart);
                done.Add(key);
                done.Add(otherKey);

                if (!byObjectId.TryGetValue(key.ObjectId, out FeatureChain chain))
                    continue;
                if (!byObjectId.TryGetValue(link.OtherObjectId, out FeatureChain other))
                    continue;

                float ax = key.IsStart ? chain.StartX : chain.EndX;
                float az = key.IsStart ? chain.StartZ : chain.EndZ;
                float bx = link.OtherIsStart ? other.StartX : other.EndX;
                float bz = link.OtherIsStart ? other.StartZ : other.EndZ;
                float gap = Distance(ax, az, bx, bz);
                if (gap < 0.5f)
                    continue;

                bool closed = false;
                // Prefer adjusting an end over a start.
                if (!key.IsStart)
                    closed = AdjustChainEndToTarget(chain, bx, bz)
                        || ForceCloseEnd(chain, isStart: false, bx, bz);
                else if (!link.OtherIsStart)
                    closed = AdjustChainEndToTarget(other, ax, az)
                        || ForceCloseEnd(other, isStart: false, ax, az);
                else
                    closed = AdjustChainStartToTarget(chain, bx, bz)
                        || ForceCloseEnd(chain, isStart: true, bx, bz);

                if (closed)
                    reseats++;
            }

            return reseats;
        }

        /// <summary>
        /// When Adjust refuses (curve reverse / long gap), still bridge with a
        /// filler so linked tips don't leave a visible DynTrack hole.
        /// </summary>
        private bool ForceCloseEnd(FeatureChain chain, bool isStart, float targetX, float targetZ)
        {
            float sx = isStart ? chain.StartX : chain.EndX;
            float sz = isStart ? chain.StartZ : chain.EndZ;
            float gap = Distance(sx, sz, targetX, targetZ);
            if (gap < 0.5f)
                return false;

            if (isStart)
            {
                PrependFillerStraight(chain, targetX, targetZ);
                return true;
            }

            AppendFillerStraight(chain, targetX, targetZ);
            return true;
        }

        /// <summary>
        /// Only close leftover geo-matched gaps that are already small. Large
        /// unmatched residuals are real corridor gaps or junctions (Step 4).
        /// </summary>
        private const float MaxOrphanFillerMeters = 120f;

        /// <summary>
        /// Append a short forward filler only when the last section is a curve
        /// and the gap is small; otherwise reseat the last section in place.
        /// </summary>
        private const float MaxCurveFillerMeters = 50f;

        private int CloseSmallResidualGaps(
            List<FeatureChain> chains,
            Dictionary<EndpointKey, EndpointLink> links)
        {
            int reseats = 0;

            foreach (var chain in chains.ToList())
            {
                reseats += TryCloseEnd(chain, isStart: true, chains, links);
                reseats += TryCloseEnd(chain, isStart: false, chains, links);
            }

            return reseats;
        }

        private int TryCloseEnd(
            FeatureChain chain,
            bool isStart,
            List<FeatureChain> chains,
            Dictionary<EndpointKey, EndpointLink> links)
        {
            var key = new EndpointKey(chain.ObjectId, isStart);
            if (links.ContainsKey(key))
                return 0;

            float gx = isStart ? chain.GeoStartX : chain.GeoEndX;
            float gz = isStart ? chain.GeoStartZ : chain.GeoEndZ;
            float sx = isStart ? chain.StartX : chain.EndX;
            float sz = isStart ? chain.StartZ : chain.EndZ;

            float bestGeo = float.MaxValue;
            FeatureChain bestOther = null;
            bool bestOtherIsStart = false;

            foreach (var other in chains)
            {
                if (other.ObjectId == chain.ObjectId)
                    continue;

                foreach (bool otherIsStart in new[] { true, false })
                {
                    float ogx = otherIsStart ? other.GeoStartX : other.GeoEndX;
                    float ogz = otherIsStart ? other.GeoStartZ : other.GeoEndZ;
                    float geoDist = Distance(gx, gz, ogx, ogz);
                    if (geoDist > EndpointSnapMeters || geoDist >= bestGeo)
                        continue;
                    if (!ShouldSnapTips(chain, isStart, other, otherIsStart))
                        continue;

                    bestGeo = geoDist;
                    bestOther = other;
                    bestOtherIsStart = otherIsStart;
                }
            }

            if (bestOther == null)
                return 0;

            float tx = bestOtherIsStart ? bestOther.StartX : bestOther.EndX;
            float tz = bestOtherIsStart ? bestOther.StartZ : bestOther.EndZ;
            float gap = Distance(sx, sz, tx, tz);
            if (gap < 0.5f || gap > MaxOrphanFillerMeters)
                return 0;

            if (isStart)
                return AdjustChainStartToTarget(chain, tx, tz) ? 1 : 0;
            return AdjustChainEndToTarget(chain, tx, tz) ? 1 : 0;
        }

        private uint NextSectionIndex()
        {
            uint next = 40001;
            if (_primitives.Count > 0)
                next = _primitives.Keys.Max() + 1;
            return next;
        }

        private static void SectionWorldStart(TrVectorSection section, out float worldX, out float worldZ)
        {
            worldX = (section.TileX - BaseTileX) * 2048f + section.X;
            worldZ = (section.TileZ - BaseTileZ) * 2048f + section.Z;
        }

        /// <summary>
        /// Rewrite a section as a straight from start→end (lengthen, shorten, or
        /// replace a curve). Avoids twin collinear / reverse filler sections.
        /// </summary>
        private void ReseatSectionAsStraight(
            TrackNode node,
            TrackPrimitive prim,
            float startX,
            float startZ,
            float endX,
            float endZ)
        {
            float dx = endX - startX;
            float dz = endZ - startZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            float ay = (float)Math.Atan2(dx, dz);

            prim.Type = "straight";
            prim.Length = length;
            prim.Radius = 0f;
            prim.Angle = 0f;
            prim.Clockwise = false;

            PlaceWorld(startX, startZ, out int tileX, out int tileZ, out float localX, out float localZ);
            node.Section.TileX = tileX;
            node.Section.TileZ = tileZ;
            node.Section.X = localX;
            node.Section.Z = localZ;
            node.Section.AY = ay;
            node.Section.WFNameX = tileX.ToString();
            node.Section.WFNameZ = tileZ.ToString();
        }

        private bool AdjustChainEndToTarget(FeatureChain chain, float targetX, float targetZ)
        {
            float gap = Distance(chain.EndX, chain.EndZ, targetX, targetZ);
            if (gap < 0.5f || chain.Sections.Count == 0)
                return false;

            TrackNode lastNode = chain.Sections[chain.Sections.Count - 1];
            TrVectorSection last = lastNode.Section;
            if (!_primitives.TryGetValue(last.SectionIndex, out TrackPrimitive prim))
                return false;

            SectionWorldStart(last, out float startX, out float startZ);
            float toTargetX = targetX - chain.EndX;
            float toTargetZ = targetZ - chain.EndZ;
            float endHx = (float)Math.Sin(chain.EndAy);
            float endHz = (float)Math.Cos(chain.EndAy);
            float forward = endHx * toTargetX + endHz * toTargetZ;
            bool wouldReverse = forward < 0f;
            bool longGap = gap > MaxCurveFillerMeters;

            if (prim.IsCurve)
            {
                // Same rule as within-feature: never chord-reseat a curve.
                if (wouldReverse || longGap)
                    return false;
                AppendFillerStraight(chain, targetX, targetZ);
                return true;
            }

            // Straight: lengthen/shorten onto the partner (covers reverse and
            // long residuals without creating a twin section).
            ReseatSectionAsStraight(lastNode, prim, startX, startZ, targetX, targetZ);
            chain.EndX = targetX;
            chain.EndZ = targetZ;
            chain.EndAy = last.AY;
            return true;
        }

        private bool AdjustChainStartToTarget(FeatureChain chain, float targetX, float targetZ)
        {
            float gap = Distance(chain.StartX, chain.StartZ, targetX, targetZ);
            if (gap < 0.5f || chain.Sections.Count == 0)
                return false;

            TrackNode firstNode = chain.Sections[0];
            TrVectorSection first = firstNode.Section;
            if (!_primitives.TryGetValue(first.SectionIndex, out TrackPrimitive prim))
                return false;

            float endX;
            float endZ;
            if (chain.Sections.Count == 1)
            {
                endX = chain.EndX;
                endZ = chain.EndZ;
            }
            else
            {
                SectionWorldStart(chain.Sections[1].Section, out endX, out endZ);
            }

            float toTargetX = chain.StartX - targetX;
            float toTargetZ = chain.StartZ - targetZ;
            float startHx = (float)Math.Sin(chain.StartAy);
            float startHz = (float)Math.Cos(chain.StartAy);
            // Incoming travel is along StartAy from the new start toward the old start.
            float forward = startHx * toTargetX + startHz * toTargetZ;
            bool wouldReverse = forward < 0f;
            bool longGap = gap > MaxCurveFillerMeters;

            if (prim.IsCurve)
            {
                if (wouldReverse || longGap)
                    return false;
                PrependFillerStraight(chain, targetX, targetZ);
                return true;
            }

            ReseatSectionAsStraight(firstNode, prim, targetX, targetZ, endX, endZ);
            chain.StartX = targetX;
            chain.StartZ = targetZ;
            chain.StartAy = first.AY;
            return true;
        }

        private void AppendFillerStraight(FeatureChain chain, float targetX, float targetZ)
        {
            float dx = targetX - chain.EndX;
            float dz = targetZ - chain.EndZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.001f)
                return;
            float ay = (float)Math.Atan2(dx, dz);

            uint sectionIndex = NextSectionIndex();
            var prim = new TrackPrimitive
            {
                SectionIndex = sectionIndex,
                Type = "straight",
                Length = length,
            };
            _primitives[sectionIndex] = prim;

            PlaceWorld(chain.EndX, chain.EndZ, out int tileX, out int tileZ, out float localX, out float localZ);
            var node = new TrackNode
            {
                Id = _nextSectionOrdinal++,
                Section = new TrVectorSection
                {
                    SectionIndex = sectionIndex,
                    TileX = tileX,
                    TileZ = tileZ,
                    X = localX,
                    Z = localZ,
                    AY = ay,
                },
            };
            chain.Sections.Add(node);
            _nodes.Add(node);

            chain.EndX = targetX;
            chain.EndZ = targetZ;
            chain.EndAy = ay;
        }

        private void PrependFillerStraight(FeatureChain chain, float targetX, float targetZ)
        {
            float dx = chain.StartX - targetX;
            float dz = chain.StartZ - targetZ;
            float length = (float)Math.Sqrt(dx * dx + dz * dz);
            if (length < 0.001f)
                return;
            float ay = (float)Math.Atan2(dx, dz);

            uint sectionIndex = NextSectionIndex();
            var prim = new TrackPrimitive
            {
                SectionIndex = sectionIndex,
                Type = "straight",
                Length = length,
            };
            _primitives[sectionIndex] = prim;

            PlaceWorld(targetX, targetZ, out int tileX, out int tileZ, out float localX, out float localZ);
            var node = new TrackNode
            {
                Id = _nextSectionOrdinal++,
                Section = new TrVectorSection
                {
                    SectionIndex = sectionIndex,
                    TileX = tileX,
                    TileZ = tileZ,
                    X = localX,
                    Z = localZ,
                    AY = ay,
                },
            };
            chain.Sections.Insert(0, node);
            _nodes.Add(node);

            chain.StartX = targetX;
            chain.StartZ = targetZ;
            chain.StartAy = ay;
        }

        private static void TranslateChain(FeatureChain chain, float dx, float dz)
        {
            chain.StartX += dx;
            chain.StartZ += dz;
            chain.EndX += dx;
            chain.EndZ += dz;

            foreach (var sectionNode in chain.Sections)
            {
                var section = sectionNode.Section;
                // Convert tile-local back to world, translate, then re-tile.
                float worldX = (section.TileX - BaseTileX) * 2048f + section.X;
                float worldZ = (section.TileZ - BaseTileZ) * 2048f + section.Z;
                PlaceWorld(worldX + dx, worldZ + dz, out int tileX, out int tileZ, out float localX, out float localZ);
                section.TileX = tileX;
                section.TileZ = tileZ;
                section.X = localX;
                section.Z = localZ;
                section.WFNameX = tileX.ToString();
                section.WFNameZ = tileZ.ToString();
            }
        }

        /// <summary>
        /// Translate sections [fromIndex..] so DynTrack abutments stay intact.
        /// </summary>
        private static void TranslateChainFromSection(FeatureChain chain, int fromIndex, float dx, float dz)
        {
            if (Math.Abs(dx) < 1e-4f && Math.Abs(dz) < 1e-4f)
                return;

            for (int j = fromIndex; j < chain.Sections.Count; j++)
            {
                var section = chain.Sections[j].Section;
                float worldX = (section.TileX - BaseTileX) * 2048f + section.X;
                float worldZ = (section.TileZ - BaseTileZ) * 2048f + section.Z;
                PlaceWorld(worldX + dx, worldZ + dz, out int tileX, out int tileZ, out float localX, out float localZ);
                section.TileX = tileX;
                section.TileZ = tileZ;
                section.X = localX;
                section.Z = localZ;
                section.WFNameX = tileX.ToString();
                section.WFNameZ = tileZ.ToString();
            }

            if (fromIndex == 0)
            {
                chain.StartX += dx;
                chain.StartZ += dz;
            }
            chain.EndX += dx;
            chain.EndZ += dz;
        }

        /// <summary>
        /// Fix DynTrack holes where consecutive sections have the right heading
        /// but don't meet (common after junction tip rewrite). Slide the remainder
        /// of the chain so section i+1 starts exactly at section i's end.
        /// </summary>
        private int RepairChainAbutments(List<FeatureChain> chains)
        {
            const float minGap = 0.15f;
            int fixes = 0;

            foreach (var chain in chains)
            {
                if (chain.Sections == null || chain.Sections.Count < 2)
                    continue;

                for (int i = 0; i < chain.Sections.Count - 1; i++)
                {
                    TrackNode a = chain.Sections[i];
                    TrackNode b = chain.Sections[i + 1];
                    if (!_primitives.TryGetValue(a.Section.SectionIndex, out TrackPrimitive pa))
                        continue;
                    if (!_primitives.TryGetValue(b.Section.SectionIndex, out TrackPrimitive _))
                        continue;

                    GetSectionWorldEnd(a, pa, out float ex, out float ez);
                    SectionWorldStart(b.Section, out float sx, out float sz);
                    float gap = Distance(ex, ez, sx, sz);
                    if (gap < minGap)
                        continue;

                    Console.WriteLine(
                        "  Abutment gap oid " + chain.ObjectId
                        + " [" + i + "->" + (i + 1) + "]: " + gap.ToString("0.00") + "m — sliding remainder");

                    TranslateChainFromSection(chain, i + 1, ex - sx, ez - sz);
                    fixes++;
                }

                if (chain.Sections.Count > 0)
                {
                    TrackNode first = chain.Sections[0];
                    SectionWorldStart(first.Section, out float fx, out float fz);
                    chain.StartX = fx;
                    chain.StartZ = fz;
                    chain.StartAy = first.Section.AY;
                    UpdateChainEndFromLastSection(chain);
                }
            }

            return fixes;
        }

        private void ReportResidualGaps(
            List<FeatureChain> chains,
            Dictionary<EndpointKey, EndpointLink> links)
        {
            var byObjectId = chains.ToDictionary(c => c.ObjectId);
            int linkGaps = 0;
            int freeGaps = 0;

            var reported = new HashSet<EndpointKey>();
            foreach (var kv in links)
            {
                EndpointKey key = kv.Key;
                if (reported.Contains(key))
                    continue;
                EndpointLink link = kv.Value;
                var otherKey = new EndpointKey(link.OtherObjectId, link.OtherIsStart);
                reported.Add(key);
                reported.Add(otherKey);

                if (!byObjectId.TryGetValue(key.ObjectId, out FeatureChain chain))
                    continue;
                if (!byObjectId.TryGetValue(link.OtherObjectId, out FeatureChain other))
                    continue;

                float ax = key.IsStart ? chain.StartX : chain.EndX;
                float az = key.IsStart ? chain.StartZ : chain.EndZ;
                float bx = link.OtherIsStart ? other.StartX : other.EndX;
                float bz = link.OtherIsStart ? other.StartZ : other.EndZ;
                float gap = Distance(ax, az, bx, bz);
                if (gap < 0.5f)
                    continue;
                linkGaps++;
                Console.WriteLine(
                    "  Residual LINK gap "
                    + key.ObjectId + (key.IsStart ? "S" : "E")
                    + "<->"
                    + link.OtherObjectId + (link.OtherIsStart ? "S" : "E")
                    + ": " + gap.ToString("0.00") + "m");
            }

            // Within-chain abutments still open
            foreach (var chain in chains)
            {
                for (int i = 0; i < chain.Sections.Count - 1; i++)
                {
                    TrackNode a = chain.Sections[i];
                    TrackNode b = chain.Sections[i + 1];
                    if (!_primitives.TryGetValue(a.Section.SectionIndex, out TrackPrimitive pa))
                        continue;
                    GetSectionWorldEnd(a, pa, out float ex, out float ez);
                    SectionWorldStart(b.Section, out float sx, out float sz);
                    float gap = Distance(ex, ez, sx, sz);
                    if (gap < 0.5f)
                        continue;
                    freeGaps++;
                    Console.WriteLine(
                        "  Residual ABUT oid " + chain.ObjectId
                        + " [" + i + "->" + (i + 1) + "]: " + gap.ToString("0.00") + "m");
                }
            }

            if (linkGaps + freeGaps == 0)
                Console.WriteLine("Residual gaps: none (>0.5m)");
            else
                Console.WriteLine(
                    "Residual gaps: " + linkGaps + " link, " + freeGaps + " abutment");
        }

        private static float Distance(float x0, float z0, float x1, float z1)
        {
            float dx = x0 - x1;
            float dz = z0 - z1;
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>
        /// True when two tips within snap range sit on parallel tracks (lateral
        /// offset) rather than at a joint. Prevents double-track centerlines
        /// from being merged / translated onto each other.
        /// </summary>
        private static bool IsLateralParallelGap(
            float ax, float az, float aAy,
            float bx, float bz, float bAy,
            float dist)
        {
            if (dist <= HardJoinMeters || dist < 1e-6f)
                return false;

            float axh = (float)Math.Sin(aAy);
            float azh = (float)Math.Cos(aAy);
            float bxh = (float)Math.Sin(bAy);
            float bzh = (float)Math.Cos(bAy);

            float align = Math.Abs(axh * bxh + azh * bzh);
            if (align < ParallelHeadingAlign)
                return false;

            float sx = (bx - ax) / dist;
            float sz = (bz - az) / dist;
            float along = Math.Abs(sx * axh + sz * azh);
            return along < ParallelMaxAlongTrack;
        }

        private static float TipLeaveAy(FeatureChain chain, bool isStart)
        {
            // Leave into the feature from this tip (matches GUI ParallelTrackTips).
            if (isStart)
                return chain.GeoStartAy;
            return chain.GeoEndAy + (float)Math.PI;
        }

        private static bool IsHairpinConnection(
            float aArriveX, float aArriveZ,
            float bLeaveX, float bLeaveZ)
            => aArriveX * bLeaveX + aArriveZ * bLeaveZ < -0.5f;

        private static void TipArrivalDir(FeatureChain chain, bool isStart, out float hx, out float hz)
        {
            // Opposite of leave-into-feature.
            float leaveAy = TipLeaveAy(chain, isStart);
            hx = -(float)Math.Sin(leaveAy);
            hz = -(float)Math.Cos(leaveAy);
        }

        private static bool ShouldSnapTips(FeatureChain a, bool aIsStart, FeatureChain b, bool bIsStart)
        {
            float ax = aIsStart ? a.GeoStartX : a.GeoEndX;
            float az = aIsStart ? a.GeoStartZ : a.GeoEndZ;
            float bx = bIsStart ? b.GeoStartX : b.GeoEndX;
            float bz = bIsStart ? b.GeoStartZ : b.GeoEndZ;
            float dist = Distance(ax, az, bx, bz);
            if (dist > EndpointSnapMeters)
                return false;

            float aLeaveAy = TipLeaveAy(a, aIsStart);
            float bLeaveAy = TipLeaveAy(b, bIsStart);
            if (IsLateralParallelGap(ax, az, aLeaveAy, bx, bz, bLeaveAy, dist))
                return false;

            TipArrivalDir(a, aIsStart, out float aArrX, out float aArrZ);
            float bLeaveX = (float)Math.Sin(bLeaveAy);
            float bLeaveZ = (float)Math.Cos(bLeaveAy);
            if (IsHairpinConnection(aArrX, aArrZ, bLeaveX, bLeaveZ))
                return false;

            TipArrivalDir(b, bIsStart, out float bArrX, out float bArrZ);
            float aLeaveX = (float)Math.Sin(aLeaveAy);
            float aLeaveZ = (float)Math.Cos(aLeaveAy);
            if (IsHairpinConnection(bArrX, bArrZ, aLeaveX, aLeaveZ))
                return false;

            return true;
        }

        private void BuildFromNetwork(string path)
        {
            var network = JsonConvert.DeserializeObject<NetworkLocalFile>(File.ReadAllText(path));
            if (network == null || network.Features == null)
                throw new InvalidDataException("Invalid network JSON: " + path);

            uint sectionIndex = 40001;
            foreach (var feature in network.Features)
            {
                if (!string.IsNullOrEmpty(feature.Error)
                    || feature.Primitives == null
                    || feature.Primitives.Count == 0
                    || feature.Start == null)
                {
                    continue;
                }

                _x = feature.Start.X;
                _z = feature.Start.Z;
                _ay = feature.Start.Ay;

                var chain = new FeatureChain
                {
                    ObjectId = feature.ObjectId,
                    // Source polyline ends — used for topology matching.
                    GeoStartX = feature.Start.X,
                    GeoStartZ = feature.Start.Z,
                    GeoEndX = feature.End != null ? feature.End.X : feature.Start.X,
                    GeoEndZ = feature.End != null ? feature.End.Z : feature.Start.Z,
                    GeoStartAy = feature.Start.Ay,
                    GeoEndAy = feature.Start.Ay,
                };
                SetGeoApproachHeadings(chain, feature);
                bool firstPrimitive = true;

                foreach (var primitive in feature.Primitives)
                {
                    // Continuous X/Z along the chain — snapping every section to
                    // its fitted Start created angled joint fillers (OBJECTID 2017).
                    //
                    // Heading: never override before a curve (Start.Ay is a
                    // polyline-chord estimate and fights the fitted tangent —
                    // that produced the wild DynTrack oscillations). For
                    // straights, adopt Start.Ay so corner chords / polyline
                    // fallbacks can change direction without a curve.
                    if (firstPrimitive && primitive.Start != null)
                    {
                        _x = primitive.Start.X;
                        _z = primitive.Start.Z;
                        _ay = primitive.Start.Ay;
                    }
                    else if (primitive.Type == "straight" && primitive.Start != null)
                    {
                        _ay = primitive.Start.Ay;
                        // Split long straights / chord halves can leave the running
                        // pose a few meters short of the next fitted start — stretch
                        // the previous straight to meet so DynTracks don't show a
                        // collinear hole.
                        if (!firstPrimitive && chain.Sections.Count > 0)
                        {
                            float gapToStart = Distance(
                                _x, _z, primitive.Start.X, primitive.Start.Z);
                            if (gapToStart > 0.2f && gapToStart < 40f)
                            {
                                TrackNode prev = chain.Sections[chain.Sections.Count - 1];
                                if (_primitives.TryGetValue(
                                        prev.Section.SectionIndex, out TrackPrimitive prevPrim)
                                    && !prevPrim.IsCurve)
                                {
                                    SectionWorldStart(prev.Section, out float psx, out float psz);
                                    ReseatSectionAsStraight(
                                        prev, prevPrim, psx, psz,
                                        primitive.Start.X, primitive.Start.Z);
                                    _x = primitive.Start.X;
                                    _z = primitive.Start.Z;
                                    _ay = primitive.Start.Ay;
                                }
                            }
                        }
                    }

                    sectionIndex = NextSectionIndex();
                    primitive.SectionIndex = sectionIndex;
                    _primitives[sectionIndex] = primitive;

                    if (firstPrimitive)
                    {
                        chain.StartX = _x;
                        chain.StartZ = _z;
                        chain.StartAy = _ay;
                        firstPrimitive = false;
                    }

                    if (primitive.Type == "straight")
                        AppendStraight(sectionIndex, chain);
                    else if (primitive.Type == "curve")
                        AppendCurve(sectionIndex, chain);
                }

                chain.EndX = _x;
                chain.EndZ = _z;
                chain.EndAy = _ay;
                // Do not reseat onto GeoEnd here — reconstruction drift makes
                // that a sharp kink at the tip (2017 into the T). Cluster/link
                // align pins endpoints after the chain is built.

                if (chain.Sections.Count > 0)
                    _chains.Add(chain);
            }

            if (_chains.Count == 0)
                throw new InvalidDataException("No fittable features found in " + path);

            Console.WriteLine(
                "Loaded network from " + path + ": "
                + _chains.Count + " features, "
                + _primitives.Count + " sections");
        }

        private static void SetGeoApproachHeadings(FeatureChain chain, NetworkFeature feature)
        {
            chain.GeoStartAy = feature.Start != null ? feature.Start.Ay : 0f;
            chain.GeoEndAy = chain.GeoStartAy;

            var pts = feature.PointsLocal;
            if (pts == null || pts.Count < 2)
                return;

            // Start travel heading: first segment.
            float x0 = pts[0][0], z0 = pts[0][1];
            float x1 = pts[1][0], z1 = pts[1][1];
            chain.GeoStartAy = (float)Math.Atan2(x1 - x0, z1 - z0);

            // End arrival heading: last segment.
            int n = pts.Count;
            float xa = pts[n - 2][0], za = pts[n - 2][1];
            float xb = pts[n - 1][0], zb = pts[n - 1][1];
            chain.GeoEndAy = (float)Math.Atan2(xb - xa, zb - za);
        }

        private void BuildFromLegacyPrimitives(string path)
        {
            var file = JsonConvert.DeserializeObject<PrimitiveFile>(File.ReadAllText(path));
            if (file == null || file.Segments == null || file.Segments.Count == 0)
                throw new InvalidDataException("Invalid primitives JSON: " + path);

            _x = 0;
            _z = 0;
            _ay = -2.7f;

            var chain = new FeatureChain
            {
                ObjectId = 0,
                GeoStartX = _x,
                GeoStartZ = _z,
                StartX = _x,
                StartZ = _z,
                StartAy = _ay,
            };
            uint sectionIndex = 40001;
            foreach (var primitive in file.Segments)
            {
                primitive.SectionIndex = sectionIndex;
                _primitives[sectionIndex] = primitive;

                if (primitive.Type == "straight")
                    AppendStraight(sectionIndex, chain);
                else if (primitive.Type == "curve")
                    AppendCurve(sectionIndex, chain);

                sectionIndex++;
            }

            chain.EndX = _x;
            chain.EndZ = _z;
            chain.EndAy = _ay;
            chain.GeoEndX = _x;
            chain.GeoEndZ = _z;
            _chains.Add(chain);

            Console.WriteLine(
                "Loaded legacy primitives from " + path + ": "
                + _primitives.Count + " sections");
        }

        /// <summary>
        /// Close a within-feature joint so the chain end lands on the next fitted
        /// pose. Reseat straights in place. Never chord-reseat curves (that erases
        /// fitted arcs and opens holes against neighbors); only bridge forward
        /// leftovers with a filler.
        /// </summary>
        private void CloseJointToPose(FeatureChain chain, float targetX, float targetZ)
        {
            float gap = Distance(chain.EndX, chain.EndZ, targetX, targetZ);
            if (gap < 0.001f || chain.Sections.Count == 0)
                return;

            TrackNode lastNode = chain.Sections[chain.Sections.Count - 1];
            if (!_primitives.TryGetValue(lastNode.Section.SectionIndex, out TrackPrimitive prim))
                return;

            float toTargetX = targetX - chain.EndX;
            float toTargetZ = targetZ - chain.EndZ;
            float endHx = (float)Math.Sin(chain.EndAy);
            float endHz = (float)Math.Cos(chain.EndAy);
            float forward = endHx * toTargetX + endHz * toTargetZ;

            if (prim.IsCurve)
            {
                // Keep the curve. Only bridge when the leftover is forward along
                // the exit heading; a reverse residual means the fitted start is
                // behind the arc end — snapping there would open a hole.
                if (forward >= 0f)
                    AppendFillerStraight(chain, targetX, targetZ);
                return;
            }

            SectionWorldStart(lastNode.Section, out float startX, out float startZ);
            ReseatSectionAsStraight(lastNode, prim, startX, startZ, targetX, targetZ);
            chain.EndX = targetX;
            chain.EndZ = targetZ;
            chain.EndAy = lastNode.Section.AY;
        }

        /// <summary>
        /// Snap the running pose onto a fitted start only when the joint actually
        /// closed; otherwise keep chain continuity (avoids sub-meter holes from
        /// jumping to a start we refused to bridge).
        /// </summary>
        private void AdoptPoseOrKeepContinuity(
            FeatureChain chain,
            float targetX,
            float targetZ,
            float targetAy)
        {
            if (Distance(chain.EndX, chain.EndZ, targetX, targetZ) < 0.01f)
            {
                _x = targetX;
                _z = targetZ;
                _ay = targetAy;
            }
            else
            {
                _x = chain.EndX;
                _z = chain.EndZ;
                _ay = chain.EndAy;
            }
        }

        private void AppendStraight(uint sectionIndex, FeatureChain chain)
        {
            var node = CreateSectionNode(sectionIndex);
            chain.Sections.Add(node);
            _nodes.Add(node);

            float length = _primitives[sectionIndex].Length;
            _x += length * (float)Math.Sin(_ay);
            _z += length * (float)Math.Cos(_ay);
        }

        private void AppendCurve(uint sectionIndex, FeatureChain chain)
        {
            TrackPrimitive primitive = _primitives[sectionIndex];
            var node = CreateSectionNode(sectionIndex);
            chain.Sections.Add(node);
            _nodes.Add(node);

            float dx =
                primitive.LocalEndX * (float)Math.Cos(_ay) +
                primitive.LocalEndZ * (float)Math.Sin(_ay);
            float dz =
               -primitive.LocalEndX * (float)Math.Sin(_ay) +
                primitive.LocalEndZ * (float)Math.Cos(_ay);

            _x += dx;
            _z += dz;
            _ay += primitive.SignedAngle;
        }

        private TrackNode CreateSectionNode(uint sectionIndex)
        {
            PlaceWorld(_x, _z, out int tileX, out int tileZ, out float localX, out float localZ);

            var node = new TrackNode
            {
                Id = _nextSectionOrdinal++,
                Section = new TrVectorSection
                {
                    SectionIndex = sectionIndex,
                    TileX = tileX,
                    TileZ = tileZ,
                    X = localX,
                    Z = localZ,
                    AY = _ay,
                },
            };
            return node;
        }

        private static void PlaceWorld(
            float worldX,
            float worldZ,
            out int tileX,
            out int tileZ,
            out float localX,
            out float localZ)
        {
            int relativeTileX = (int)Math.Floor((worldX + 1024.0) / 2048.0);
            int relativeTileZ = (int)Math.Floor((worldZ + 1024.0) / 2048.0);
            tileX = BaseTileX + relativeTileX;
            tileZ = BaseTileZ + relativeTileZ;
            localX = worldX - relativeTileX * 2048f;
            localZ = worldZ - relativeTileZ * 2048f;
        }

        private static string FindInputFile(string fileName)
        {
            var candidates = new[]
            {
                Path.Combine(Directory.GetCurrentDirectory(), fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", fileName),
                Path.Combine(@"C:\Users\jared\main\openrails\Tools\curve-fitter", fileName),
                Path.Combine(@"C:\Users\jared\main\openrails\Source\TdbDump", fileName),
            };

            return candidates.FirstOrDefault(File.Exists);
        }

        private struct EndpointKey : IEquatable<EndpointKey>
        {
            public EndpointKey(int objectId, bool isStart)
            {
                ObjectId = objectId;
                IsStart = isStart;
            }

            public int ObjectId { get; }
            public bool IsStart { get; }

            public bool Equals(EndpointKey other)
            {
                return ObjectId == other.ObjectId && IsStart == other.IsStart;
            }

            public override bool Equals(object obj)
            {
                return obj is EndpointKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return ObjectId * 2 + (IsStart ? 1 : 0);
            }
        }

        private class Endpoint
        {
            public int ObjectId;
            public int VectorId;
            public bool IsStart;
            public float X;
            public float Z;
        }

        private class EndpointLink
        {
            public int OtherObjectId;
            public int OtherVectorId;
            public bool OtherIsStart;
        }
    }
}
