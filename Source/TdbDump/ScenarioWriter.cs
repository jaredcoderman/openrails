using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TdbDump
{
    public sealed class ScenarioPathOptions
    {
        public int? StartObjectId;
        public bool StartIsStart = true;
        public int? GoalObjectId;
        public bool GoalIsStart = true;
        public string PathId = "GeneratedTrack";
        public string PathName = "Generated Track";
        public string StartLabel = "Start";
        public string EndLabel = "End";
        public string Consist = "Everett Switcher";
        public string RouteId = "BNSF_Scenic";
    }

    /// <summary>
    /// Builds a playable path across the networked TDB and writes matching
    /// .pat / .srv / .act files (same PathID in all three).
    /// </summary>
    public static class ScenarioWriter
    {
        private const string DefaultConsist = "Everett Switcher";
        private const string DefaultRouteId = "BNSF_Scenic";
        private const float MinWaypointSeparationM = 5f;

        public static void Write(
            string routeDirectory,
            IReadOnlyList<FeatureChain> chains,
            IReadOnlyList<object> allNodes)
        {
            Write(routeDirectory, chains, allNodes, null);
        }

        public static void Write(
            string routeDirectory,
            IReadOnlyList<FeatureChain> chains,
            IReadOnlyList<object> allNodes,
            ScenarioPathOptions options)
        {
            if (chains == null)
                throw new ArgumentNullException(nameof(chains));
            if (allNodes == null)
                throw new ArgumentNullException(nameof(allNodes));

            if (options == null)
                options = new ScenarioPathOptions();

            if (!TryBuildPlayerRoute(chains, allNodes, options, out PlayerRoute route))
            {
                Console.WriteLine(
                    "Skipping scenario files: no path between the requested ends.");
                return;
            }

            string pathId = SanitizeId(options.PathId);
            string serviceId = pathId;
            string activityId = pathId;

            string pathsDirectory = Path.Combine(routeDirectory, "PATHS");
            string servicesDirectory = Path.Combine(routeDirectory, "SERVICES");
            string activitiesDirectory = Path.Combine(routeDirectory, "ACTIVITIES");
            Directory.CreateDirectory(pathsDirectory);
            Directory.CreateDirectory(servicesDirectory);
            Directory.CreateDirectory(activitiesDirectory);

            string patPath = Path.Combine(pathsDirectory, pathId + ".pat");
            PATWriter.Write(
                patPath,
                route.Waypoints,
                pathId,
                options.PathName,
                options.StartLabel,
                options.EndLabel);
            Console.WriteLine(
                "Wrote path to: " + patPath
                + " (" + route.Waypoints.Count + " PDPs, "
                + route.VectorCount + " vectors)");

            string srvPath = Path.Combine(servicesDirectory, serviceId + ".srv");
            SRVWriter.Write(
                srvPath,
                options.PathName,
                string.IsNullOrWhiteSpace(options.Consist) ? DefaultConsist : options.Consist,
                pathId);
            Console.WriteLine("Wrote service to: " + srvPath);

            string actPath = Path.Combine(activitiesDirectory, activityId + ".act");
            ACTWriter.Write(
                actPath,
                route.Start,
                route.End,
                string.IsNullOrWhiteSpace(options.RouteId) ? DefaultRouteId : options.RouteId,
                options.PathName,
                serviceId,
                pathId);
            Console.WriteLine("Wrote activity to: " + actPath);
        }

        public static string SanitizeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "GeneratedTrack";
            var sb = new StringBuilder(value.Length);
            foreach (char c in value.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else if (char.IsWhiteSpace(c) || c == '/' || c == '\\')
                    sb.Append('_');
            }
            string id = sb.ToString();
            return id.Length == 0 ? "GeneratedTrack" : id;
        }

        private sealed class PlayerRoute
        {
            public TrEndNode Start;
            public TrEndNode End;
            public List<PathWaypoint> Waypoints;
            public int VectorCount;
        }

        private static bool TryBuildPlayerRoute(
            IReadOnlyList<FeatureChain> chains,
            IReadOnlyList<object> allNodes,
            ScenarioPathOptions options,
            out PlayerRoute route)
        {
            route = null;
            var byId = IndexNodes(allNodes);
            var ends = allNodes.OfType<TrEndNode>().ToList();
            if (ends.Count < 2)
                return false;

            var vectorsById = chains.ToDictionary(c => c.VectorNodeId);
            var chainsByObjectId = chains.ToDictionary(c => c.ObjectId);

            if (options.StartObjectId.HasValue && options.GoalObjectId.HasValue)
            {
                if (!TryResolveEnd(
                        options.StartObjectId.Value, options.StartIsStart,
                        chainsByObjectId, byId, out TrEndNode start))
                {
                    Console.WriteLine(
                        "Could not resolve start end oid "
                        + options.StartObjectId.Value
                        + (options.StartIsStart ? "S" : "E"));
                    return false;
                }
                if (!TryResolveEnd(
                        options.GoalObjectId.Value, options.GoalIsStart,
                        chainsByObjectId, byId, out TrEndNode goal))
                {
                    Console.WriteLine(
                        "Could not resolve goal end oid "
                        + options.GoalObjectId.Value
                        + (options.GoalIsStart ? "S" : "E"));
                    return false;
                }
                if (!TryShortestHopPath(start, goal, byId, out List<int> nodeIds))
                {
                    Console.WriteLine("No connected path between selected ends.");
                    return false;
                }
                route = Materialize(start, goal, nodeIds, byId, vectorsById);
                return route != null && route.Waypoints.Count >= 2;
            }

            PlayerRoute best = null;
            int bestScore = -1;

            foreach (var start in ends)
            {
                foreach (var goal in ends)
                {
                    if (goal.Id == start.Id)
                        continue;
                    if (!TryShortestHopPath(start, goal, byId, out List<int> nodeIds))
                        continue;

                    PlayerRoute candidate = Materialize(start, goal, nodeIds, byId, vectorsById);
                    if (candidate == null)
                        continue;
                    if (candidate.Waypoints.Count > bestScore)
                    {
                        bestScore = candidate.Waypoints.Count;
                        best = candidate;
                    }
                }
            }

            route = best;
            return route != null && route.Waypoints.Count >= 2;
        }

        private static bool TryResolveEnd(
            int objectId,
            bool isStart,
            Dictionary<int, FeatureChain> chainsByObjectId,
            Dictionary<int, object> byId,
            out TrEndNode end)
        {
            end = null;
            if (!chainsByObjectId.TryGetValue(objectId, out FeatureChain chain))
                return false;
            if (!byId.TryGetValue(chain.VectorNodeId, out object vectorObj)
                || !(vectorObj is TrackNode vector)
                || vector.Pins.Count < 2)
                return false;

            int neighborId = isStart ? vector.Pins[0].Node : vector.Pins[1].Node;
            if (!byId.TryGetValue(neighborId, out object neighbor) || !(neighbor is TrEndNode tip))
                return false;

            end = tip;
            return true;
        }

        private static Dictionary<int, object> IndexNodes(IReadOnlyList<object> allNodes)
        {
            var byId = new Dictionary<int, object>();
            foreach (object node in allNodes)
            {
                if (node is TrackNode vector)
                    byId[vector.Id] = vector;
                else if (node is TrEndNode end)
                    byId[end.Id] = end;
                else if (node is TrJunctionNode junction)
                    byId[junction.Id] = junction;
            }
            return byId;
        }

        private static bool TryShortestHopPath(
            TrEndNode start,
            TrEndNode goal,
            Dictionary<int, object> byId,
            out List<int> nodeIds)
        {
            nodeIds = null;
            var cameFrom = new Dictionary<int, int>();
            var queue = new Queue<int>();
            queue.Enqueue(start.Id);
            cameFrom[start.Id] = -1;

            bool found = false;
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (current == goal.Id && current != start.Id)
                {
                    found = true;
                    break;
                }

                if (!byId.TryGetValue(current, out object node))
                    continue;

                int parent = cameFrom[current];
                foreach (int nextId in NeighborIds(node, parent))
                {
                    if (cameFrom.ContainsKey(nextId))
                        continue;
                    cameFrom[nextId] = current;
                    queue.Enqueue(nextId);
                }
            }

            if (!found)
                return false;

            nodeIds = new List<int>();
            for (int id = goal.Id; id >= 0; id = cameFrom[id])
            {
                nodeIds.Add(id);
                if (id == start.Id)
                    break;
            }
            nodeIds.Reverse();
            return nodeIds.Count >= 2;
        }

        private static IEnumerable<int> NeighborIds(object node, int parentId)
        {
            IEnumerable<TrPin> pins;
            if (node is TrEndNode end)
                pins = end.Pins;
            else if (node is TrackNode vector)
                pins = vector.Pins;
            else if (node is TrJunctionNode junction)
                pins = junction.Pins;
            else
                yield break;

            foreach (var pin in pins)
            {
                if (pin.Node == parentId)
                    continue;
                yield return pin.Node;
            }
        }

        private static PlayerRoute Materialize(
            TrEndNode start,
            TrEndNode goal,
            List<int> nodeIds,
            Dictionary<int, object> byId,
            Dictionary<int, FeatureChain> vectorsById)
        {
            var waypoints = new List<PathWaypoint>();
            int vectorCount = 0;

            for (int i = 0; i < nodeIds.Count; i++)
            {
                if (!byId.TryGetValue(nodeIds[i], out object node))
                    continue;

                if (node is TrJunctionNode junction)
                {
                    // Never drop junction PDPs: section tips often sit on the
                    // same coordinates, and Near-dedupe would leave flag 1 1.
                    // Without a TrackPDP ( … 2 0 ), OR keeps the default main
                    // at facing points and ignores the spur.
                    AppendJunction(waypoints, FromJunction(junction));
                    continue;
                }

                if (!(node is TrackNode vector))
                    continue;
                if (!vectorsById.TryGetValue(vector.Id, out FeatureChain chain))
                    continue;

                int prevId = i > 0 ? nodeIds[i - 1] : -1;
                int nextId = i + 1 < nodeIds.Count ? nodeIds[i + 1] : -1;
                bool forward = TravelForward(vector, prevId, nextId);
                AppendChainSections(chain, forward, waypoints);
                vectorCount++;
            }

            AppendUnique(waypoints, FromEnd(goal, junctionFlag: 1, invalidFlag: 0));

            if (waypoints.Count < 2)
                return null;

            return new PlayerRoute
            {
                Start = start,
                End = goal,
                Waypoints = waypoints,
                VectorCount = vectorCount,
            };
        }

        private static void AppendUnique(List<PathWaypoint> waypoints, PathWaypoint next)
        {
            if (waypoints.Count > 0 && Near(waypoints[waypoints.Count - 1], next))
                return;
            waypoints.Add(next);
        }

        private static void AppendJunction(List<PathWaypoint> waypoints, PathWaypoint junction)
        {
            if (waypoints.Count > 0 && Near(waypoints[waypoints.Count - 1], junction))
            {
                PathWaypoint prev = waypoints[waypoints.Count - 1];
                prev.TileX = junction.TileX;
                prev.TileZ = junction.TileZ;
                prev.X = junction.X;
                prev.Y = junction.Y;
                prev.Z = junction.Z;
                prev.JunctionFlag = 2;
                prev.InvalidFlag = 0;
                prev.PathFlags = 0;
                return;
            }
            waypoints.Add(junction);
        }

        private static bool Near(PathWaypoint a, PathWaypoint b)
        {
            if (a.TileX != b.TileX || a.TileZ != b.TileZ)
            {
                float dx = (a.X - b.X) + (a.TileX - b.TileX) * 2048f;
                float dz = (a.Z - b.Z) + (a.TileZ - b.TileZ) * 2048f;
                return dx * dx + dz * dz < MinWaypointSeparationM * MinWaypointSeparationM;
            }
            float lx = a.X - b.X;
            float lz = a.Z - b.Z;
            return lx * lx + lz * lz < MinWaypointSeparationM * MinWaypointSeparationM;
        }

        private static bool TravelForward(TrackNode vector, int prevId, int nextId)
        {
            if (vector.Pins.Count == 0)
                return true;
            int startNeighbor = vector.Pins[0].Node;
            int endNeighbor = vector.Pins.Count > 1 ? vector.Pins[1].Node : -1;

            if (prevId == startNeighbor || nextId == endNeighbor)
                return true;
            if (prevId == endNeighbor || nextId == startNeighbor)
                return false;
            return true;
        }

        private static void AppendChainSections(
            FeatureChain chain,
            bool forward,
            List<PathWaypoint> waypoints)
        {
            var sections = chain.Sections;
            if (forward)
            {
                for (int i = 0; i < sections.Count; i++)
                {
                    if (sections[i].Section != null)
                        AppendUnique(waypoints, FromSection(sections[i].Section));
                }
            }
            else
            {
                for (int i = sections.Count - 1; i >= 0; i--)
                {
                    if (sections[i].Section != null)
                        AppendUnique(waypoints, FromSection(sections[i].Section));
                }
            }
        }

        private static PathWaypoint FromSection(TrVectorSection section)
        {
            return new PathWaypoint
            {
                TileX = section.TileX,
                TileZ = section.TileZ,
                X = section.X,
                Y = section.Y,
                Z = section.Z,
                JunctionFlag = 1,
                InvalidFlag = 1,
                PathFlags = 0,
            };
        }

        private static PathWaypoint FromEnd(TrEndNode node, int junctionFlag, int invalidFlag)
        {
            return new PathWaypoint
            {
                TileX = node.TileX,
                TileZ = node.TileZ,
                X = node.X,
                Y = node.Y,
                Z = node.Z,
                JunctionFlag = junctionFlag,
                InvalidFlag = invalidFlag,
                PathFlags = 0,
            };
        }

        private static PathWaypoint FromJunction(TrJunctionNode node)
        {
            return new PathWaypoint
            {
                TileX = node.TileX,
                TileZ = node.TileZ,
                X = node.X,
                Y = node.Y,
                Z = node.Z,
                JunctionFlag = 2,
                InvalidFlag = 0,
                PathFlags = 0,
            };
        }
    }
}
