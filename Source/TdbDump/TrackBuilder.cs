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

            foreach (var chain in activeChains)
            {
                foreach (var sectionNode in chain.Sections)
                {
                    var section = sectionNode.Section;
                    section.WFNameX = section.TileX.ToString();
                    section.WFNameZ = section.TileZ.ToString();
                    section.WorldFileUiD = worldUiD++;
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

                if (links.TryGetValue(startKey, out EndpointLink startLink))
                {
                    // Connect to the other vector's matching side.
                    vector.Pins.Add(new TrPin(startLink.OtherVectorId, startLink.OtherIsStart ? 0 : 1));
                }
                else
                {
                    int endNodeId = nextId++;
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

                if (links.TryGetValue(endKey, out EndpointLink endLink))
                {
                    vector.Pins.Add(new TrPin(endLink.OtherVectorId, endLink.OtherIsStart ? 0 : 1));
                }
                else
                {
                    int endNodeId = nextId++;
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

                allNodes.Add(vector);
            }

            // Stable TDB order by node id.
            return allNodes.OrderBy(NodeId).ToList();
        }

        private static int NodeId(object node)
        {
            if (node is TrackNode vector)
                return vector.Id;
            if (node is TrEndNode end)
                return end.Id;
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
            for (int i = 0; i < endpoints.Count; i++)
            {
                for (int j = i + 1; j < endpoints.Count; j++)
                {
                    Endpoint a = endpoints[i];
                    Endpoint b = endpoints[j];
                    if (a.ObjectId == b.ObjectId)
                        continue;

                    float dist = Distance(a.X, a.Z, b.X, b.Z);
                    if (dist <= EndpointSnapMeters)
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
                    if (Distance(endpoints[i].Gx, endpoints[i].Gz, endpoints[j].Gx, endpoints[j].Gz)
                        <= EndpointSnapMeters)
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

                // Prefer adjusting an end over a start.
                if (!key.IsStart)
                {
                    if (AdjustChainEndToTarget(chain, bx, bz))
                        reseats++;
                }
                else if (!link.OtherIsStart)
                {
                    if (AdjustChainEndToTarget(other, ax, az))
                        reseats++;
                }
                else if (AdjustChainStartToTarget(chain, bx, bz))
                {
                    reseats++;
                }
            }

            return reseats;
        }

        /// <summary>
        /// Only close leftover geo-matched gaps that are already small. Large
        /// unmatched residuals are real corridor gaps or junctions (Step 4).
        /// </summary>
        private const float MaxOrphanFillerMeters = 50f;

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
                worldX += dx;
                worldZ += dz;
                PlaceWorld(worldX, worldZ, out int tileX, out int tileZ, out float localX, out float localZ);
                section.TileX = tileX;
                section.TileZ = tileZ;
                section.X = localX;
                section.Z = localZ;
                section.WFNameX = tileX.ToString();
                section.WFNameZ = tileZ.ToString();
            }
        }

        private static float Distance(float x0, float z0, float x1, float z1)
        {
            float dx = x0 - x1;
            float dz = z0 - z1;
            return (float)Math.Sqrt(dx * dx + dz * dz);
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
                };
                bool firstPrimitive = true;

                foreach (var primitive in feature.Primitives)
                {
                    if (primitive.Start != null)
                    {
                        if (firstPrimitive)
                        {
                            _x = primitive.Start.X;
                            _z = primitive.Start.Z;
                            _ay = primitive.Start.Ay;
                        }
                        else
                        {
                            // Fitted starts are authoritative. Chaining LocalEnd alone
                            // drifts tens of meters (e.g. OBJECTID 2017 ~64m), which
                            // shows up as gaps / parallel miss on T approaches.
                            chain.EndX = _x;
                            chain.EndZ = _z;
                            chain.EndAy = _ay;
                            CloseJointToPose(chain, primitive.Start.X, primitive.Start.Z);
                            AdoptPoseOrKeepContinuity(
                                chain,
                                primitive.Start.X,
                                primitive.Start.Z,
                                primitive.Start.Ay);
                        }
                    }

                    // Allocate after any joint fillers so indices stay unique.
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
                // Land on the true GeoJSON end when possible without destroying
                // a trailing curve (chord reseat opened the remaining holes).
                CloseJointToPose(chain, chain.GeoEndX, chain.GeoEndZ);
                if (Distance(chain.EndX, chain.EndZ, chain.GeoEndX, chain.GeoEndZ) < 0.01f)
                {
                    // already on geo end
                }
                // chain.End* already updated by CloseJointToPose when it bridged

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
