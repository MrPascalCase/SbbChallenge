using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;

using SbbChallenge.Helpers;
using sbbChallange.Layers;
using sbbChallange.Mutable.IntegrityChecks;
using sbbChallange.Mutable.Search;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch SingleThreadSearchChecks = 
            new Switch(nameof(Search.SingleThreadSearch) + "." + nameof(SingleThreadSearchChecks));
    }
}

namespace sbbChallange.Search
{
    public static class SingleThreadSearch
    {
        public class SearchDoneReport
        {
            public int IterationsCompleted;
            public List<double> CumulativeTimes;
            public List<double> CostAtIteration;
            public List<double> BestCostAtIteration;
            public string ProblemName;
            public string RunName;

            public IJobShopLayer Result;

            private Stopwatch stopwatch;
            
            public SearchDoneReport (string problemName, string runName)
            {
                RunName = runName;
                ProblemName = problemName;
                
                IterationsCompleted = 0;
                CumulativeTimes = new List<double>();
                CostAtIteration = new List<double>();
                BestCostAtIteration = new List<double>();
                
                stopwatch = Stopwatch.StartNew();
            }

            public void Add(double cost, double bestCost)
            {
                CumulativeTimes.Add(stopwatch.ElapsedMilliseconds / 1000.0);
                CostAtIteration.Add(cost);
                BestCostAtIteration.Add(bestCost);

                IterationsCompleted++;
            }

            public string ToCsv(bool header = true)
            {
                StringBuilder builder = new StringBuilder();
                if (header) builder.AppendLine($"problem_name, run_name, iteration, total_time_sec, cost, best_cost");

                for (int i = 0; i < IterationsCompleted; i++)
                {
                    builder.AppendLine($"{ProblemName}, {RunName}, {i}, {CumulativeTimes[i]}, {CostAtIteration[i]}, {BestCostAtIteration[i]}");
                }
                
                return builder.ToString();
            }
        }
        
        private enum TabooType
        {
            Unknown = 0,
            AprioriTaboo,
            AposterioriTaboo,
            NotTaboo,
            Ascension,
        }

        private static string ShortToString(TabooType type)
        {
            switch (type)
            {
                case TabooType.Unknown: return "??";
                case TabooType.AposterioriTaboo: return "t<";
                case TabooType.AprioriTaboo: return ">t";
                case TabooType.NotTaboo: return "ok";
                case TabooType.Ascension: return "!!";
                default: throw new ArgumentException();
            }
        }

        private class SearchMove
        {
            public readonly Move ActualMove;
            public TabooType TabooType;
            public double Cost;

            public SearchMove(Move actualMove)
            {
                ActualMove = actualMove;
                TabooType = TabooType.Unknown;
                Cost = double.MaxValue;
            }
        }
        
        private static SearchMove[] GetMoves(IJobShopLayer origin, ITabooList tabooList, Random random)
        {
            if (origin.GetConnectionsCost() > .5 * origin.GetTotalCost())
            {
                return origin.GetMoves(
                        connectionFix: true,
                        connectionImprovement: true,
                        criticalArcsBased: tabooList.Any(),
                        routeSwapBased: false,
                        routeSwapWithHeuristicInsertion: false, //true, //!tabooList.Any(),
                        jobReinsertion: false,
                        routePenaltyImprovement: false)
                    .Shuffle(random)
                    .Select(m => new SearchMove(m))
                    .ToArray();
            }
            
            if (origin.GetDelayCost() > origin.GetRoutingCost())
            {
                bool changeRoute = !tabooList.Any() || random.NextDouble() < .3;
                
                return origin
                    .GetMoves(
                        connectionFix: true,
                        connectionImprovement: true,
                        criticalArcsBased: true,
                        routeSwapBased: changeRoute,
                        routeSwapWithHeuristicInsertion: changeRoute, //true, //!tabooList.Any(),
                        jobReinsertion: changeRoute,
                        routePenaltyImprovement: false)
                    .Shuffle(random)
                    .Select(m => new SearchMove(m))
                    .ToArray();
            }
            else
            {
                return origin
                    .GetMoves(false, false, false, false, false, false, routePenaltyImprovement: true)
                    .Shuffle(random).Select(m => new SearchMove(m)).ToArray();
            }
        }
        
        private class NoMoves : Exception { }
        
