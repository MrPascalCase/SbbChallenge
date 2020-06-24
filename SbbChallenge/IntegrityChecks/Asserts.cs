using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace sbbChallange.IntegrityChecks
{
    /// <summary>
    /// As we do not generate small/specific sample problem, but always work with the larger input problems supplied
    /// by Sbb, most methods cannot be effectively tested with unit tests. As a result, asserts became very important.
    /// Furthermore, some of the asserts I defined are extremely costly (see for example to GraphLayer.UpdateTimes
    /// loop invariant). All of that necessitates that asserts can be enabled/disabled and are organised in some
    /// fashion.
    ///
    /// Hence, I defined a method [Conditional(DEBUG)]Assert(), similar to System.Diagnostic.Debug.Assert(), which
    /// additionally takes an Asserts.Switch as input and is only active if DEBUG is set AND the respective switch is
    /// on.
    ///
    /// Every such switch is defined in the partial class (ie, the class is distributed over multiple files) Asserts.
    /// Every switch has a On/Off bool, a name, and a description (what does it check). The static method
    /// ShowActiveAsserts() prints a summary to the console.
    /// </summary>
    public static partial class Asserts
    {
        private static Dictionary<string, Switch> _switches;
        
        public class Switch
        {
            public string Name { get; }
            public bool On;
            public string[] Description;

            public Switch(string name, params string[] description)
            {
                Name = name;
                Description = description;

                if (_switches == null) _switches = new Dictionary<string, Switch>();
                
                _switches.Add(name, this);
            }

            public void Enable() => On = true;

            public void Disable() => On = false;
        }

        public static void ShowActiveAsserts()
        {
            ConsoleWrapper.HLine();
            ConsoleWrapper.WriteLine("ASSERT SWITCHES:");
            foreach (Switch s in _switches.Values) //Switches.Values)
            {
                ConsoleWrapper.Write("[ ");
                ConsoleWrapper.Write(s.On ? "ON " : "OFF", s.On? ConsoleColor.Green : ConsoleColor.Red);
                ConsoleWrapper.Write(" ] ");
                ConsoleWrapper.WriteLine(s.Name);

                foreach (string line in s.Description)
                {
                    ConsoleWrapper.WriteLine("        " + line, ConsoleColor.Gray);
                }
            }
            ConsoleWrapper.HLine();
        }

        [Conditional("DEBUG")]
        public static void Assert(Switch @switch, bool condition, string message = null)
        {
            if (@switch.On)
            {
                if (!condition) throw new Exception($"Assert failed{(message == null? "." : $": {message}.")}");
            }
        }
        
        private static class ConsoleWrapper
        {
            private static Stack<ConsoleColor> _savedFgs = new Stack<ConsoleColor>();
            private static void SetFg(ConsoleColor color)
            {
                _savedFgs.Push(Console.ForegroundColor);
                Console.ForegroundColor = color;
            }

            private static void RestoreFg()
            {
                Console.ForegroundColor = _savedFgs.Pop();
            }

            public static void HLine (ConsoleColor color = ConsoleColor.Black)
            {
            
                WriteLine(new string('_', Console.WindowWidth), color);
                WriteLine();
            }

            public static void Write(object toWrite, ConsoleColor color = ConsoleColor.Black)
            {
                if (color != Console.ForegroundColor)
                {
                    SetFg(color);
                    Console.Write(toWrite);
                    RestoreFg();
                } else 
                    Console.Write(toWrite);
            }

            public static void WriteLine(object toWrite, ConsoleColor color = ConsoleColor.Black)
                => Write (toWrite + "\n", color);

            public static void WriteLine() => Console.WriteLine();

        }
    }
}