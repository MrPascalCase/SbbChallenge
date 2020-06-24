using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SbbChallenge.Helpers;

using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly IntegrityChecks.Asserts.Switch ProblemInitialization = 
            new Switch(nameof(ProblemDefinition.Problem) + "." + nameof(ProblemInitialization), 
                "machine indices match the occupations",
                "not(loadConnections) implies not(Connections.Any())");
    }
}

namespace sbbChallange.ProblemDefinition
{
    public class Problem : 
        IReadOnlyList<Job>,
        IProblem
    {
        IEnumerable<IJob> IProblem.Jobs => Jobs;
        ObjectiveType IProblem.Objective => Objective;
        string IProblem.ProblemName => ProblemName;
        
        public readonly string ProblemName;
        public readonly ObjectiveType Objective;
        public readonly IReadOnlyList<Job> Jobs;
        public readonly IReadOnlyList<TimeSpan> ReleaseTimes;
        public readonly bool RoutingOptionsExist;

        public int Count => Jobs.Count;
        public int JobCount => Jobs.Count;
        public int MachineCount => ReleaseTimes.Count;
        public int OperationCount => Jobs[JobCount - 1].LastOperation + 1;
        public readonly int ConnectionCount;
        public int MachineAndConnectionCount => MachineCount + ConnectionCount;
        
        
        public IEnumerator<Job> GetEnumerator() => Jobs.GetEnumerator();
        public Job this[int index] => Jobs[index];
        IEnumerator IEnumerable.GetEnumerator() => Jobs.GetEnumerator();


        public Problem (
            IProblem problem, 
            bool loadConnections)
        {
            Objective = problem.Objective;
            ProblemName = problem.ProblemName;
            
            // During the initialization phase we keep a dictionary of the machines:
            var machineToIdMap = new Dictionary<IMachine, int>();
            int MachineLookUp(IMachine machine)
            {
                if (machineToIdMap.TryGetValue(machine, out var value)) return value;

                var nextId = machineToIdMap.Count + 1;
                machineToIdMap.Add(machine, nextId);
                return nextId;
            }
            
            // During the initialization phase we keep a dictionary of connections:
            int machineCount = problem.MachineCount();
            var iConnectionToBjsConnection = new Dictionary<IConnection, BjsConnection>();
            BjsConnection ConnectionLookup (IConnection connection)
            {
                if (iConnectionToBjsConnection.TryGetValue(connection, out var value)) return value;

                var nextId = machineCount + iConnectionToBjsConnection.Count + 1;
                var newCon = new BjsConnection(nextId, connection.Length);
                iConnectionToBjsConnection.Add(connection, newCon);
                return newCon;
            }
            
            IJob[] enumerated = problem.Jobs.ToArray();
            var jobs = new Job[enumerated.Length];
            jobs[0] = new Job(enumerated[0], 0, 0, MachineLookUp, ConnectionLookup,loadConnections);
            for (int i = 1; i < enumerated.Length; i++)
            {
                jobs[i] = new Job(
                    enumerated[i], i, jobs[i - 1].LastOperation + 1, MachineLookUp, ConnectionLookup, loadConnections);
            }

            Jobs = jobs;
            
            
            var releaseTimes = new TimeSpan[machineToIdMap.Count + 1];
            foreach (KeyValuePair<IMachine,int> pair in machineToIdMap)
            {
                releaseTimes[pair.Value] = pair.Key.ReleaseTime;
            }

            ReleaseTimes = releaseTimes;
            ConnectionCount = iConnectionToBjsConnection.Count;

            RoutingOptionsExist = jobs.Any(j => j.Routes.Count > 1);
            // !loadConnections => !Connections.Any()
            Assert(ProblemInitialization, loadConnections || (ConnectionCount == 0));
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("Problem {");
            foreach (Job job in Jobs)
            {
                builder.AppendLine(job.ToString(1));
            }
            builder.AppendLine("}");
            return builder.ToString();
        }
    }

