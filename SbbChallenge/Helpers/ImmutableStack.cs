using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;

namespace SbbChallenge.Helpers
{
    /// <summary>
    /// Should be equivalent to System.Collection.Immutable.ImmutableStack, which is not included to reduce
    /// dependencies (also, the implementation is very simple) 
    /// </summary>
    public class ImmutableStack<T> : IEnumerable<T>
    {
        public static readonly ImmutableStack<T> Empty = new ImmutableStack<T>(default, null);

        private readonly T _head;
        private readonly ImmutableStack<T> _tail;
        
        private ImmutableStack(T head, ImmutableStack<T> tail)
        {
            _head = head;
            _tail = tail;
        }

        [Pure] public T Peek() => _head;

        [Pure] public ImmutableStack<T> Push(T element) => new ImmutableStack<T>(element, this);

        [Pure] public (ImmutableStack<T>, T) Pop() => (_tail, _head);

        public IEnumerator<T> GetEnumerator() => 
            (ReferenceEquals(this, Empty) ? new T[0] : _tail.Prepend(_head)).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal static class Test
    {
        public static void Test0()
        {
            var l = ImmutableList<int>.Empty;
            foreach (int i in Enumerable.Range(0, 10).Shuffle(new Random()))
            {
                l = l.Add(i);
            }
            
            foreach (int i in Enumerable.Range(0, 10).Shuffle(new Random()))
            {
                Console.WriteLine(l.JoinToString(", "));
                Console.WriteLine($"l contains {i} == {l.Contains(i)}.");
                l = l.Remove(i);
                Console.WriteLine($"removed {i}.");
                Console.WriteLine($"l contains {i} == {l.Contains(i)}.");
            }
        } 
    }
    
    /// <summary>
    /// Should be equivalent to System.Collection.Immutable.ImmutableList, which is not included to reduce
    /// dependencies (also, the implementation is very simple) 
    /// </summary>
    public class ImmutableList<T> : IEnumerable<T>
    {
        public static readonly ImmutableList<T> Empty = new ImmutableList<T>(ImmutableStack<T>.Empty, 0);
        
        private readonly ImmutableStack<T> _stack;
        private readonly int _count;
        
        private ImmutableList(ImmutableStack<T> stack, int count)
        {
            _stack = stack;
            _count = count;
        }

        [Pure] public int Count => _count;

        [Pure] public ImmutableList<T> Add (T element) => new ImmutableList<T>(_stack.Push(element), _count+1);

        [Pure] public ImmutableList<T> Remove(T element)
        {
            if (ReferenceEquals(_stack, ImmutableStack<T>.Empty)) throw new Exception();
            
            Stack<T> top = new Stack<T>();
            ImmutableStack<T> rest;
            T current;
            
            for ((rest, current) = _stack.Pop();
                !ReferenceEquals(rest, ImmutableStack<T>.Empty);
                (rest, current) = rest.Pop())
                
                if (current.Equals(element)) break;    
                else top.Push(current);

            while (top.Count > 0) rest = rest.Push(top.Pop());
            
            return new ImmutableList<T>(rest, _count-1);
        }
        
        public IEnumerator<T> GetEnumerator() => _stack.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}