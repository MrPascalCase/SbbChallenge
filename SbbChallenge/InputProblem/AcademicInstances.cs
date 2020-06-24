using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using sbbChallange.ProblemDefinition;
using sbbChallange.Layers;
using sbbChallange.Search;

namespace sbbChallange
{
    public static class AcademicInstances
    {
        public static void Start()
        {
            var allFiles = 
                Enumerable.Range(6, 40)
                .Select(i => "la" + i.ToString().PadLeft(2, '0'));

            string datetimeNow = DateTime.Now.ToString().Replace("/", "-").Replace(" ", "_").Replace(":", "-");

            string Input(string name) => $"../../Data/Input/LiteratureProblems/{name}.txt";
            string OutputSvg(string name) => $"../../Data/Input/LiteratureProblems/{name}.svg";
            string Output() => $"../../Data/Output/LiteratureInstances_{datetimeNow}.csv";
            
            if (File.Exists(Output())) File.Delete(Output());
            
            foreach (string problemName in allFiles)
            {
                double min = Double.MaxValue;
                for (int i = 0; i < 5; i++)
                {
                    var problem = new Problem(LiteratureProblem.ReadFormFile(Input(problemName)), false);

                    var jobShop = new JobShopLayer(problem);
                
                    jobShop.CreateInitialSolution(problem.Select(j => (j, j.First())), true);
                
                    //GraphPlot.Plot(jobShop.RoutingLayer.ClosureLayer.SequencingLayer.GraphLayer,"../../plot.svg");

                    //Console.WriteLine("done.");
                
                    var result = SingleThreadSearch.Run("run_0", jobShop, new Random(), 0, 60000);

                    min = Math.Min(min, result.Result.GetTotalCost());
                }
                
                //var result = RealSearchMakeSpan.Run(driver, 0, 50000, default);

                //result.RoutingLayer.ClosureLayer.SequencingLayer.GraphLayer.Plot(OutputSvg(problemName));

                File.AppendAllText(Output(), $"{problemName}, {min}\n");
            }
        }
        
        public class LiteratureProblem : ProblemDefinition.IProblem
        {
            public IEnumerable<ProblemDefinition.IJob> Jobs { get; }
            public ObjectiveType Objective => ObjectiveType.MaximumWeighedTardiness;
            public string ProblemName { get; }

            public LiteratureProblem(LiteratureJob[] jobs, string problemName)
            {
                Jobs = jobs;
                this.ProblemName = problemName;
            }


            public static LiteratureProblem ReadFormFile(string file)
            {
                
                int TryParse(string s) => int.TryParse(s, out var i) ? i : -1;
                
                // create an enumerator of all relevant integer values:
                var parsedFile = File
                    .ReadAllLines(file)
                    .Where(l => !l.StartsWith("$"))                                                  // $  Comments
                    .SelectMany(l => l.Split(new []{' '}, StringSplitOptions.RemoveEmptyEntries))    // split & flatten
                    .Select(TryParse)
                    .GetEnumerator();

                parsedFile.MoveNext();
                var jobCount = parsedFile.Current;

                parsedFile.MoveNext();
                var opCount = parsedFile.Current;
                
                var array = new LiteratureJob[jobCount];
                for (int i = 0; i < jobCount; i++) 
                    array[i] = new LiteratureJob(parsedFile, opCount);
                
                parsedFile.Dispose();
                return new LiteratureProblem(array, file);
            }
        }

        public class LiteratureJob : ProblemDefinition.IJob
        {
            public IEnumerable<ProblemDefinition.IRoute> Routes { get; }
            
            public LiteratureJob(LiteratureRoute route) => Routes = new[] {route};

            public LiteratureJob(IEnumerator<int> parsedFile, int opCount)
            {
                Routes = new[] {new LiteratureRoute(parsedFile, opCount)};
            }
        }

        public class LiteratureRoute : ProblemDefinition.IRoute
        {
            public double RoutingPenalty => 0;
            public IEnumerable<ProblemDefinition.IOperation> Operations { get; private set; }
            
            public LiteratureRoute(LiteratureOperation[] ops) => Operations = ops;

            internal LiteratureRoute(IEnumerator<int> parsedFile, int opCount)
            {
                var array = new IOperation[opCount + 1];

                for (int i = 0; i < opCount; i++) array[i] = new LiteratureOperation(parsedFile);
                
                array[opCount] = new LiteratureOperation(-1, 0, delayWeight: 1);
                
                Operations = array;
            }
        }

        public class LiteratureOperation : ProblemDefinition.IOperation
        {
            public IEnumerable<IMachine> Machines { get; private set; }
            public IEnumerable<IConnection> OutgoingConnections => new IConnection[0];
            public IEnumerable<IConnection> IncomingConnections => new IConnection[0];
            public TimeSpan Runtime { get; private set; }
            public double DelayWeight { get; private set; }
            public TimeSpan EarliestEntry => TimeSpan.Zero;
            public TimeSpan LatestEntry => DelayWeight > 0 ? TimeSpan.Zero : TimeSpan.MaxValue;

            public LiteratureOperation(int machine, int runtime, int delayWeight = 0)
            {
                Machines = machine == -1 ? new IMachine[0] : new[] {new LiteratureMachine(machine)};
                Runtime = TimeSpan.FromMinutes(runtime);
                DelayWeight = delayWeight;
            }

            public LiteratureOperation (IEnumerator<int> parsedFile)
            {
                Machines = new[] {new LiteratureMachine(parsedFile)};
                if (!parsedFile.MoveNext()) throw new Exception("Not enough values to read, missing runtime.");
                Runtime = TimeSpan.FromMinutes(parsedFile.Current);
            }
        }

        public class LiteratureMachine : ProblemDefinition.IMachine
        {
            private readonly int _id;

            internal LiteratureMachine(IEnumerator<int> parsedFile)
            {
                if (!parsedFile.MoveNext()) throw new Exception("Not enough values to read, missing machine id.");
                _id = parsedFile.Current;
            }

            public LiteratureMachine(int id) => _id = id;

            public bool Equals(IMachine other) => other is LiteratureMachine lm && lm._id == _id;

            public override int GetHashCode() => _id;

            public TimeSpan ReleaseTime => TimeSpan.Zero;
        }
    }
}