    public class Job : 
        IReadOnlyList<Route>,
        IJob
    {
        IEnumerable<IRoute> IJob.Routes => Routes;
        
        public readonly IReadOnlyList<Route> Routes;
        public readonly int Id;
        public readonly int LongestRoute;
        public readonly int FirstOperation;
        public readonly int LastOperation;

        public int Count => Routes.Count;

        public readonly IReadOnlyList<int> MachinesUsedByAllRoutes;

        public Route this[int index] => Routes[index];
        public IEnumerator<Route> GetEnumerator() => Routes.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => Routes.GetEnumerator();


        public Job(
            IJob job,
            int id, 
            int firstOperationId,
            Func<IMachine, int> machineLookUp,
            Func<IConnection, BjsConnection> connectionLookup,
            bool loadConnections)
        {
            Id = id;
            FirstOperation = firstOperationId;
            
            IRoute[] enumerated = job.Routes.ToArray();
            var routes = new Route[enumerated.Length];

            for (int i = 0; i < routes.Length; i++)
            {
                routes[i] = new Route(Id, firstOperationId, enumerated[i], machineLookUp, connectionLookup, loadConnections);
            }

            Routes = routes;
            LongestRoute = Routes.Select(r => r.Count).Max();
            LastOperation = FirstOperation + LongestRoute - 1;

            var machinesUsedByAllRoutes = Routes[0].AllMachineAndConnectionIds().ToHashSet();
            foreach (var route in Routes.Skip(1))
            {
                machinesUsedByAllRoutes.IntersectWith(route.AllMachineAndConnectionIds());
            }
            MachinesUsedByAllRoutes = machinesUsedByAllRoutes.ToArray();
        }

        public string ToString(int indent)
        {
            StringBuilder builder = new StringBuilder();
            void Add(string line) => builder.Append(new string(' ', 3 * indent)).AppendLine(line);
            Add("Job {");
            foreach (Route route in Routes)
            {
                Add(route.ToString(indent + 1));
            }            
            Add("}");
            return builder.ToString();
        }
    }


