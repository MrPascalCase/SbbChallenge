using System;
using System.Linq;
using SbbChallenge.Helpers;
using sbbChallange.Layers;
using static sbbChallange.IntegrityChecks.Asserts;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch SolutionChecks =
            new Switch(nameof(ProblemDefinition.Solution)+ "." + nameof(SolutionChecks),
                "index in range checks (getOperation, getRoute, getJob)", "set route exists");
    }

}

namespace sbbChallange.ProblemDefinition
{
    public class Solution : ISolution
    {
        public Problem Problem { get; }

        /// <summary>  A map : Job.Id -> Route. No route is selected iff entry is null. </summary>
        private readonly Route[] _routeSelection;

        /// <summary>  A map : Operation.Id -> Timespan. </summary>
        private readonly TimeSpan[] _entryTimes;

        public Solution(Problem problem)
        {
            Problem = problem;
            _routeSelection = new Route[Problem.JobCount];
            _entryTimes = new TimeSpan[problem.OperationCount];
        }

        private Solution(Problem problem, Route[] routeSelection, TimeSpan[] entryTimes)
        {
            Problem = problem;
            _routeSelection = routeSelection;
            _entryTimes = entryTimes;
        }

        public TimeSpan GetEntryTime(int operationId)
        {
            Assert(SolutionChecks, 0 <= operationId && operationId < Problem.OperationCount);
            return _entryTimes[operationId];
        }

        public void SetEntryTime(int operationId, TimeSpan timeSpan)
        {
            Assert(
                SolutionChecks, 
                0 <= operationId 
                && operationId <= Problem.OperationCount 
                && timeSpan >= TimeSpan.Zero);
            
            _entryTimes[operationId] = timeSpan;
        }

        public Route GetRoute(Job job, bool canBeNull = false)
        {
            if (!canBeNull && _routeSelection[job.Id] == null)
                throw new Exception($"GetRoute called with {nameof(canBeNull)}=={canBeNull}, but resp. route is null.");
            
            return _routeSelection[job.Id];
        }

        public void SetRoute(Job job, Route route)
        {
            Assert(SolutionChecks, job.Contains(route));

            _routeSelection[job.Id] = route;
        }

        /// <summary>
        /// Uses a binary search on the job array to return the job which contains operation with id 'operationId'
        /// </summary>
        public Job GetJob(int operationId)
        {
            Assert(SolutionChecks, 0 <= operationId && operationId < Problem.OperationCount);
            
            int low = 0;
            int high = Problem.JobCount - 1;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (Problem[mid].FirstOperation <= operationId && operationId <= Problem[mid].LastOperation)
                {
                    var job = Problem[mid];
                    Assert(SolutionChecks, 
                        job.FirstOperation <= operationId && operationId <= job.LastOperation);
                    return job;
                }
                if (operationId < Problem[mid].FirstOperation)
                {
                    high = mid - 1;
                }
                else
                {
                    Assert(SolutionChecks, Problem[mid].LastOperation < operationId);
                    low = mid + 1;
                }
            }

            throw new Exception("Job not found.");
        }

        public Route GetRoute(int operationId, bool canBeNull = false)
        {
            Job j = GetJob(operationId);

            if (!canBeNull)
            {
                Assert(SolutionChecks, _routeSelection[j.Id] != null,
                    $"Tried to get the route for job={j.Id}, operation={operationId}. No route is set.");
                Assert(SolutionChecks, _routeSelection[j.Id].Any(o => o.Id == operationId));
            }

            return _routeSelection[j.Id];
        }
        
        public TimeSpan GetMachineReleaseTime(MachineOccupation first, MachineOccupation second)
        {
            Assert(SolutionChecks, first.MachineId == second.MachineId, "first.MachineId == second.MachineId");

            return first.ReleaseTime;
            
            // I changed this to always return the fixed release time which is also the connection time. this solves the
            // problem of the determining the termination criterion, as now all setup times are sequence independent.
            // penalties for missed connections will be added as soft penalties from the sequencing layer.
            
            //if (first.Type != MachineOccupationType.ConnectionTarget) return first.ReleaseTime;
            //return _missedConnectionPenalty;
        }

        /// <summary>
        /// GetOperation (operationId) returns the operation or (null iff no route is set)
        /// </summary>
        public Operation GetOperation(int operationId, bool canBeNull = false)
        {
            Job j = GetJob(operationId);
            
            if (_routeSelection[j.Id] == null) return null;

            var route = GetRoute(j);
            if (route.Count < j.LongestRoute
                && j.FirstOperation + route.Count <= operationId)
            {
                // the operationId is valid and the job is correct, but with the current route set,
                // there is no such operation.
                if (canBeNull) return null;

                throw new Exception();
            }
            
            Operation o = route[operationId - j.FirstOperation];
            
            Assert(SolutionChecks, o.Id == operationId);
            
            return o;
        }

        public ISolution Clone()
        {
            var routeSelectionCopy = new Route[Problem.JobCount];
            Array.Copy(_routeSelection, routeSelectionCopy, _routeSelection.Length);
            
            var entryTimesCopy = new TimeSpan[Problem.OperationCount];
            Array.Copy(_entryTimes, entryTimesCopy, _entryTimes.Length);
            
            return new Solution(Problem, routeSelectionCopy, entryTimesCopy);
        }

        public bool Equals(ISolution other)
        {
            if (ReferenceEquals(null, other)) return false;
            
            if (!ReferenceEquals(Problem, other.Problem)) return false;

            foreach (Job job in Problem)
            {
                if (!ReferenceEquals(GetRoute(job), other.GetRoute(job))) return false;
            }

            for (int i = 0; i < Problem.OperationCount; i++)
            {
                if (GetEntryTime(i) != other.GetEntryTime(i)) return false;
            }

            return true;
        }

        public override int GetHashCode()
        {
            throw new NotImplementedException();
        }
    }
}