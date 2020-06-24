using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace SbbChallenge.Helpers
{
    public static class AdditionalLinq
    {
        [Pure]
        public static IEnumerable<(T, int)> Numbered<T>(this IEnumerable<T> enumerable)
        {
            int i = 0;
            foreach (T item in enumerable)
            {
                yield return (item, i);
                i++;
            }
        }

        [Pure]
        public static IEnumerable<T0> Select<T0, T1, T2>(this IEnumerable<(T1, T2)> enumerable, Func<T1, T2, T0> func)
        {
            foreach (var (t1, t2) in enumerable) yield return func(t1, t2);
        }

        [Pure]
        public static int IndexOf<T>(this IEnumerable<T> enumerable, Func<T, bool> predicate)
        {
            var tuple = enumerable.Numbered().FirstOrDefault(t => predicate(t.Item1));
            if (tuple.Equals(default)) return -1;
            return tuple.Item2;
        }

        [Pure]
        public static T ArgMax<T>(this IEnumerable<T> enumerable, Func<T, IComparable> comparer)
        {
            T maxElem;
            using (var iter = enumerable.GetEnumerator())
            {
                iter.MoveNext();
            
                maxElem = iter.Current;
                IComparable max = comparer(iter.Current);

                while (iter.MoveNext())
                    if (comparer(iter.Current).CompareTo(max) > 0)
                    {
                        maxElem = iter.Current;
                        max = comparer(iter.Current);
                    }
            
                iter.Dispose();
            }

            return maxElem;
        }


        [Pure]
        public static string JoinToString<T>(this IEnumerable<T> enumerable, string sep) =>
            enumerable == null ? "" : string.Join(sep, enumerable.Select(x => x.ToString()));


        [Pure]
        public static IEnumerable<(T, T)> Pairwise<T>(this IEnumerable<T> enumerable)
        {
            var e = enumerable.GetEnumerator();
            if (e.MoveNext())
            {
                T last = e.Current;
                while (e.MoveNext())
                {
                    yield return (last, e.Current);
                    last = e.Current;
                }
            }

            e.Dispose();
        }


        [Pure]
        public static IEnumerable<T1> Pairwise<T0, T1>(this IEnumerable<T0> enumerable, Func<T0, T0, T1> func) => 
            enumerable.Pairwise().Select(tuple => func(tuple.Item1, tuple.Item2));


        //public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source) => 
        //    source.Shuffle(new Random());

        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random rng)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (rng == null) throw new ArgumentNullException(nameof(rng));

            return source.ShuffleIterator(rng);
        }

        private static IEnumerable<T> ShuffleIterator<T>(
            this IEnumerable<T> source, Random rng)
        {
            var buffer = source.ToList();
            for (int i = 0; i < buffer.Count; i++)
            {
                int j = rng.Next(i, buffer.Count);
                yield return buffer[j];

                buffer[j] = buffer[i];
            }
        }
    }
}
