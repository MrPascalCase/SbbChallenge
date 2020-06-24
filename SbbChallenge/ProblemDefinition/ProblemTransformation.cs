using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using SbbChallenge.Helpers;
using sbbChallange.SbbProblem;
using SbbChallenge;

namespace sbbChallange.ProblemDefinition
{
    public static class ProblemTransformation
    {
        public static void PrintProblemSizes()
        {
            foreach (var file in EntryPoint.InputFiles)
            {
                Scenario scenario = Scenario.ReadFromFile(file);
                scenario.Setup();

                Console.WriteLine("Before removing redundancy:");
                Console.WriteLine(scenario.NodesFixedAlternativeArcCountsToString());

                IProblem iprob = scenario.RemoveRedundantMachines();
                iprob = iprob.RemoveRedundantOperations();
                
                Console.WriteLine("After removing redundancy:");
                Console.WriteLine(iprob.NodesFixedAlternativeArcCountsToString());
            }
        }
        
        
        /// <summary>
        /// Reports the following counts and rates in a readable string:
        /// - #nodes
        /// - #fixed arcs
        /// - #alternative arcs
        /// - #fixed arcs / #nodes
        /// - #alternative arcs / #nodes
        /// As was suggested by Mazzarello & Ottaviani (2005) "A traffic management system for real-time traffic
        /// optimisation in railways", page 265, to quantify the complexity of the problem.
        /// </summary>
        public static string NodesFixedAlternativeArcCountsToString(this IProblem problem)
        {
            StringBuilder builder = new StringBuilder();

            var prob = new Problem(problem, false);

            Route[] routes = prob.Jobs.Select(j => j.Routes.ArgMax(r => r.Operations.Count)).ToArray();
            
            // Conjunctive arcs between operations:
            int fixedArcsCount = routes
                .Select(r => r.Operations.Count() - 1)
                .Sum();
            
            // Conjunctive arcs from sigma:
            foreach (var op in problem.Jobs
                .Select(j => j.Routes.ArgMax(r => r.Operations.Count()))
                .SelectMany(r => r.Operations))
            {
                if (op.EarliestEntry > TimeSpan.Zero) fixedArcsCount++;
            }
            
            // Disjunctive arcs, first count only non-transitive
            int[] usagesPerMachine = new int[prob.MachineCount];

            foreach (Route route in routes)
            {
                foreach (int m in route.Operations.SelectMany(r => r.EndingMachineOccupations.Select(occ => occ.MachineId)))
                {
                    usagesPerMachine[m] += 1;
                }
            }

            var nodeCount = routes.Select(r => r.Operations.Count).Sum();
            var alternativeCount = usagesPerMachine.Select(n => (n - 1) * n).Sum();
            
            builder.AppendLine($"Problem = {prob.ProblemName}");
            builder.AppendLine($"# nodes = {nodeCount}");
            builder.AppendLine($"# fixed arcs = {fixedArcsCount}");
            builder.AppendLine($"# alternative arcs = {alternativeCount}");
            builder.AppendLine($"# fixed arcs / # nodes = {Math.Round(1.0 * fixedArcsCount / nodeCount, 2)}");
            builder.AppendLine($"# alternative arcs / # nodes = {Math.Round(1.0 * alternativeCount / nodeCount, 2)}");
            
            return builder.ToString();
        }


        public static IProblem RestrictRouteChoices(this IProblem problem, int maxNumberRoutes, Random random)
        {
            return new ModifiedIProblem(
                problem.Jobs.Select(j => new ModifiedIJob(j.Routes.Shuffle(random).OrderBy(r => r.RoutingPenalty).Take(maxNumberRoutes))), 
                problem.Objective, 
                problem.ProblemName);
        }
        
        public static int MachineCount(this IProblem problem)
        {
            return problem.Jobs
                .SelectMany(j => j.Routes)
                .SelectMany(r => r.Operations)
                .SelectMany(o => o.Machines)
                .Distinct()
                .Count();
        }

