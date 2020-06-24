using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SbbChallenge.Helpers
{
    public class BucketPriorityQueue : IEnumerable<Arc>
    {
        /*   BucketPriorityQueue implements a priority queue. (see https://en.wikipedia.org/wiki/Bucket_queue)
         *   The elements inserted are Arcs. 
         * 
         *   This data structure used in the most performance critical loop, that is in the left/right closure. 
         *     For description on the usage of this in the the left/right closure see the
         *     master thesis paragraph 3.5 Closure: All at once
         *     ================================================
         *                  
         * 
         *   Normally a heap/rb-tree or similar would be used. However, we know the maximum and minimum priorities
         *     involved and take advantage of this. The minimum priority is always 0, the maximum priority is given to
         *     the constructor.
         *
         *   Essentially, we store an array (index == priority) of stacks.
         * 
         *    _nodes =
         *        [0] --> { arc } --> { arc }     (lowest priority)
         *        [1] 
         *        [2] --> { arc }   
         *        [3] 
         *        [4] --> { arc }                 <-- lastPosition (highest priority elements present)
         *        ...
         *        [n]                             (highest priority)
         */

        private class Node : IEnumerable<Arc>
        {
            public Node Next;
            public Arc Arc;
            
            public IEnumerator<Arc> GetEnumerator()
            {
                Node n = this;
                for (; n != null; n = n.Next) yield return n.Arc;
            }

            IEnumerator IEnumerable.GetEnumerator() 
            {
                return GetEnumerator();
            }
        }

        private readonly Node[] _nodes;
        private int _lastPosition = -1;
            
        public BucketPriorityQueue(int maxPriority)
        {
            _nodes = new Node[maxPriority];
        }

        public bool IsEmpty => _lastPosition == -1;

        public void Insert(Arc arc, int priority)
        {
            _nodes[priority] = new Node
            {
                Arc =  arc, Next = _nodes[priority]
            };

            if (priority > _lastPosition) _lastPosition = priority;
        }

        public Arc Pop()
        {
            var res = _nodes[_lastPosition].Arc;
            _nodes[_lastPosition] = _nodes[_lastPosition].Next;

            while (_lastPosition > -1 && _nodes[_lastPosition] == null) _lastPosition--;
            
            return res;
        }

        public override string ToString()
        {
            StringBuilder builder = new StringBuilder();
            
            for (int i = 0; i < _nodes.Length; i++)
                if (_nodes[i] != null) builder.AppendLine($"{i}: [{_nodes[i].JoinToString(", ")}]");
            
            return builder.ToString();
        }
        
        public IEnumerator<Arc> GetEnumerator() => _nodes.Where(n => n != null).SelectMany(n => n).GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}