    public class Route :
        IRoute,
        IReadOnlyList<Operation>
    {
        double IRoute.RoutingPenalty => RoutingPenalty;
        IEnumerable<IOperation> IRoute.Operations => Operations;

        public readonly IReadOnlyList<Operation> Operations;
        public readonly IReadOnlyList<BjsConnection> Connections;
        public readonly double RoutingPenalty;

        public IEnumerator<Operation> GetEnumerator() => Operations.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int Count => Operations.Count;
        public Operation this[int index] => Operations[index];

        public IEnumerable<int> AllMachineAndConnectionIds()
        {
            return
                Operations.SelectMany(o => o.MachineIds)
                    .Concat(Operations.SelectMany(o => o.IncomingConnections.Select(c => c.Id)))
                    .Concat(Operations.SelectMany(o => o.OutgoingConnections.Select(c => c.Id)))
                    .Distinct();
        }

        /// <summary>
        /// The algorithm depends on the entryTimes being set in a specific way.
        ///     Let 'ops' be the list of operations, then:
        ///     if there is no entryTime set for ops[i] (t=TimeSpan.MinValue)
        ///     then the entryTime should be set to 'ops[i-1].EntryTime + ops[i-1].Runtime'
        ///     as ops[i] cannot be reached before that time, hence this is a valid entry time.
        /// In effect, this removes the necessities to keep arcs form the imaginary sigma node.
        /// </summary>
        private static TimeSpan[] CalculateEarliestEntries(IRoute route)
        {
            var ops = route.Operations;
            var enumerated = ops as IOperation[] ?? ops.ToArray(); // avoid multiple enumeration

            var times = new TimeSpan[enumerated.Length];
            times[0] = enumerated[0].EarliestEntry;

            for (int i = 1; i < times.Length; i++)
            {
                times[i] = enumerated[i].EarliestEntry.Max(enumerated[i - 1].Runtime + times[i - 1]);
            }

            return times;
        }

        /// <summary>
        /// Similar to the method above we rely on the latestEntries being set in a specific way.
        /// If the operation in question has 0 delay weight, we can set the latest entry to whatever value we like
        /// without changing the problem. It this case we set the time to the latest time we have to enter this
        /// operation such that (assuming no conflicts with other jobs) we will reach later checkpoints in time.
        /// This is then used by the build-up heuristic to initially place jobs.
        /// </summary>
        private static TimeSpan[] CalculateLatestEntries(IRoute route)
        {
            var ops = route.Operations;
            var enumerated = ops as IOperation[] ?? ops.ToArray();

            var times = new TimeSpan[enumerated.Length];
            times[times.Length - 1] = enumerated[enumerated.Length - 1].LatestEntry;

            for (int i = times.Length - 2; i >= 0; i--)
            {

                if (enumerated[i].DelayWeight > 0)
                {
                    times[i] = enumerated[i].LatestEntry;
                }
                else
                {
                    times[i] = times[i + 1] - enumerated[i].Runtime;
                }
            }

            return times;
        }

        public TimeSpan MinimumRuntime()
        {
            return Operations.Select(o => o.Runtime).Sum();
        }


        public Route(
            int jobIndex,
            int firstOperationIndex,
            IRoute route,
            Func<IMachine, int> machineIdLookUp,
            Func<IConnection, BjsConnection> connectionLookup,
            bool loadConnections)
        {
            RoutingPenalty = route.RoutingPenalty;
            var enumerated = route.Operations.ToArray();

            var array = new Operation[enumerated.Length];
            var machines = new int[enumerated.Length][];

            for (int i = 0; i < machines.Length; i++)
            {
                machines[i] = enumerated[i].Machines.Select(machineIdLookUp).ToArray();
            }

            // Local function, which captures 'machines' and is passed into the Operation ctor.
            MachineOccupation GetOccupation(int startIndex, int machineId, TimeSpan releaseTime)
            {
                Assert(ProblemInitialization, machines[-firstOperationIndex + startIndex].Contains(machineId));

                int GetEndIndex()
                {
                    for (int i = startIndex;; i++)
                        if (!machines[-firstOperationIndex + i + 1].Contains(machineId))
                            return i;
                }

                return new MachineOccupation(startIndex, GetEndIndex(), machineId, jobIndex,
                    MachineOccupationType.Normal, releaseTime);
            }

            var actualEarliestEntryTimes = CalculateEarliestEntries(route);
            var actualLatestEntryTimes = CalculateLatestEntries(route);

            var list = new List<MachineOccupation>();
            for (int i = 0; i < array.Length; i++)
            {
                array[i] = new Operation(
                    firstOperationIndex + i, jobIndex, enumerated[i],
                    actualEarliestEntryTimes[i], actualLatestEntryTimes[i],
                    GetOccupation, ref list, machineIdLookUp, connectionLookup,
                    loadConnections);
            }

            Operations = array;
        }

        public string ToString(int indent)
        {
            StringBuilder builder = new StringBuilder();
            void Add(string line) => builder.Append(new string(' ', 3 * indent)).Append(line);
            
            Add("Route {\n");

            foreach (var (op, i) in Operations.Numbered()) Add(op.ToString(indent + 1) + "\n");

            Add("}");
            return builder.ToString();
        }

        public override string ToString() => ToString(0);
    }

    public class BjsConnection : IConnection
    {
        TimeSpan IConnection.Length { get; }
        
        public readonly TimeSpan Length;
        public readonly int Id;

        public BjsConnection(int id, TimeSpan length)
        {
            Length = length;
            Id = id;
        }
        
        public bool Equals(IConnection other)
        {
            return other != null
                   && other is BjsConnection conn
                   && conn.Id == Id;
        }

        public override int GetHashCode() => throw new Exception();

        public override string ToString() => $"Connection {{ id={Id}, length={Length.Show()} }}";
    }

    public struct IntMachine : IMachine
    {
        TimeSpan IMachine.ReleaseTime => ReleaseTime;
        
        public readonly int Id;
        public readonly TimeSpan ReleaseTime;


        public IntMachine(int machineId, TimeSpan releaseTime)
        {
            Id = machineId;
            ReleaseTime = releaseTime;
        }
        
        public bool Equals(IMachine other) => other is IntMachine im && im.Id == Id;

        public override int GetHashCode() => Id;

        public override string ToString() => $"M{Id}";
    }

    public class Operation : IOperation
    {
        TimeSpan IOperation.Runtime => Runtime;
        TimeSpan IOperation.EarliestEntry => EarliestEarliestEntry;
        TimeSpan IOperation.LatestEntry => LatestEntry;
        double IOperation.DelayWeight => DelayWeight;
        IEnumerable<IMachine> IOperation.Machines => Machines.Select(m => (IMachine)m);
        IEnumerable<IConnection> IOperation.OutgoingConnections => OutgoingConnections;
        IEnumerable<IConnection> IOperation.IncomingConnections => IncomingConnections;