        public static string PrintProblemOverviewStats(this IProblem problem)
        {
            StringBuilder builder = new StringBuilder();

            builder.AppendLine($"Name = {problem.ProblemName}");
            builder.AppendLine($"Job count = {problem.Jobs.Count()}");
            builder.AppendLine($"Average number of routes per job = {problem.Jobs.Select(j => j.Routes.Count()).Average()}");
            builder.AppendLine($"Max number of routes of a job = {problem.Jobs.Select(j => j.Routes.Count()).Max()}");
            
            var allRoutes = problem.Jobs.SelectMany(j => j.Routes).ToArray();

            builder.AppendLine($"Average number of sections per route = {allRoutes.Select(r => r.Operations.Count()).Average()}");
            builder.AppendLine($"Average number of machines per route = {allRoutes.Select(r => r.Operations).Select(list => list.SelectMany(o => o.Machines).Distinct().Count()).Average()}");
            builder.AppendLine($"Connection count = {problem.Jobs.Select(j => j.Routes.First()).SelectMany(r => r.Operations.SelectMany(o => o.OutgoingConnections)).Count()}");
            
            
            return builder.ToString();
        }
        
        
        public static HashSet<IMachine> GetRedundantMachines(this IProblem problem)
        {
            IEnumerable<IOperation> allOperations =
                problem.Jobs
                    .SelectMany(j => j.Routes)
                    .SelectMany(r => r.Operations);

            // if machine k maps to { m_0, ..., m_k },
            // this means that whenever k is present on a operation, so are m_0, ..., m_k
            var coOccurenceMap = new Dictionary<IMachine, HashSet<IMachine>>();

            foreach (var op in allOperations)
            foreach (var m in op.Machines)
                if (coOccurenceMap.TryGetValue(m, out var set))
                    set.IntersectWith(op.Machines);

                else
                    coOccurenceMap.Add(m, op.Machines.Except(new[] {m}).ToHashSet());


            var redundant = new HashSet<IMachine>();

            foreach (var kvp in coOccurenceMap
                .Where(kvp => !kvp.Value.All(m => redundant.Contains(m))))
            {
                redundant.Add(kvp.Key);
            }

            return redundant;
        }

        public class ModifiedIProblem : IProblem
        {
            public ModifiedIProblem(IEnumerable<IJob> jobs, ObjectiveType objective, string name)
            {
                Objective = objective;
                Jobs = jobs.ToArray();
                ProblemName = name;
            }

            public ObjectiveType Objective { get; }
            public IEnumerable<IJob> Jobs { get; }
            public string ProblemName { get; }
        }

        public class ModifiedIJob : IJob
        {
            public ModifiedIJob(IEnumerable<IRoute> routes)
            {
                Routes = routes.ToArray();
            }

            public IEnumerable<IRoute> Routes { get; }
        }

        public class ModifiedIRoute : IRoute
        {
            public ModifiedIRoute( double routingPenalty, IEnumerable<IOperation> operations)
            {
                Operations = operations.ToArray();
                RoutingPenalty = routingPenalty;
            }

            public IEnumerable<IOperation> Operations { get; }
            public double RoutingPenalty { get; }
        }

        public class ModifiedIOperation : IOperation
        {
            public ModifiedIOperation(
                TimeSpan runtime, TimeSpan earliestEntry, TimeSpan latestEntry,
                double delayWeight, IEnumerable<IMachine> machines, 
                IEnumerable<IConnection> outgoingConnections, IEnumerable<IConnection> incomingConnections)
            {
                Runtime = runtime;
                EarliestEntry = earliestEntry;
                LatestEntry = latestEntry;
                DelayWeight = delayWeight;
                Machines = machines;
                OutgoingConnections = outgoingConnections;
                IncomingConnections = incomingConnections;
            }

            public TimeSpan Runtime { get; }
            public TimeSpan EarliestEntry { get; }
            public TimeSpan LatestEntry { get; }
            public double DelayWeight { get; }
            public IEnumerable<IMachine> Machines { get; }
            public IEnumerable<IConnection> OutgoingConnections { get; }
            public IEnumerable<IConnection> IncomingConnections { get; }
        }

        public static void RemoveRedundantRoutes(this IProblem problem)
        {
            foreach (IJob job in problem.Jobs)
            {
                job.RemoveRedundantRoutes();
            }
        }

        public static void RemoveRedundantRoutes(this IJob job)
        {
            List<IRoute> routes = new List<IRoute>{job.Routes.First()};

            foreach (IRoute route in job.Routes.Skip(1))
            {
                // Do we need the Route 'route', or is it already represented by the selected set?
                foreach (var selectedRoute in routes)
                {
                    switch (Comparison.Route(selectedRoute, route).Value)
                    {
                        case Comparison.Type.Same:
                            Console.Write(" same ");
                            goto TakeNext;
                        case Comparison.Type.ABetterThanB:
                            Console.Write(" A_better ");
                            Console.ReadLine();

                            goto TakeNext; 

                        case Comparison.Type.AWorseThanB:
                            routes.Remove(route);
                            routes.Add(selectedRoute);
                            Console.Write(" B_better ");
                            Console.ReadLine();

                            goto TakeNext;

                        case Comparison.Type.Different:
                            Console.Write("  diff  ");
                            continue;

                        default: throw new ArgumentException();
                    }
                }
                // 'route' is different form all selected routes
                routes.Add(route);
                
                TakeNext: continue;
            }
        }
        
