using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using SbbChallenge.Helpers;
using sbbChallange.IntegrityChecks;

namespace sbbChallange.SbbProblem
{
    // instantiated by json deserialization, name kept consistent with resp. json files
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable UnassignedField.Global
    // ReSharper disable InconsistentNaming
    // ReSharper disable MemberCanBePrivate.Global
    // ReSharper disable once CollectionNeverUpdated.Global
    public class Route
    {
        public string id;
        public List<RoutePath> route_paths;
    }
    
    
    // Instantiated by json deserialization, name kept consistent with resp. json files
    // ReSharper disable ClassNeverInstantiated.Global
    // ReSharper disable UnassignedField.Global
    // ReSharper disable InconsistentNaming
    // ReSharper disable MemberCanBePrivate.Global
    public class RoutePath
    {
        // -------------------------------------------------------------------------------------------------------------
        // Instantiated by JSON deserialization:
        // -------------------------------------------------------------------------------------------------------------
        public string id;
        public List<RouteSection> route_sections;

        // -------------------------------------------------------------------------------------------------------------
        // Implementation
        // -------------------------------------------------------------------------------------------------------------
        internal IEnumerable<string> StartMarkers
        {
            get => route_sections.First().route_alternative_marker_at_entry ?? new List<string>();
            set => route_sections[0].route_alternative_marker_at_entry = value.ToList();
        }

        internal IEnumerable<string> EndMarkers
        {
            get => route_sections.Last().route_alternative_marker_at_exit ?? new List<string>();
            set => route_sections[route_sections.Count - 1].route_alternative_marker_at_exit = value.ToList();
        }

        /// <summary>
        /// Returns true it the RoutePath has a single entry and a single exit marker at the start (resp. end).
        /// (There can be RoutePaths that have no entry, no exit markers if they are at the very start (or end) of the
        /// complete Route.
        /// </summary>
        internal bool IsNormalized()
        {
            // single start marker (there might be sections - start of route) that have no start marker at all.
            if (StartMarkers.Count() >= 2) return false;

            // single end marker (there might be sections - end of route) that have no end marker at all.
            if (EndMarkers.Count() >= 2) return false;

            // if the route is longer than just one section, the start (resp. end) section should not have
            // end (resp. start) markers.
            if (route_sections.Count > 1)
            {
                if (route_sections[0].route_alternative_marker_at_exit != null
                    && route_sections[0].route_alternative_marker_at_exit.Count > 1)
                    return false;

                if (route_sections.Last().route_alternative_marker_at_entry != null
                    && route_sections.Last().route_alternative_marker_at_entry.Count > 1)
                    return false;
            }
            
            // all sections within the path should have no markers at all.
            return route_sections
                .Skip(1)
                .Take(route_sections.Count - 2)
                .All(sec =>
                    (sec.route_alternative_marker_at_entry == null ||
                     sec.route_alternative_marker_at_entry.Count == 0)
                    && (sec.route_alternative_marker_at_exit == null ||
                        sec.route_alternative_marker_at_exit.Count == 0));

        }

        /// <summary>
        /// Splits a single RoutePath into multiple ones, such that each resulting RoutePath fragment has a single
        /// entry marker at the start and a single exit marker at the end. Creating all Routing possibilities is then
        /// easier, as the structure is more rigorously defined. 
        /// </summary>
        internal void Normalize(ref List<RoutePath> result)
        {
            route_sections = route_sections.OrderBy(s => s.sequence_number).ToList();
            
            if (IsNormalized())
            {
                result.Add(this);
                return;
            }
            
            RouteSection SectionCopy(RouteSection section)
            {
                RouteSection copy = new RouteSection();
                copy.minimum_running_time = section.minimum_running_time;
                copy.penalty = section.penalty;
                copy.sequence_number = section.sequence_number;
                copy.resource_occupations = section.resource_occupations;
                copy.section_marker = section.section_marker;

                // Deep copy needed here:
                copy.route_alternative_marker_at_entry = section.route_alternative_marker_at_entry?.ToList() ?? new List<string>();
                copy.route_alternative_marker_at_exit = section.route_alternative_marker_at_exit?.ToList() ?? new List<string>();
                return copy;
            }

            RoutePath PathCopy(RoutePath path)
            {
                var copy = new RoutePath();
                copy.id = path.id;
                copy.route_sections = new List<RouteSection>();
                foreach (RouteSection section in path.route_sections)
                {
                    copy.route_sections.Add(SectionCopy(section));
                }

                return copy;
            }

            
            if (StartMarkers.Count() > 1)
            {
                var copy = PathCopy(this);
                copy.StartMarkers = StartMarkers.Take(1);
                copy.Normalize(ref result);

                StartMarkers = StartMarkers.Skip(1);
                Normalize(ref result);
                return;
            }

            if (EndMarkers.Count() > 1)
            {
                var copy = PathCopy(this);
                copy.EndMarkers = EndMarkers.Take(1);
                copy.Normalize(ref result);

                EndMarkers = EndMarkers.Skip(1);
                Normalize(ref result);
                return;
            }


            for (int i = 0; i < route_sections.Count - 1; i++)
            {
                var endMarkers = route_sections[i].route_alternative_marker_at_exit;
                if (endMarkers != null && endMarkers.Count > 0)
                {
                    var marker = endMarkers.First();
                    Asserts.Assert(Asserts.SbbParserChecks, route_sections[i + 1].route_alternative_marker_at_entry.Contains(marker));

                    var copy = PathCopy(this);
                    copy.route_sections = route_sections.Take(i+1).ToList();
                    copy.Normalize(ref result);

                    route_sections = route_sections.Skip(i+1).ToList();
                    Normalize(ref result);
                    return;
                }
            }
            
            throw new Exception("Normalizing a route path failed.");
        }