        public IEnumerable<int> MachineIds => Machines.Select(m => m.Id);
        
        public readonly int Id;
        public readonly IReadOnlyList<IntMachine> Machines;
        public readonly TimeSpan Runtime;
        public readonly double DelayWeight;
        public readonly TimeSpan EarliestEarliestEntry;
        public readonly TimeSpan LatestEntry;
        public readonly IReadOnlyList<MachineOccupation> StartingMachineOccupations;
        public readonly IReadOnlyList<MachineOccupation> EndingMachineOccupations;
        public readonly IReadOnlyList<BjsConnection> OutgoingConnections;
        public readonly IReadOnlyList<BjsConnection> IncomingConnections;


        public Operation(
            int id,
            int jobId,
            IOperation operation,
            TimeSpan actualEarliestEntryTime,
            TimeSpan actualLatestEntryTime,
            // given the Operation.Id and the resp Machine, return the Occupation
            Func<int, int, TimeSpan, MachineOccupation> getMachineOccupation,
            ref List<MachineOccupation> currentOccupations,
            // given a IMachine lookup its new id
            Func<IMachine, int> machineIdLookup,
            Func<IConnection, BjsConnection> connectionLookup,
            bool loadConnections)
        {
            Id = id;
            Runtime = operation.Runtime;
            DelayWeight = operation.DelayWeight;
            EarliestEarliestEntry = actualEarliestEntryTime;
            LatestEntry = actualLatestEntryTime;
            Machines = operation.Machines.Select(m => new IntMachine(machineIdLookup(m), m.ReleaseTime)).ToArray();

            // Starting occupations first, some might end here directly.            
            List<MachineOccupation> startingHere = new List<MachineOccupation>();

            foreach (var m in Machines)
            {
                if (currentOccupations.Select(c => c.MachineId).Contains(m.Id)) continue;

                startingHere.Add(getMachineOccupation(id, m.Id, m.ReleaseTime));
            }


            currentOccupations.AddRange(startingHere);

            // Ending occupations:
            var endingHere = currentOccupations.Where(m => m.LastOperation == Id).ToList();
            currentOccupations = currentOccupations.Except(endingHere).ToList();


            // Connections:
            OutgoingConnections = !loadConnections
                ? new BjsConnection[0]
                : (operation.OutgoingConnections?
                       .Where(c => c != null).Select(connectionLookup).ToArray() ?? new BjsConnection[0]);

            IncomingConnections = !loadConnections
                ? new BjsConnection[0]
                : (operation.IncomingConnections?
                       .Where(c => c != null).Select(connectionLookup).ToArray() ?? new BjsConnection[0]);

            foreach (BjsConnection connection in OutgoingConnections)
            {
                MachineOccupation occ = new MachineOccupation(id, id, connection.Id, jobId,
                    MachineOccupationType.ConnectionOrigin, connection.Length);

                startingHere.Add(occ);
                endingHere.Add(occ);
            }

            foreach (BjsConnection connection in IncomingConnections)
            {
                MachineOccupation occ = new MachineOccupation(id, id, connection.Id, jobId,
                    MachineOccupationType.ConnectionTarget, TimeSpan.Zero);

                startingHere.Add(occ);
                endingHere.Add(occ);
            }

            StartingMachineOccupations = startingHere.ToArray();
            EndingMachineOccupations = endingHere.ToArray();
        }


        public string ToString (int indent) => new string(' ', 3 * indent) + ToString();
        
        public override string ToString()
        {
            StringBuilder builder = new StringBuilder().Append("Operation { ");
            builder.Append($"id={Id}, ");
            builder.Append($"earliest={EarliestEarliestEntry.Show()}, ");
            builder.Append($"latest={LatestEntry.Show()}, ");
            builder.Append($"t={Runtime.Show()}, ");
            builder.Append($"m={{ {MachineIds.JoinToString(", ")} }}, ");
           
            if (OutgoingConnections.Any())
            {
                builder.Append($"out={{ {OutgoingConnections.JoinToString(", ")} }}, ");
            }
            if (IncomingConnections.Any())
            {
                builder.Append($"in={{ {IncomingConnections.JoinToString(", ")} }}, ");
            }
            return builder.Append("}").ToString();
        }
    }
}