        public struct Comparison
        {
            public static Comparison Same = new Comparison(Type.Same);
            public static Comparison ABetterThanB = new Comparison(Type.ABetterThanB);
            public static Comparison AWorseThanB = new Comparison(Type.AWorseThanB);
            public static Comparison Different = new Comparison(Type.Different);
            
            internal enum Type
            {
                Same, ABetterThanB, AWorseThanB, Different
            }

            // Since it's an abelian monoid, why not :-)
            public static Comparison operator * (Comparison lhs, Comparison rhs)
            {
                if (lhs.Value == Type.Different || rhs.Value == Type.Different) 
                    return new Comparison(Type.Different);
                
                if (lhs.Value == Type.Same || rhs.Value == Type.Same) 
                    return new Comparison(Type.Same);
                
                if (lhs.Value == Type.Same && rhs.Value == Type.ABetterThanB
                    || lhs.Value == Type.ABetterThanB && rhs.Value == Type.Same
                    || lhs.Value == Type.ABetterThanB && rhs.Value == Type.ABetterThanB) 
                    return new Comparison(Type.ABetterThanB);
                
                if (lhs.Value == Type.Same && rhs.Value == Type.AWorseThanB
                    || lhs.Value == Type.AWorseThanB && rhs.Value == Type.Same
                    || lhs.Value == Type.AWorseThanB && rhs.Value == Type.AWorseThanB) 
                    return new Comparison(Type.AWorseThanB);
                
                return new Comparison(Type.Different);
            }

            public static Comparison Route(IRoute a, IRoute b)
            {
                var current = Same;
                foreach (var (opA, opB) in a.Operations.Zip(b.Operations, (opA, opB) => (opA, opB)))
                {
                    current *= Lower(opA.Runtime, opB.Runtime)
                               * Lower(opA.EarliestEntry, opB.EarliestEntry)
                               * Higher(opA.LatestEntry, opB.LatestEntry)
                               * Lower(opA.DelayWeight, opB.DelayWeight)
                               * FewerElements(opA.Machines, opB.Machines)
                               * FewerElements(opA.IncomingConnections, opB.IncomingConnections)
                               * FewerElements(opA.OutgoingConnections, opB.OutgoingConnections);
                    
                    if (current.Value == Type.Different) return Different;
                }

                return current;
            }

            public static Comparison Lower(IComparable a, IComparable b)
            {
                int comp = a.CompareTo(b);
                if (comp == 0) return new Comparison(Type.Same);
                if (comp < 0) return new Comparison(Type.ABetterThanB);
                return new Comparison(Type.AWorseThanB);
            }
            
            public static Comparison Higher (IComparable a, IComparable b)
            {
                int comp = -1 * a.CompareTo(b);
                if (comp == 0) return new Comparison(Type.Same);
                if (comp < 0) return new Comparison(Type.ABetterThanB);
                return new Comparison(Type.AWorseThanB);
            }

            public class ReferenceComparer<T> : IEqualityComparer<T>
            {
                public static readonly ReferenceComparer<T> Instance = new ReferenceComparer<T>();
                
                public bool Equals(T x, T y) => ReferenceEquals(x, y);
                
                public int GetHashCode(T obj) => obj.GetHashCode();
            }
            
            public static Comparison FewerElements<T>(IEnumerable<T> a, IEnumerable<T> b) where T : class
            {
                bool aContainsAllB = !b.Except(a, ReferenceComparer<T>.Instance).Any();
                bool bContainsAllA = !a.Except(b, ReferenceComparer<T>.Instance).Any();
                if (aContainsAllB && bContainsAllA) return Same;
                if (bContainsAllA) return ABetterThanB;
                if (aContainsAllB) return AWorseThanB;
                return Different;
            }

            internal readonly Type Value;

            private Comparison(Type type) => Value = type;
        }


        public static bool TryMerge(this IOperation fst, IOperation snd, out IOperation result)
        {
            if (fst.Machines.Except(snd.Machines).Any()
                || snd.Machines.Except(fst.Machines).Any()
                || fst.OutgoingConnections.Any()
                || snd.IncomingConnections.Any()
                || (fst.DelayWeight > 0 && snd.DelayWeight > 0))
            {
                result = null;
                return false;
            }

            result = new ModifiedIOperation(
                fst.Runtime + snd.Runtime,
                
                snd.EarliestEntry == TimeSpan.MinValue 
                    ? fst.EarliestEntry
                    : fst.EarliestEntry.Max(snd.EarliestEntry - fst.Runtime),
                
                snd.LatestEntry == TimeSpan.MaxValue
                    ? fst.LatestEntry 
                    : fst.LatestEntry.Min(snd.LatestEntry - fst.Runtime),
                
                fst.DelayWeight + snd.DelayWeight,
                
                fst.Machines,
                
                snd.OutgoingConnections,
                fst.IncomingConnections);

            return true;
        }
        