        private static IJobShopLayer Step(
            int iteration,
            IJobShopLayer origin,
            double bestCost,
            ITabooList tabooList,
            Random random,
            double moveImmediatelyImprovementRate = 0.00001,  // improvement >= 0.001% -> move to the next solution immediately
            int minArcsToCheckCount = 1,
            bool verbose = true)
        {
            TimesGraphIntegrity.DoCheck(origin.RoutingLayer.ClosureLayer.SequencingLayer.GraphLayer);

            var moves = GetMoves(origin, tabooList, random);
            
            if (moves.Length == 0) throw new NoMoves();

            if (verbose) Console.WriteLine($"Inspecting {moves.Length} moves.");
            
            //TabooType[] tabooTypes = new TabooType[moves.Length]; // init all to 'unknown'

            if (tabooList.Any())
                for (int i = 0; i < moves.Length; i++)
                    if (tabooList.IsAprioriTaboo(moves[i].ActualMove))
                    {
                        moves[i].TabooType = TabooType.AprioriTaboo;
                    }

            double originCost = origin.GetTotalCost();
            //double[] resultCosts = Enumerable.Repeat(double.MaxValue, moves.Length).ToArray();
            
            // Try the moves.
            for (int i = 0; i < moves.Length; i++)
            {
                if (moves[i].TabooType == TabooType.AprioriTaboo)
                {
                    if (verbose) Console.Write(ShortToString(moves[i].TabooType) + " ");
                    continue;
                }
                
                // Clone & execute.
                var clone = origin.Clone();
                clone.ExecuteMove(moves[i].ActualMove);
                moves[i].Cost = clone.GetTotalCost();
                
                // Never trust yourself
                Assert(SingleThreadSearchChecks, clone.GetHashCode() != origin.GetHashCode() || !clone.Equals(origin));

                // decide if the move is taboo
                if (!tabooList.Any()) moves[i].TabooType = TabooType.NotTaboo;
                
                else if (moves[i].Cost < 0.9999 * bestCost) moves[i].TabooType = TabooType.Ascension;
                
                else if (tabooList.IsAposterioriTaboo(clone)) moves[i].TabooType = TabooType.AposterioriTaboo;
                
                else moves[i].TabooType = TabooType.NotTaboo;
                
                if (verbose) Console.Write(
                    ShortToString(moves[i].TabooType) 
                    + (moves[i].Cost < 0.9999 * originCost? "^" : "") 
                    + " ");
                
                // break, if the improvement is large enough (&& not taboo).
                double improvementRate = (bestCost - moves[i].Cost) / bestCost;
                
                if (i > minArcsToCheckCount 
                    && improvementRate > moveImmediatelyImprovementRate
                    && (moves[i].TabooType == TabooType.Ascension || moves[i].TabooType == TabooType.NotTaboo))
                {
                   break;
                }
            }

            if ((iteration % 10) == 0)
            {
                //Console.WriteLine("hello...");
            }
            
            // Find the move to return.
            int candidateIndex = -1;
            Move candidate = null;
            for (int i = 0; i < moves.Length; i++)
            {
                var move = moves[i];
                var taboo = moves[i].TabooType;

                if ((candidateIndex == -1 || moves[i].Cost < moves[candidateIndex].Cost)
                    && taboo != TabooType.AposterioriTaboo
                    && taboo != TabooType.AprioriTaboo)
                {
                    candidateIndex = i;
                    candidate = move.ActualMove;
                }
            }

            Assert(SingleThreadSearchChecks, candidate != null);

            if (candidate == null)
            {
                Console.WriteLine("all moves taboo!");
                double minCost = moves.Select(m => m.Cost).Min();
                candidateIndex = Enumerable.Range(0, moves.Length).First(i => (moves[i].Cost == minCost));
                candidate = moves[candidateIndex].ActualMove;
            }
            
            if (moves[candidateIndex].Cost > 0.9999 * bestCost)
            {
                tabooList.ProhibitUndoOfMove(origin, candidate);

                Assert(SingleThreadSearchChecks, tabooList.IsAprioriTaboo(candidate)
                               || tabooList.IsAposterioriTaboo(origin),
                    "When adding elements to the taboo-list, the original solution (before executing the move) must be taboo.");

                if (SingleThreadSearchChecks.On
                    && (candidate.Type == MoveType.RemoveCriticalArcLeftClosure
                        || candidate.Type == MoveType.RemoveCriticalArcRightClosure))
                {
                    Assert(SingleThreadSearchChecks, origin.TransitiveArcExist(candidate.CriticalArc));
                    var target = origin.Clone();
                    target.ExecuteMove(candidate);
                    Assert(SingleThreadSearchChecks, !target.TransitiveArcExist(candidate.CriticalArc));
                }
            }
            
            // Execute this move & return.
            origin.ExecuteMove(candidate);
            return origin;
        }

        public static void TryFixConnections(IJobShopLayer solution)
        {
            var moves = solution.GetMoves(true, false, false, false, false, false, false);

            for (int i = 0; i < moves.Length; i++)
            {
                Console.Write($"Fixing missed connection {i}...");
                solution.ExecuteMove(moves[i]);
                Console.WriteLine($"\t(Cost={solution.GetTotalCost()})");
            }
        }

