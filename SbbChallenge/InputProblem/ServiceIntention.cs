using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

namespace sbbChallange.SbbProblem
{

    /// <summary>
    /// Sbb route: one route for many 'routing options', which is train (ie requirements)
    /// agnositc.
    /// </summary>
    public class NewRoute : ProblemDefinition.IRoute
    {
        IEnumerable<ProblemDefinition.IOperation> ProblemDefinition.IRoute.Operations => Sections;
        double ProblemDefinition.IRoute.RoutingPenalty => RoutingPenalty;
        
        public List<RouteSection> Sections;
        public double RoutingPenalty;
        
        public NewRoute(
            ServiceIntention currentIntention,
            List<RouteSection> route,
            Dictionary<string, SectionRequirement> requirements,
            Dictionary<string, Resource> resources,
            List<Connection> ontoNeedsBeSet)
        {
            Sections = route;
            RoutingPenalty = Sections.Select(s => s.penalty ?? 0.0).Sum();

            SectionRequirement GetSectionRequirement(RouteSection section)
            {
                if (section.section_marker != null)
                    foreach (string s in section.section_marker)
                        if (requirements.TryGetValue(s, out var r))
                            return r;

                return null;
            }
            
            var dummyEndNode = new RouteSection();
            Sections.Add(dummyEndNode);

            SectionRequirement[] sectionRequirements = Sections.Select(GetSectionRequirement).ToArray();
            
            Sections[0].Setup(currentIntention, null, sectionRequirements[0], 
                resources, ontoNeedsBeSet,false);
            for (int i = 1; i < Sections.Count; i++)
            {
                Sections[i].Setup(currentIntention, sectionRequirements[i-1], sectionRequirements[i], 
                    resources, ontoNeedsBeSet,
                    i == Sections.Count - 2 
                    || i == Sections.Count - 1);
            }
            
        }
    }
    
    // instantiated by json deserialization, name kept consistent with resp. json files
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable UnassignedField.Global
    // ReSharper disable InconsistentNaming
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable CollectionNeverUpdated.Global
    
    public class ServiceIntention : ProblemDefinition.IJob
    {
        public string id;
        public string route; // Route id reference
        public List<SectionRequirement> section_requirements;
        
        //////////
        [ScriptIgnore] IEnumerable<ProblemDefinition.IRoute> ProblemDefinition.IJob.Routes => SbbRoutes;
        
        [ScriptIgnore] public List<NewRoute> SbbRoutes { get; private set; }
        
        internal void Setup(
            Dictionary<string, Route> idToRoute,
            Dictionary<string, Resource> idToResource,
            List<Connection> ontoNeedsBeSet)
        {
            Route route = idToRoute[this.route];

            var routingPossibilities = RoutePath.CreateStandardRoute(route.route_paths);

            var requirementDictionary = section_requirements.ToDictionary(r => r.section_marker);
            
            var routes = new List<NewRoute>();
            
            foreach (var possibility in routingPossibilities)
            {
                routes.Add(new NewRoute(this, possibility.ToList(), requirementDictionary, idToResource, ontoNeedsBeSet));
            }

            SbbRoutes = routes;
        }
        
        public override string ToString()
        {
            return $"ServiceIntention: {id}";
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((ServiceIntention) obj);
        }
        
        protected bool Equals(ServiceIntention other)
        {
            return string.Equals(id, other.id);
        }

        public override int GetHashCode()
        {
            // cannot make ids properly readonly as that interferes with deserialization
            // ReSharper disable NonReadonlyMemberInGetHashCode
            return (id != null ? id.GetHashCode() : 0);
        }
    }
}