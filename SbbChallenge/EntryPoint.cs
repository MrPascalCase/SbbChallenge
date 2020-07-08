using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using sbbChallange;
using sbbChallange.ProblemDefinition;
using sbbChallange.Layers;
using sbbChallange.SbbProblem;
using sbbChallange.Search;
using SbbChallenge.Helpers;

using static sbbChallange.IntegrityChecks.Asserts;
using Route = sbbChallange.ProblemDefinition.Route;

namespace SbbChallenge
{
    internal static class EntryPoint
    {
        private static readonly (string, string)[] InputOptionDescriptions =
        {
            ("-s | --seed (Optional)", "Specify a seed to use. If no seed is specified, a random seed is used."),
            ("-i | --maxIter (Optional)", "Specify a maximum iteration to preform"),
            ("-t | --maxTime (Optional)", "Specify a maximum search time, in seconds"),
            ("-r | --restrictRouting (Optional)", "Specify a maximum number of routes to consider for each job.")
        };

        static void ShowUsage()
        {
            Console.WriteLine("Usage:");
            
            Console.WriteLine("As a first argument, specify the path to a input file.");
            Console.WriteLine("Supported are SBB .json files or academic instances (such as lawrence) .txt.");
            
            int leftLength = InputOptionDescriptions.Select(t => t.Item1.Length).Max() + 2;
            
            foreach (var (left, right) in InputOptionDescriptions)
                Console.WriteLine(left.PadRight(leftLength, ' ') + right);
        }
            
        static bool TryParseInput(
            string[] args, 
            out int seed, 
            out string inputFile, 
            out TimeSpan maxTime,
            out int maxIter, 
            out int restrictRouting)
        {
            seed = new Random().Next();
            inputFile = null;
            maxIter = Int32.MaxValue;
            maxTime = TimeSpan.MaxValue;
            restrictRouting = Int32.MaxValue;

            if (args.Length == 0)
            {
                Console.WriteLine("Failure: No input file specified.");
                return false;
            }
            
            if (File.Exists(args[0])) inputFile = args[0];

            else
            {
                Console.WriteLine($"Failure: file {args[0]} not found.");
                return false;
            }
            
            for (int i = 1; i < args.Length; i += 2)
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
                    case "s":
                    case "seed":
                    {
                        bool success = int.TryParse(argument, out seed);
                        Console.WriteLine($"Setting seed = {seed}.");
                        if (!success)
                        {
                            Console.WriteLine(
                                $"Failure: Unable to parse integer number for seed, input = {argument}");
                            return false;
                        }
                        break;
                    }

                    case "maxtime": 
                    case "t":
                    {
                        bool success = int.TryParse(argument, out var t);
                        maxTime = TimeSpan.FromSeconds(t);
                        Console.WriteLine($"Setting maxTime = {maxTime.Show()}.");

                        if (!success)
                        {
                            Console.WriteLine(
                                $"Failure: Unable to parse integer number for maxTime, input = {argument}");
                            return false;
                        }
                        break;
                    }

                    case "maxiter":
                    case "i":
                    {
                        bool success = int.TryParse(argument, out maxIter);
                        Console.WriteLine($"Setting maxIter = {maxIter}.");
                        if (!success)
                        {
                            Console.WriteLine(
                                $"Failure: Unable to parse integer number for maxIter, input = {argument}");
                            return false;
                        }
                        break;
                    }
                    
                    case "restrictRouting":
                    case "r":
                    {
                        bool success = int.TryParse(argument, out restrictRouting);
                        Console.WriteLine($"Setting restrictRouting = {restrictRouting}.");

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

            return true;
        }
        

        public static void Main(string[] args)
        {
            if (!TryParseInput(args, out var seed, out var file, out var maxTime, out var maxIter, out var restrictRouting))
            {
               ShowUsage();
               return;
            }
            Random rand = new Random(seed);
            
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

            IProblem iproblem;
            
            if (file.EndsWith(".txt"))
            {    
                iproblem = AcademicInstances.LiteratureProblem.ReadFormFile(file);
            }
            else if (file.EndsWith(".json"))
            {
                Scenario scenario = Scenario.ReadFromFile(file);

                Console.Write($"Setting up scenario...");
                scenario.Setup();
                Console.WriteLine(" done.");
            
                iproblem = scenario;
            }
            else
            {
                return;
            }

            if (restrictRouting < Int32.MaxValue)
            {
                Console.Write($"Restricting route choices...");
                iproblem = iproblem.RestrictRouteChoices(restrictRouting, rand);
                Console.WriteLine(" done.");
            }

            Console.WriteLine("Removing redundant machines.");
            StringBuilder report = new StringBuilder();
            iproblem.RemoveRedundantMachines(report);
            Console.WriteLine(report.ToString());

            Console.WriteLine("Remove redundant operations.");
            report.Clear();
            iproblem = iproblem.RemoveRedundantOperations(report);
            Console.WriteLine(report);

            var problem = new Problem(iproblem, loadConnections: true);

            IJobShopLayer jobShop = new JobShopLayer(problem);

            IEnumerable<(Job, Route)> initialRouting = problem.Jobs
                .Select(t => (t, t.Routes.OrderBy(r => r.RoutingPenalty).First()))
                .OrderByDescending(tuple => tuple.Item2.First().EarliestEarliestEntry);
            
            jobShop.CreateInitialSolution(initialRouting);

            var searchReport = SingleThreadSearch.Run($"run_{seed}", jobShop, rand, 0, maxIter, maxTime);

            
            // Data collected for the Objective value vs execution time plot (Thesis, Fig 7 b)
            /*
            if (File.Exists("../../output.csv"))
            {
                File.AppendAllLines("../../output.csv", searchReport.ToCsv().Split('\n').Skip(1));
            }
            else
            {
                File.WriteAllText("../../output.csv", searchReport.ToCsv());
            }
            Console.WriteLine(searchReport.ToCsv());
            */
        }
    }
}