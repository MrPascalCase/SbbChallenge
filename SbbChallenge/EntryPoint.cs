using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using sbbChallange.ProblemDefinition;
using sbbChallange.Layers;
using sbbChallange.SbbProblem;
using sbbChallange.Search;

using static sbbChallange.IntegrityChecks.Asserts;
using Route = sbbChallange.ProblemDefinition.Route;

namespace SbbChallenge
{
    internal static class EntryPoint
    {
        public static readonly string[] InputFiles =
            new []
                {
                    "sample_scenario",
                    "01_dummy",
                    "02_a_little_less_dummy",
                    "03_FWA_0.125",
                    "04_V1.02_FWA_without_obstruction",
                    "05_V1.02_FWA_with_obstruction",
                    "06_V1.20_FWA", // 29mb
                    "07_V1.22_FWA", // 38mb
                    "08_V1.30_FWA", // 16mb
                    "09_ZUE-ZG-CH_0600-1200", // 24mb
                }
                .Select(f => $"../../../../Thesis/Data/Input/{f}.json")
                .ToArray();

        private static readonly (string, string)[] InputOptionDescriptions =
        {
            ("-i | --input", "Specify the path to an input file. "),
            ("-s | --seed (Optional)", "Specify a seed to use. If no seed is specified, a random seed is used."),
            ("--maxIter (Optional)", "Specify a maximum iteration to preform"),
            ("--maxTime (Optional)", "Specify a maximum search time, eg 90s or 1h40m40s")
        };

        static void ShowUsage()
        {
            Console.WriteLine("Usage:");
            
            int leftLength = InputOptionDescriptions.Select(t => t.Item1.Length).Max() + 2;
            
            foreach (var (left, right) in InputOptionDescriptions)
                Console.WriteLine(left.PadRight(leftLength, ' ') + right);
        }
            
        static bool TryParseInput(
            string[] args, 
            out Random random, 
            out string inputFile, 
            out TimeSpan maxTime,
            out int maxIter)
        {
            random = new Random();
            inputFile = null;
            maxIter = Int32.MaxValue;
            maxTime = TimeSpan.MaxValue;
            
            for (int i = 0; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length)
                {
                    Console.WriteLine($"Failure: No argument specified for {args[i]}");
                    return false;
                }
                
                var option = args[i].ToLowerInvariant().Trim().TrimStart('-');
                var argument = args[i + 1];

                switch (option)
                {
                    case "i":
                    case "input":
                        if (File.Exists(argument))
                        {
                            inputFile = argument;
                        }
                        else
                        {
                            Console.WriteLine($"Failure: file {argument} not found.");
                            return false;
                        }
                        break;

                    case "s":
                    case "seed":
                    {
                        bool success = int.TryParse(argument, out var seed);
                        random = new Random(seed);
                        if (!success)
                        {
                            Console.WriteLine(
                                $"Failure: Unable to parse integer number for seed, input = {argument}");
                            return false;
                        }
                        break;
                    }

                    case "maxtime":
                        break;

                    case "maxIter":
                    {
                        bool success = int.TryParse(argument, out maxIter);

                        if (!success)
                        {
                            Console.WriteLine(
                                $"Failure: Unable to parse integer number for maxIter, input = {argument}");
                            return false;
                        }
                        break;
                    }

                    default:
                        Console.WriteLine($"Failure: Option {option} not recognised.");
                        return false;
                }
            }

            if (inputFile == null) Console.WriteLine("Failure: No input file specified.");

            return inputFile != null;
        }
        

        public static void Main(string[] args)
        {
            //if (!TryParseInput(args, out var rang, out var file, out var maxTime, out var maxIter))
            //{
            //   ShowUsage();
            //   return;
            //}
            
            Console.ForegroundColor = ConsoleColor.Black;
            //Console.BackgroundColor = ConsoleColor.White;
            #if DEBUG
            var checks = new Switch[]
            {
                //ProblemInitialization,
                //SolutionChecks,
                //SbbParserChecks,
                
                // Cost Layer:
                //CostLayerChecks,
                //CostLayerClassInvariant,
                
                // Graph Layer:
                //GraphChecks,
                //GraphLayerChecks,
                //GraphLayerClassInvariant,
                //GraphLayerLoopInvariant,
                //TimesGraphChecks,            
                
                // Sequencing Layer:
                //JobSequenceChecks,
                JobSequenceClassInvariant,
                //SequencingChecks,
                
                // Closure Layer:
                //ClosureCheck,
                AfterRemoveAcyclic,
                //BeforeRemovalAcyclic,
                //InvertPrecedenceCheck,
                
                // Routing Layer:
                //RoutingChecks,
                AfterRouteLoadAcyclic,
                
                // Job Shop Layer: 
                //JobShopChecks,
            };
            foreach (var check in checks) check.Enable();
            #endif
            
            ShowActiveAsserts();

            //LiteratureInstances.Start();
            //return;
            
            
            Scenario scenario = Scenario.ReadFromFile(InputFiles[5]);
            Console.Write($"Setting up scenario...");
            scenario.Setup();
            Console.WriteLine(" done.");
            
            IProblem iproblem = scenario;
            
            //Console.Write($"Restricting route choices...");
            //iproblem = iproblem.RestrictRouteChoices(1);
            //Console.WriteLine(" done.");
            
            Console.WriteLine("Removing redundant machines.");
            StringBuilder report = new StringBuilder();
            iproblem.RemoveRedundantMachines(report);
            Console.WriteLine(report.ToString());

            Console.WriteLine("Remove redundant operations.");
            report.Clear();
            iproblem = iproblem.RemoveRedundantOperations(report);
            Console.WriteLine(report);

            var problem = new Problem(iproblem, loadConnections: true);

            //File.WriteAllText("../../problemPrint.txt", problem.ToString());
            
            IJobShopLayer jobShop = new JobShopLayer(problem);

            IEnumerable<(Job, Route)> initialRouting = problem.Jobs
                .Select(t => (t, t.Routes.OrderBy(r => r.RoutingPenalty).First()))
                .OrderByDescending(tuple => tuple.Item2.First().EarliestEarliestEntry);
            
            jobShop.CreateInitialSolution(initialRouting);


            var searchReport = SingleThreadSearch.Run("run_0", jobShop, new Random(), 0, Int32.MaxValue);

        }
    }
}