        private static void DisplayToConsole(params (string, Func<string>)[] elements)
        {
            void Line()
            {
                Console.WriteLine(new string('_', Console.WindowWidth));
                Console.WriteLine();
            }
            
            Console.Clear();
            Line();

            var sizeLeft = elements.Max(t => t.Item1.Length);

            foreach (var (left, right) in elements)
            {
                Console.Write(left.PadRight(sizeLeft));
                Console.Write(string.IsNullOrWhiteSpace(left) ? "   " : " = ");
                Console.WriteLine(right());
            }
            
            Line();
        }
        

        public static SearchDoneReport Run(
            string runName,
            IJobShopLayer initialSolution,
            Random random,
            double terminationCost = Double.Epsilon,
            int maxIter = Int32.MaxValue,
            TimeSpan maxRunTime = default)
        {
            if (maxRunTime == default) maxRunTime = TimeSpan.MaxValue;
            
            ITabooList tabooList = new TabooList();
            
            //TryFixConnections(initialSolution);
            
            IJobShopLayer bestSolution = initialSolution.Clone();
            IJobShopLayer currentSolution = initialSolution;
            var sw = Stopwatch.StartNew();
            
            
            string problemName = currentSolution
                .RoutingLayer.ClosureLayer.SequencingLayer.GraphLayer.CostLayer.Solution.Problem.ProblemName;
            
            var report = new SearchDoneReport(problemName, runName);
            
            var newLine = new ValueTuple<string, Func<string>>("", () => "");

            for (int iter = 0; iter < maxIter; iter++)
            {
                // Display
                DisplayToConsole(
                    ("Problem", problemName.ToString),
                    newLine,
                    ("Iteration", iter.ToString),
                    ("Time passed", () => sw.Elapsed.Show()),
                    ("Current Total Cost", Math.Round(currentSolution.GetTotalCost(), 2).ToString),
                    ("Current Routing Cost", Math.Round(currentSolution.GetRoutingCost(), 2).ToString),
                    ("Current Delay Cost", Math.Round(currentSolution.GetDelayCost(), 2).ToString),
                    ("Missed Connections",
                        currentSolution.RoutingLayer.ClosureLayer.SequencingLayer.GetMissedConnectionSequences().Count().ToString),
                    ("Connection Cost", Math.Round(currentSolution.GetConnectionsCost(), 2).ToString),
                    newLine,
                    ("Best Cost", Math.Round(bestSolution.GetTotalCost(), 2).ToString),
                    ("Best Delay Cost", Math.Round(bestSolution.GetDelayCost(), 2).ToString),
                    ("Best Routing Cost", Math.Round(bestSolution.GetRoutingCost(), 2).ToString),
                    ("Best Missed Connections", bestSolution.RoutingLayer.ClosureLayer.SequencingLayer.GetMissedConnectionSequences().Count().ToString),
                    newLine,
                    ("Taboo Arcs", tabooList.GetProhibitedArcsInfo),
                    ("Taboo Routing's", tabooList.GetProhibitedRouteInto),
                    ("Prohibited Solutions", (tabooList as TabooList)._prohibitedSolutions.Count.ToString)
                );
                
                
                // Step.
                try
                {
                    currentSolution = Step(
                        iter,
                        currentSolution.Clone(), 
                        bestSolution.GetTotalCost(), 
                        tabooList, 
                        random);
                }
                catch (NoMoves _)
                {
                    Console.WriteLine($"Cost = {currentSolution.GetTotalCost()}");
                    if (Math.Abs(currentSolution.GetTotalCost()) < 1e-8)
                    {
                        break;
                    }
                    else
                    {
                        throw;
                    }
                }
                

                // best cost improvement ==> clear taboo list
                if (currentSolution.GetTotalCost() < 0.9999 * bestSolution.GetTotalCost())
                {
                    bestSolution = currentSolution.Clone();
                    tabooList.Clear();
                }
                
                report.Add(cost: currentSolution.GetTotalCost(), bestCost: bestSolution.GetTotalCost());
                
               
                if (sw.Elapsed >= maxRunTime)
                {
                    Console.WriteLine($"Stopping, max time={maxRunTime.Show()} reached.");
                    break;
                }

                if (bestSolution.GetTotalCost() <= terminationCost)
                {
                    Console.WriteLine($"Stopping, termination cost={Math.Round(terminationCost, 2)} reached " +
                                      $"(best solution cost={bestSolution.GetTotalCost()}).");
                    break;
                }
            }

            report.Result = bestSolution;
            return report;
        }
    }
}