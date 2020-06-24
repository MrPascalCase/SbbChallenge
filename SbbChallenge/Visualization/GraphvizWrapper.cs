using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace sbbChallange.GraphvizWrapper
{
    public static class Tests
    {
        public static void Test()
        {
            Graph g = new Graph();
            g.RankDirection = RankDirection.LeftRight;

            Node a = new Node()
            {
                Label = "A",
                Style = NodeStyle.Dashed | NodeStyle.Filled | NodeStyle.Bold,
                FillColor = Color.Lime,
                Shape = NodeShape.Box
            };

            Node b = new Node()
            {
                Label = "B",
                FontColor = Color.Blue,
                Shape = NodeShape.Circle,
            };

            var c = new Cluster()
            {
                Label = "A cluster",
            };

            c.AddNodes(b);

            g.AddNodes(a);
            g.AddClusters(c);

            var e = new Edge(a, b)
            {
                Label = "An edge",
                Style = EdgeStyle.Dashed | EdgeStyle.Bold,
                Color = Color.DarkRed
            };

            g.AddEdges(e);

            Console.WriteLine(g);
            g.Display();
        }
    }

    public class ColorScale
    {
        public static Color[] Palette(int count, double saturation = .5, double lightness = .7)
        {
            Color[] colors = new Color[count];
            for (int i = 0; i < count; i++)
            {
                double t = i * (1.0 / (count - 1));
                colors[i] = ColorFromHSL(t, saturation, lightness);
            }

            return colors;
        }


        [Pure]
        public static Color[] ShuffledPalette(int count)
        {
            int subCount = (count / 3) + 2;

            Color[] colors = Palette(subCount, .2, .75)
                .Concat(Palette(subCount, .45, .75))
                .Concat(Palette(subCount, .7, .75))
                .ToArray();

            Color[] output = new Color[count];
            int step = 1;
            for (int i = 0; i < count; i++)
            {
                output[i] = colors[(step * i) % count];
            }

            return output;
        }

        public static Color ColorFromHSL(double h, double s, double l)
        {
            double r = 0, g = 0, b = 0;
            if (l != 0)
            {
                if (s == 0)
                    r = g = b = l;
                else
                {
                    double temp2;
                    if (l < 0.5)
                        temp2 = l * (1.0 + s);
                    else
                        temp2 = l + s - (l * s);

                    double temp1 = 2.0 * l - temp2;

                    r = GetColorComponent(temp1, temp2, h + 1.0 / 3.0);
                    g = GetColorComponent(temp1, temp2, h);
                    b = GetColorComponent(temp1, temp2, h - 1.0 / 3.0);
                }
            }

            return Color.FromArgb((int) (255 * r), (int) (255 * g), (int) (255 * b));

        }

        private static double GetColorComponent(double temp1, double temp2, double temp3)
        {
            if (temp3 < 0.0)
                temp3 += 1.0;
            else if (temp3 > 1.0)
                temp3 -= 1.0;

            if (temp3 < 1.0 / 6.0)
                return temp1 + (temp2 - temp1) * 6.0 * temp3;
            else if (temp3 < 0.5)
                return temp2;
            else if (temp3 < 2.0 / 3.0)
                return temp1 + ((temp2 - temp1) * ((2.0 / 3.0) - temp3) * 6.0);
            else
                return temp1;
        }
    }


    public abstract class Style
    {
        private readonly Dictionary<string, string> _attributes = new Dictionary<string, string>();

        protected void Set(string attribute, Color color)
        {
            Set(
                attribute,
                "#"
                + Convert.ToString(color.R, 16).PadLeft(2, '0')
                + Convert.ToString(color.G, 16).PadLeft(2, '0')
                + Convert.ToString(color.B, 16).PadLeft(2, '0'));
        }

        protected void Set(string attribute, Enum value, bool takesMultipleValues = true)
        {
            Set(attribute, value.ToString().ToLower(), takesMultipleValues);
        }

        protected void Set(string attribute, string value, bool takesMultipleValues = true)
        {
            if (takesMultipleValues
                && _attributes.TryGetValue(attribute, out var str))
            {
                var parts = str.Split(',');
                if (!parts.Contains(value))
                    _attributes[attribute] = $"{str},{value}";
            }

            else _attributes[attribute] = value;
        }

        internal abstract void AppendToBuilder(StringBuilder builder, int indent = 1);

        private IEnumerable<string> Create()
        {
            return _attributes.Select(kvp => $"{kvp.Key}=\"{kvp.Value}\"");
        }

        protected void CreateClusterOrGraph(StringBuilder builder, int indent)
        {
            foreach (string s in Create())
            {
                builder.Append(new string('\t', indent)).Append(s).AppendLine(";");
            }
        }

        protected void CreateEdgeOrNode(StringBuilder builder)
        {
            builder.Append("[").Append(string.Join(", ", Create())).Append("]");
        }
    }

    [Flags]
    public enum NodeStyle
    {
        Solid = 1 << 0,
        Dashed = 1 << 1,
        Dotted = 1 << 2,
        Bold = 1 << 3,
        Rounded = 1 << 4,
        Diagonals = 1 << 5,
        Filled = 1 << 6,
        Striped = 1 << 7,
        Wedged = 1 << 8,
    }

    public enum NodeShape
    {
        Box,
        Polygon,
        Ellipse,
        Oval,
        Circle,
        Point,
        Egg,
        Triangle
    }

    public class Node : Style
    {
        private static int _idCounter = 0;
        internal readonly int Id = _idCounter++;

        internal override void AppendToBuilder(StringBuilder builder, int indent = 1)
        {
            builder.Append(new string('\t', indent)).Append(Id).Append(" ");
            base.CreateEdgeOrNode(builder);
            builder.AppendLine(";");
        }

        public string Label
        {
            set => Set("label", value);
        }

        public NodeShape Shape
        {
            set => Set("shape", value);
        }

        public Color FontColor
        {
            set => Set("fontcolor", value);
        }

        public Color FillColor
        {
            set => Set("fillcolor", value);
        }

        public Color Color
        {
            set => Set("color", value);
        }

        public NodeStyle Style
        {
            set => Set("style", value);
        }
    }

    public class Cluster : Style
    {
        private static int _idCounter = 0;
        private readonly int _id = _idCounter++;
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Edge> _edges = new List<Edge>();
        private List<Cluster> _subClusters = new List<Cluster>();

        public string Label
        {
            set => Set("label", value);
        }

        public void AddNodes(IEnumerable<Node> nodes) => _nodes.AddRange(nodes);
        public void AddNodes(params Node[] nodes) => AddNodes((IEnumerable<Node>) nodes);
        public void AddEdges(IEnumerable<Edge> edges) => _edges.AddRange(edges);
        public void AddEdges(params Edge[] edges) => AddEdges((IEnumerable<Edge>) edges);
        public void AddClusters(IEnumerable<Cluster> clusters) => _subClusters.AddRange(clusters);
        public void AddClusters(params Cluster[] clusters) => AddClusters((IEnumerable<Cluster>) clusters);

        internal override void AppendToBuilder(StringBuilder builder, int indent = 1)
        {
            builder.Append(new string('\t', indent)).Append($"subgraph cluster_{_id} {{").AppendLine();

            CreateClusterOrGraph(builder, indent + 1);

            foreach (var element in _subClusters.Concat<Style>(_nodes).Concat(_edges))
                element.AppendToBuilder(builder, indent + 1);

            builder.Append(new string('\t', indent)).AppendLine("}");
        }
    }

    [Flags]
    public enum EdgeStyle
    {
        Solid = 1 << 0,
        Dashed = 1 << 1,
        Dotted = 1 << 2,
        Bold = 1 << 3,
    }

    public class Edge : Style
    {
        public string Label
        {
            set => Set("label", value);
        }

        public EdgeStyle Style
        {
            set => Set("style", value);
        }

        public Color Color
        {
            set => Set("color", value);
        }

        private readonly Node _tail, _head;

        public Edge(Node tail, Node head)
        {
            _tail = tail;
            _head = head;
        }

        internal override void AppendToBuilder(StringBuilder builder, int indent = 1)
        {
            builder.Append(new string('\t', indent)).Append(_tail.Id).Append(" -> ").Append(_head.Id);
            builder.Append(" ");
            base.CreateEdgeOrNode(builder);
            builder.AppendLine(";");
        }
    }

    public enum RankDirection
    {
        LeftRight,
        TopBottom
    }

    public class Graph : Style
    {
        private readonly List<Node> _nodes = new List<Node>();
        private readonly List<Cluster> _clusters = new List<Cluster>();
        private readonly List<Edge> _edges = new List<Edge>();

        public RankDirection RankDirection
        {
            set => Set("rankdir", value == RankDirection.LeftRight ? "LR" : "TB");
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            AppendToBuilder(builder, 0);
            return builder.ToString();
        }

        public void AddNodes(IEnumerable<Node> nodes) => _nodes.AddRange(nodes);

        public void AddNodes(params Node[] nodes) => AddNodes((IEnumerable<Node>) nodes);

        public void AddEdges(IEnumerable<Edge> edges) => _edges.AddRange(edges);

        public void AddEdges(params Edge[] edges) => AddEdges((IEnumerable<Edge>) edges);

        public void AddClusters(IEnumerable<Cluster> clusters) => _clusters.AddRange(clusters);

        public void AddClusters(params Cluster[] clusters) => AddClusters((IEnumerable<Cluster>) clusters);

        internal override void AppendToBuilder(StringBuilder builder, int indent = 1)
        {
            builder.Append(new string('\t', indent)).AppendLine("digraph {");

            base.CreateClusterOrGraph(builder, indent + 1);

            foreach (var element in _clusters.Concat<Style>(_nodes).Concat(_edges))
                element.AppendToBuilder(builder, indent + 1);

            builder.Append(new string('\t', indent)).AppendLine("}");
        }

        public void SaveSvg(string path)
        {
            Process dotProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "dot",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    Arguments = "-Tsvg"
                }
            };

            dotProcess.Start();
            dotProcess.StandardInput.Write(this.ToString());
            dotProcess.StandardInput.Close();

            File.WriteAllText(
                path.EndsWith(".svg") ? path : path + ".svg",
                dotProcess.StandardOutput.ReadToEnd());
        }

        public void Display()
        {
            string filename = Path.GetTempFileName() + ".svg";
            SaveSvg(filename);

            Process chromium = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "chromium-browser",
                    Arguments = $"{filename}",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };

            chromium.Start();
            chromium.WaitForExit();
        }
    }
}