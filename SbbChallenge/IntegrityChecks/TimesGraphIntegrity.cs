using System;
using System.Linq;
using System.Text;
using SbbChallenge.Helpers;
using sbbChallange.Layers;
using static sbbChallange.IntegrityChecks.Asserts;


namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch TimesGraphChecks = 
            new Switch(nameof(Mutable.IntegrityChecks.TimesGraphIntegrity) + "." + nameof(TimesGraphChecks), 
                "For all vertices: entry time set according to graph (incoming arcs) and problem (earliest entry).");
    }

}

namespace sbbChallange.Mutable.IntegrityChecks
{
    /// <summary>
    /// Contains a static method DoCheck() which Asserts that for all vertices: the entry time is set according to
    /// graph (incoming arcs) and problem (earliest entry).
    /// </summary>
    public static class TimesGraphIntegrity
    {
        /// <summary>  Asserts that for all vertices: the entry time is set according to graph (incoming arcs) and
        /// problem (earliest entry). </summary>
        [System.Diagnostics.Conditional("DEBUG")]
        public static void DoCheck(IGraphLayer graphLayer)
        {
            var costLayer = graphLayer.CostLayer;
            var solution = costLayer.Solution;
            
            if (!TimesGraphChecks.On) return;

            Assert(TimesGraphChecks, graphLayer.IsAcyclic());

            for (int i = 0; i < graphLayer.Count; i++)
            {
                var actualEntryTime = solution.GetEntryTime(i);

                TimeSpan shouldBe;

                if (solution.GetRoute(i, canBeNull: true) == null         // if no route set.
                    || solution.GetOperation(i, canBeNull: true) == null) // if the current route is short.
                    
                    shouldBe = TimeSpan.Zero;    

                else 
                    shouldBe = graphLayer.IncomingArcs(i)
                        .Select(ia => solution.GetEntryTime(ia.Tail) + ia.Length)
                        .Append(solution.GetOperation(i).EarliestEarliestEntry)
                        .Max();

                var deltaTicks = Math.Abs(shouldBe.Ticks - actualEntryTime.Ticks);

                if (deltaTicks > 0)
                {
                    Assert(TimesGraphChecks, false, CreateDebugMessageOnDetectedTimeInconsistency(graphLayer, i));
                }
            }
        }
        
        public static string CreateDebugMessageOnDetectedTimeInconsistency(IGraphLayer graphLayer, int vertex)
        {
            var solution = graphLayer.CostLayer.Solution;

            if (solution.GetRoute(vertex, canBeNull: true) == null)
            {
                return "Inconsistent time at a vertex along a critical path:\n" +
                       "\tIf no route is set, the entry time must be 0, but is " +
                       solution.GetEntryTime(vertex).Show() + "\n";
            }
            
            StringBuilder report = new StringBuilder()
                .AppendLine("Inconsistent time at a vertex along a critical path:")
                .Append($"\ttime @ {vertex} = ")
                .AppendLine(solution.GetEntryTime(vertex).Show())
                .Append($"\tearliest entry time @ {vertex} = ")
                .AppendLine(solution.GetOperation(vertex).EarliestEarliestEntry.Show())
                .Append($"\t(latest entry @ {vertex} = ")
                .Append(solution.GetOperation(vertex).LatestEntry.Show())
                .AppendLine(")");

            report.AppendLine(graphLayer.IncomingArcs(vertex).Any()
                ? $"\tthere are the following ({graphLayer.IncomingArcs(vertex).Count()}) incoming arcs: "
                : "\tthere are no incoming arcs.");

            foreach (Arc incomingArc in graphLayer.IncomingArcs(vertex))
            {
                report.Append("\t\t").Append(incomingArc).Append(": ")
                    .Append($"length = {incomingArc.Length.Show()}, ")
                    .Append($"time@{incomingArc.Tail} = {solution.GetEntryTime(incomingArc.Tail).Show()}")
                    .Append(", hence: entryTime >= ")
                    .AppendLine((solution.GetEntryTime(incomingArc.Tail) + incomingArc.Length).Show());
            }
            
            return report.ToString();
        }
    }
}