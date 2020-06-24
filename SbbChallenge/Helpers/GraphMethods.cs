using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using sbbChallange.Layers;

namespace SbbChallenge.Helpers
{
    public static class GraphMethods
    { 
        /// <summary>
        /// Generally we do not allow parallel arcs of the same machine id. Hence for comparison the length of the arc
        /// is not used. Here, we provide an additional comparer which does include the length. This comparer is
        /// intended to be used only for debug purposes. (ie, check that the length are correct.)
        /// </summary>
        public class WithLengthArcComparer : IEqualityComparer<Arc>
        {
            public static readonly WithLengthArcComparer Instance = new WithLengthArcComparer();

            public bool Equals(Arc x, Arc y)
            {
                return (x.Head == y.Head)
                       && (x.Tail == y.Tail)
                       && (x.MachineId == y.MachineId)
                       && x.Length.Equals(y.Length);
            }

            public int GetHashCode(Arc obj)
            {
                unchecked
                {
                    var hashCode = obj.Head;
                    hashCode = (hashCode * 397) ^ obj.Tail;
                    hashCode = (hashCode * 397) ^ obj.MachineId;
                    hashCode = (hashCode * 397) ^ obj.Length.GetHashCode();
                    return hashCode;
                }
            }
        }

        /// <summary>
        /// Debug method which creates a report of the differences of 2 graphs.
        /// </summary>
        public static string DifferencesToString(this IGraph thisGraph, IGraph otherGraph)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Graph differences:");
            if (thisGraph.Count != otherGraph.Count)
            {
                builder.AppendLine($"\tCount: {thisGraph.Count} != {otherGraph.Count}");
            }

            string ArcToString(Arc arc) =>
                $"{arc.Tail} --[{arc.MachineId}*{Math.Round(arc.Length.TotalSeconds, 2)}s]--> {arc.Head}";

            for (int i = 0; i < Math.Min(thisGraph.Count, otherGraph.Count); i++)
            {
                foreach (Arc arc in thisGraph.OutgoingArcs(i)
                    .Except(otherGraph.OutgoingArcs(i), WithLengthArcComparer.Instance))
                {
                    builder.AppendLine($"\t{ArcToString(arc)} only in '1'");
                }

                foreach (Arc arc in otherGraph.OutgoingArcs(i)
                    .Except(thisGraph.OutgoingArcs(i), WithLengthArcComparer.Instance))
                {
                    builder.AppendLine($"\t{ArcToString(arc)} only in '2'");
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Checks if two graphs are equal (this includes the length of arcs).
        /// </summary>
        public static bool IsEquivalentTo(this IGraph thisGraph, IGraph otherGraph)
        {
            // check same elements, seq equal!!
            bool SameElems(Arc[] fst, Arc[] snd)
                => !fst.Except(snd, WithLengthArcComparer.Instance).Any() &&
                   !snd.Except(fst, WithLengthArcComparer.Instance).Any();


            return thisGraph.Count == otherGraph.Count
                   && Enumerable
                       .Range(0, thisGraph.Count)
                       .All(i => SameElems(thisGraph.OutgoingArcs(i).ToArray(), otherGraph.OutgoingArcs(i).ToArray()));
        }

        public static bool IsAcyclic(this IGraph graph) => !GetNonTrivialScc(graph).Any();

        private struct SccData
        {
            public int Index;
            public int LowLink;
            public bool OnStack;
        }

        // ReSharper disable once MemberCanBePrivate.Global (used for debug, ie. in the IDE's runtime 'Evaluate')
        /// <summary>
        /// Return the connected components of a graph which contain more than one vertex.
        /// </summary>
        public static int[][] GetNonTrivialScc(this IGraph graph)
        {
            /*    Implementation of Tarjan's strongly connected components algorithm.
            *     Code structured according to pseudo code found at:
            *         https://en.wikipedia.org/wiki/Tarjan%27s_strongly_connected_components_algorithm
            */

            int index = 1; // we use '0' for 'undefined'
            SccData[] data = new SccData[graph.Count];
            Stack<int> stack = new Stack<int>();
            List<int[]> components = new List<int[]>();

            void StrongConnect(int v) // local function
            {
                data[v].Index = index;
                data[v].LowLink = index;
                index++;
                stack.Push(v);
                data[v].OnStack = true;

                foreach (int w in graph.OutgoingArcs(v).Select(a => a.Head))
                {
                    if (data[w].Index == 0)
                    {
                        StrongConnect(w);
                        data[v].LowLink = Math.Min(data[v].LowLink, data[w].LowLink);
                    }
                    else if (data[w].OnStack)
                        data[v].LowLink = Math.Min(data[v].LowLink, data[w].Index);
                }

                if (data[v].LowLink == data[v].Index)
                {
                    int w;
                    List<int> stronglyConnectedComponent = new List<int>();
                    do
                    {
                        w = stack.Pop();
                        data[w].OnStack = false;
                        stronglyConnectedComponent.Add(w);
                    } while (w != v);

                    components.Add(stronglyConnectedComponent.ToArray());
                }
            }

            for (int v = 0; v < graph.Count; v++)
                if (data[v].Index == 0)
                    StrongConnect(v);

            return components.Where(c => c.Length > 1).ToArray();
        }

        /// <summary>
        /// Returns the vertices of a graph in topological order.
        /// </summary>
        /// <param name="starts">A list of vertices, which are assumed to be on the same 'level' topologically, from
        /// where we start the enumeration. If null, then this is set to all vertices of 0 indegree.</param>
        /// <param name="breakingCondition">A vertex-predicate, if it is true for a vertex, the enumeration stops
        /// at this vertex. Null is treated as (_ => false). </param>
        /// <returns></returns>
        public static IEnumerable<int> TopologicalOrder(
            this IGraph graph,
            IEnumerable<int> starts = null,
            Func<int, bool> breakingCondition = null)
        {
            if (starts == null)
            {
                starts = Enumerable
                    .Range(0, graph.Count)
                    .Where(v => !graph.IncomingArcs(v).Any())
                    .Where(v => graph.OutgoingArcs(v).Any());
            }

            int[] remainingInDegree = new int[graph.Count];

            for (int i = 0; i < graph.Count; i++)
            {
                foreach (Arc arc in graph.OutgoingArcs(i))
                    remainingInDegree[arc.Head]++;
            }

            Stack<int> nodesWithZeroInDegree = new Stack<int>(starts.Where(s => remainingInDegree[s] == 0));

            while (nodesWithZeroInDegree.Count > 0)
            {
                int current = nodesWithZeroInDegree.Pop();

                if (breakingCondition != null && breakingCondition(current)) continue;

                yield return current;

                foreach (int successor in graph.OutgoingArcs(current).Select(a => a.Head))
                {
                    remainingInDegree[successor]--;
                    if (remainingInDegree[successor] == 0) nodesWithZeroInDegree.Push(successor);
                }
            }
        }

    }
}