        public static List<IOperation> RemoveRedundantOperations(
            this IEnumerable<IOperation> operations, ref int mergeCounter)
        {
            using (var iter = operations.GetEnumerator())
            {
                List<IOperation> result = new List<IOperation>();
                iter.MoveNext();
                var last = iter.Current;

                while (iter.MoveNext())
                {
                    var current = iter.Current;

                    if (last.TryMerge(current, out var merged))
                    {
                        last = merged;
                        mergeCounter += 1;
                    }
                    else
                    {
                        result.Add(last);
                        last = current;
                    }
                }

                result.Add(last);
                return result;
            }
        }

        public static IProblem RemoveRedundantOperations(this IProblem problem, StringBuilder getReport = null)
        {
            Stopwatch sw = Stopwatch.StartNew();

            int mergeCounter = 0;

            int OpCount() => problem.Jobs
                .SelectMany(j => j.Routes)
                .SelectMany(r => r.Operations)
                .Count();

            var result = new ModifiedIProblem(
                problem.Jobs.Select(
                        j => new ModifiedIJob(j.Routes.Select(
                                r => new ModifiedIRoute(
                                    r.RoutingPenalty,
                                    r.Operations.RemoveRedundantOperations(ref mergeCounter).ToArray()))
                            .ToArray()))
                    .ToArray(), 
                problem.Objective, 
                problem.ProblemName);

            getReport?
                .AppendLine($"-----------------  Report: Removing redundant operations.  -----------------")
                .Append($"# operation redundant = {mergeCounter} ")
                .AppendLine(
                    $"(total {OpCount(),6} operations,   {Math.Round(100.0 * mergeCounter / OpCount()),4}%)")
                .AppendLine($"Simplification took {Math.Round(sw.ElapsedMilliseconds / 1000.0, 2)} sec.")
                .AppendLine("------------------              End Report                ------------------");

            return result;
        }


        public static IProblem RemoveRedundantMachines(this IProblem problem, StringBuilder getReport = null)
        {
            Stopwatch sw = Stopwatch.StartNew();

            var redundant = problem.GetRedundantMachines();

            int MachineCount() => problem.Jobs
                .SelectMany(j => j.Routes)
                .SelectMany(r => r.Operations)
                .SelectMany(o => o.Machines)
                .Distinct()
                .Count();

            int TotalMachineCount() => problem.Jobs
                .SelectMany(j => j.Routes)
                .SelectMany(r => r.Operations)
                .SelectMany(o => o.Machines)
                .Count();


            int OpCount() => problem.Jobs
                .SelectMany(j => j.Routes)
                .SelectMany(r => r.Operations)
                .Count();

            getReport?
                .AppendLine($"------------------  Report: Removing redundant machines.  ------------------")
                .Append($"# machines redundant        = {redundant.Count,6} ")
                .Append($"(total {MachineCount(),6} machines,   {Math.Round(100.0 * redundant.Count / MachineCount()),4}%)")
                .AppendLine();

            int changedOps = 0;
            int machinesRemovedFromOps = 0;

            var result = new ModifiedIProblem(
                problem.Jobs.Select(
                        j => new ModifiedIJob(j.Routes.Select(
                                r => new ModifiedIRoute(r.RoutingPenalty, r.Operations.Select(
                                    o =>
                                    {
                                        if (getReport != null
                                            && o.Machines.Count() != o.Machines.Except(redundant).Count())
                                        {
                                            changedOps++;
                                            machinesRemovedFromOps +=
                                                o.Machines.Count() - o.Machines.Except(redundant).Count();
                                        }

                                        return new ModifiedIOperation(
                                            o.Runtime, o.EarliestEntry, o.LatestEntry,
                                            o.DelayWeight, o.Machines.Except(redundant),
                                            o.OutgoingConnections, o.IncomingConnections);
                                    }).ToArray()))
                            .ToArray()))
                    .ToArray(), 
                problem.Objective, 
                problem.ProblemName);

            getReport?
                .Append($"# changed operations        = {changedOps,6} ")
                .Append($"(total {OpCount(),6} operations, {Math.Round(100.0 * changedOps / OpCount()),4}%)")
                .AppendLine()

                .Append($"# machine instances removed = {machinesRemovedFromOps,6} ")
                .Append($"(total {TotalMachineCount(),6} instances,  {Math.Round(100.0 * machinesRemovedFromOps / TotalMachineCount()),4}%)")
                .AppendLine()

                .AppendLine($"Simplification took {Math.Round(sw.ElapsedMilliseconds / 1000.0, 2)} sec.")
                .AppendLine("------------------              End Report                ------------------");

            return result;
        }
    }
}