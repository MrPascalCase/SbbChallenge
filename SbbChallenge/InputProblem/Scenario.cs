using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Web.Script.Serialization;
using sbbChallange.ProblemDefinition;

namespace sbbChallange.IntegrityChecks
{
    public static partial class Asserts
    {
        public static readonly Switch SbbParserChecks = new Switch(nameof(SbbProblem) + "." + nameof(SbbParserChecks));
    }
}

namespace sbbChallange.SbbProblem
{
    // instantiated by json deserialization, name kept consistent with resp. json files
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable UnassignedField.Global
    // ReSharper disable InconsistentNaming
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable CollectionNeverUpdated.Global
    public class Scenario : ProblemDefinition.IProblem
    {
        public string label;
        public int hash;
        public List<ServiceIntention> service_intentions;
        public List<Route> routes;
        public List<Resource> resources;

        [ScriptIgnore] private string _problemName;
        
        [ScriptIgnore] public IEnumerable<ProblemDefinition.IJob> Jobs { get; private set; }
        [ScriptIgnore] public ObjectiveType Objective => ObjectiveType.SumWeightedTardiness;
        [ScriptIgnore] string IProblem.ProblemName => _problemName;
        
        internal void Setup()
        {
            var RouteDictionary = routes.ToDictionary(r => r.id);
            var ResourceDictionary = resources.ToDictionary(r => r.id);

            List<Connection> ontoNeedsBeSet = new List<Connection>();

            foreach (var intention in service_intentions)
            {
                intention.Setup(RouteDictionary, ResourceDictionary, ontoNeedsBeSet);
            }


            foreach (Connection connection in ontoNeedsBeSet)
            {
                var id = connection.onto_service_intention;
                connection.Onto = service_intentions.First(i => i.id == id);

                foreach (NewRoute route in connection.Onto.SbbRoutes)
                {
                    RouteSection section = route.Sections
                        .FirstOrDefault(s => s.section_marker?.Contains(connection.onto_section_marker) ?? false);

                    if (section != null)
                    {
                        section.SbbIncomingConnections.Add(connection);
                    }
                    else
                    {    
                        Console.WriteLine($"Warning: connectionId = {connection.id}, routeId = {route.ToString()}, no section with onto marker {connection.onto_section_marker} found.");
                    }
                }
            }
            
            Jobs = service_intentions;
        }
        
        public static Scenario ReadFromFile(string path)
        {
            JavaScriptSerializer jsonSerializer = new JavaScriptSerializer();
            jsonSerializer.MaxJsonLength = Int32.MaxValue / 4;

            Console.WriteLine("Json parsing...");
            var sw = Stopwatch.StartNew();
            var scenario = jsonSerializer.Deserialize<Scenario>(System.IO.File.ReadAllText(path));
            Console.WriteLine($"Json parsing done in {Math.Round(sw.ElapsedMilliseconds / 1000f, 2)} s.");

            scenario._problemName = scenario.label;

            return scenario;
        }
    }
}