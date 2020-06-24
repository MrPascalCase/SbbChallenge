using System;
using System.Collections.Generic;

namespace SbbChallenge.Helpers
{
    /// <summary>
    /// Used to compare 'doubles' for equality with a given epsilon.
    /// </summary>
    public class FloatingPointComparer : IComparer<double>, IEqualityComparer<double>
    {
        public static readonly FloatingPointComparer Instance = new FloatingPointComparer(10e-5);

        private readonly double _epsilon;
            
        public FloatingPointComparer(double epsilon) => _epsilon = epsilon;
            
        public int Compare(double x, double y) =>  Math.Abs(x - y) < _epsilon ? 0 : x.CompareTo(y);
            
        public bool Equals(double x, double y) => Math.Abs(x - y) < _epsilon;

        public int GetHashCode(double obj)
        {
            throw new Exception("The epsilon floating point comparer is incompatible with hash codes.");
        }
    }
}