        internal static List<RouteSection[]> CreateStandardRoute(IEnumerable<RoutePath> paths)
        {
            List<RoutePath> normalized = new List<RoutePath>();
            
            foreach (RoutePath path in paths) path.Normalize(ref normalized);
            
            // We initialize a map: marker -> possible paths that follow this marker.
            var continuationsDict = new Dictionary<string, List<RoutePath>>();
            foreach (RoutePath path in normalized)
                if (path.StartMarkers.Any())
                {
                    var startMarker = path.StartMarkers.First(); // exactly one element, as normalized (see above)

                    if (continuationsDict.TryGetValue(startMarker, out var list)) list.Add(path);

                    else continuationsDict.Add(startMarker, new List<RoutePath> {path});
                }

            // We need to find starting paths, ie. paths without start markers, or paths whose
            // start markers do not have corresponding end markers
            var allEndMarkers = normalized.SelectMany(p => p.EndMarkers).ToHashSet();
            var startPaths = new List<RoutePath>();

            foreach (RoutePath path in normalized)
            {
                if (!path.StartMarkers.Any())
                {
                    startPaths.Add(path);
                    continue;
                }

                var startMarker = path.StartMarkers.First();
                if (!allEndMarkers.Contains(startMarker))
                {
                    startPaths.Add(path);
                }
            }
            Asserts.Assert(Asserts.SbbParserChecks, startPaths.Count > 0);

            // Now, recursively create all routing possibilities:
            var result = new List<ImmutableStack<RoutePath>>();

            foreach (RoutePath startPath in startPaths)
            {
                var stack = ImmutableStack<RoutePath>.Empty.Push(startPath);
                Do(stack, ref result, continuationsDict);
            }
            
            // finally, reverse and flatten
            var postProcessed = new List<RouteSection[]>();
            foreach (var stack in result)
            {
                postProcessed.Add(stack.Reverse().SelectMany(p => p.route_sections).ToArray());
            }
            return postProcessed;
        }

        private static void Do(
            ImmutableStack<RoutePath> path,
            ref List<ImmutableStack<RoutePath>> result,
            Dictionary<string, List<RoutePath>> continuations)
        {
            // if this is not the end (we have a marker and there are follow up paths), add a path
            // and recurse.
            if (path.Peek().EndMarkers.Any() 
                && continuations.TryGetValue(path.Peek().EndMarkers.First(), out var list))
            {
                foreach (RoutePath routePath in list)
                    Do(path.Push(routePath), ref result, continuations);
                
            }
            
            else result.Add(path);
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("RoutePath {");
            for (int i = 0; i < route_sections.Count; i++)
            {
                builder.Append($"\t{i} ");
                var entry = route_sections[i].route_alternative_marker_at_entry ?? new List<string>();
                if (entry.Any())
                {
                    builder.Append($"Entry={{{string.Join(", ", entry)}}}");
                }
                var exit = route_sections[i].route_alternative_marker_at_exit ?? new List<string>();
                if (exit.Any())
                {
                    builder.Append($"Exit={{{string.Join(", ", exit)}}}");
                }
                builder.AppendLine();
            }

            builder.AppendLine("}");
            return builder.ToString();    
        }
    }
}