using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using sbbChallange.ProblemDefinition;
using SbbChallenge.Helpers;
using sbbChallange.Layers;
using sbbChallange.GraphvizWrapper;

namespace sbbChallange.Visualization
{
    public static class GraphVisualization
    {
        public static void VisualizeRelevant(
            this IGraphLayer graphLayer)
        {
            Console.WriteLine($"Visualizing graph.");
            var solution = graphLayer.CostLayer.Solution;
            var problem = solution.Problem;

            TimeSpan[] delayAvoidingEntryTimes = new TimeSpan[graphLayer.Count];
            
            foreach (int v in graphLayer.TopologicalOrder().Reverse())
            {
                var op = solution.GetOperation(v);

                delayAvoidingEntryTimes[v] = graphLayer
                    .OutgoingArcs(v)
                    .Select(a => delayAvoidingEntryTimes[a.Head] - a.Length)
                    .Prepend(op.DelayWeight > 0? op.LatestEntry : TimeSpan.MaxValue)
                    .Prepend(TimeSpan.MaxValue)
                    .Min();
            }
            
            bool[] select = new bool[graphLayer.Count];
            for (int i = 0; i < graphLayer.Count; i++)
                
                if (solution.GetOperation(i, canBeNull: true) != null
                    && solution.GetEntryTime(i) > delayAvoidingEntryTimes[i])
                    
                    select[i] = true;
            
            Console.WriteLine($"Selecting {select.Count(x => x)} of {graphLayer.Count} nodes.");
            
            graphLayer.Visualize(
                op => select[op.Id], 
                null, 
                op => $"avoiding={delayAvoidingEntryTimes[op.Id].Show()}");
        }

        public static Func<Operation, bool> NeighbourSelector(IGraphLayer graph, Route route, int rank)
        {
            HashSet<int> current = route.Select(op => op.Id).ToHashSet();

            for (int i = 0; i < rank; i++)
            {
                HashSet<int> next = new HashSet<int>();
                foreach (int i1 in current)
                {
                    foreach (int i2 in  graph.OutgoingArcs(i1).Select(a => a.Head))
                    {
                        next.Add(i2);
                    }

                    foreach (int i2 in graph.IncomingArcs(i1).Select(a => a.Tail))
                    {
                        next.Add(i2);
                    }
                }
                current.UnionWith(next);
            }
            

            return op => current.Contains(op.Id);
        }
        
        public static void Visualize(
            this IGraphLayer graphLayer, 
            Func<Operation, bool> opSelector = null,
            Func<Arc, bool> arcSelector = null,
            Func<Operation, string> additionalOpLabel = null)
        {
            GraphvizWrapper.Graph g = new GraphvizWrapper.Graph();
            
            opSelector = opSelector ?? (x => true);
            arcSelector = arcSelector ?? (x => true);
            additionalOpLabel = additionalOpLabel ?? (x => "");

            HashSet<Arc> allCriticalArcs;
            if (graphLayer.TimesAreUpToDate())
            {
                allCriticalArcs = graphLayer.GetCriticalArcs(onlyDisjunctive: false).ToHashSet();
            }
            else
            {
                allCriticalArcs = new HashSet<Arc>();
            }

            Func<Operation, bool> opAndCriticalSelector = 
                x => opSelector(x) || allCriticalArcs.Any(a => a.Head == x.Id || a.Tail == x.Id);
            
            Dictionary<Operation, Node> nodes = new Dictionary<Operation, Node>();

            var solution = graphLayer.CostLayer.Solution;
            var problem = solution.Problem;

            var colors = ColorScale.ShuffledPalette(problem.JobCount);

            var connectedComponents = graphLayer.GetNonTrivialScc().SelectMany(x => x).ToHashSet();
            
            foreach (var job in problem.Jobs)
            {
                Route route = solution.GetRoute(job);

                foreach (var op in route.Operations.Where(opAndCriticalSelector))
                {
                    
                    StringBuilder label = new StringBuilder();

                    label.AppendLine($"id={op.Id}")
                        .AppendLine($"time={solution.GetEntryTime(op.Id).Show()}")
                        .AppendLine($"earliest={solution.GetOperation(op.Id).EarliestEarliestEntry.Show()}")
                        .AppendLine($"latest={solution.GetOperation(op.Id).LatestEntry.Show()}");

                    if (op.DelayWeight > 0)
                    {
                        label.AppendLine($"weight={op.DelayWeight}");
                        if (graphLayer.CostLayer.GetCostAtOperation(op.Id) > 0)
                            label.AppendLine($"cost={Math.Round(graphLayer.CostLayer.GetCostAtOperation(op.Id))}");
                    }

                    var additional = additionalOpLabel(op);

                    if (!string.IsNullOrWhiteSpace(additional)) label.AppendLine(additional);
                            
                            
                    if (graphLayer.OutgoingArcs(op.Id).Where(arcSelector).Any()
                        || graphLayer.IncomingArcs(op.Id).Where(arcSelector).Any())
                    {
                        var n = new Node()
                        {
                            Label = label.ToString(),
                            Shape = NodeShape.Box,
                            FillColor = colors[job.Id],
                            Style = NodeStyle.Filled,
                            FontColor = connectedComponents.Contains(op.Id)? Color.Red : Color.Black
                        };
                        nodes.Add(op, n);
                    }
                }
            }
            
            g.AddNodes(nodes.Values);
            Console.WriteLine($"Nodes added.");

            foreach (var arc in Enumerable.Range(0, graphLayer.Count)
                .SelectMany(graphLayer.OutgoingArcs)
                .Where(arcSelector))
            {
                if (nodes.TryGetValue(solution.GetOperation(arc.Tail), out var n0)
                    && nodes.TryGetValue(solution.GetOperation(arc.Head), out var n1))
                {
                    var label = (arc.MachineId != -1 ? $"M={arc.MachineId} " : "") + $"t={arc.Length.Show()}";

                    var edge = new Edge(n0, n1)
                    {
                        Label = label,
                        Style = arc.MachineId == -1 ? EdgeStyle.Solid : EdgeStyle.Dashed,
                        Color = allCriticalArcs.Contains(arc) ? Color.Red : Color.Black,
                    };

                    if (solution.GetEntryTime(arc.Tail) + arc.Length == solution.GetEntryTime(arc.Head))
                    {
                        edge.Style = EdgeStyle.Bold;
                    }
                    
                    g.AddEdges(edge);
                }
            }
            Console.WriteLine($"Edges added.");

            g.Display();
            Console.WriteLine("Done.");
        }
    }
}