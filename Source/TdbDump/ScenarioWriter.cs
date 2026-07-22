using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TdbDump
{
    /// <summary>
    /// Builds a playable path across the networked TDB and writes matching
    /// .pat / .srv / .act files (same PathID in all three).
    /// </summary>
    public static class ScenarioWriter
    {
        private const string PathId = "GeneratedTrack";
        private const string ServiceId = "GeneratedService";
        private const string ActivityId = "GeneratedActivity";
        private const string DefaultConsist = "Everett Switcher";
        private const string DefaultRouteId = "BNSF_Scenic";
        private const float MinWaypointSeparationM = 5f;

        public static void Write(
            string routeDirectory,
            IReadOnlyList<FeatureChain> chains,
            IReadOnlyList<object> allNodes)
        {
            if (chains == null)
                throw new ArgumentNullException(nameof(chains));
            if (allNodes == null)
                throw new ArgumentNullException(nameof(allNodes));

            if (!TryBuildPlayerRoute(chains, allNodes, out PlayerRoute route))
            {
                Console.WriteLine(
                    "Skipping scenario files: no path between two free TrEndNodes.");
                return;
            }

            string pathsDirectory = Path.Combine(routeDirectory, "PATHS");
            string servicesDirectory = Path.Combine(routeDirectory, "SERVICES");
            string activitiesDirectory = Path.Combine(routeDirectory, "ACTIVITIES");
            Directory.CreateDirectory(pathsDirectory);
            Directory.CreateDirectory(servicesDirectory);
            Directory.CreateDirectory(activitiesDirectory);

            string patPath = Path.Combine(pathsDirectory, PathId + ".pat");
            PATWriter.Write(
                patPath,
                route.Waypoints,
                PathId,
                "Generated Track",
                "Start",
                "End");
            Console.WriteLine(
                "Wrote path to: " + patPath
                + " (" + route.Waypoints.Count + " PDPs, "
                + route.VectorCount + " vectors)");

            string srvPath = Path.Combine(servicesDirectory, ServiceId + ".srv");
            SRVWriter.Write(
                srvPath,
                "Generated Track",
                DefaultConsist,
                PathId);
            Console.WriteLine("Wrote service to: " + srvPath);

            string actPath = Path.Combine(activitiesDirectory, ActivityId + ".act");
            ACTWriter.Write(
                actPath,
                route.Start,
                route.End,
                DefaultRouteId,
                "Generated Track",
                ServiceId,
                PathId);
            Console.WriteLine("Wrote activity to: " + actPath);
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
            out PlayerRoute route)
        {
            route = null;
            var byId = IndexNodes(allNodes);
            var ends = allNodes.OfType<TrEndNode>().ToList();
            if (ends.Count < 2)
                return false;

            var vectorsById = chains.ToDictionary(c => c.VectorNodeId);
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

        /// <summary>
        /// BFS for fewest node hops between two ends (through line, not spur out-and-back).
        /// Among end pairs we keep the path with the most waypoints.
        /// </summary>
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
            // Do not put the free-end TrEndNode first: it coincides with the first
            // section start, and OR's Traveller then cannot pick a path direction
            // (zero distance to the next PDP), so the train faces off the tip.
            var waypoints = new List<PathWaypoint>();
            int vectorCount = 0;

            for (int i = 0; i < nodeIds.Count; i++)
            {
                if (!byId.TryGetValue(nodeIds[i], out object node))
                    continue;

                if (node is TrJunctionNode junction)
                {
                    AppendUnique(waypoints, FromJunction(junction));
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
            // WireVectorSide adds start pin then end pin → Pins[0]=start, Pins[1]=end.
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
