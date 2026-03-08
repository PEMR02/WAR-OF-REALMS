using System;
using System.Collections.Generic;

namespace Project.Core.DataStructures
{
    /// <summary>Cola de prioridad min-heap para A* (el menor valor de prioridad sale primero).</summary>
    public class PriorityQueue<T>
    {
        private readonly List<(T item, float priority)> _heap = new List<(T, float)>();

        public int Count => _heap.Count;

        public void Enqueue(T item, float priority)
        {
            _heap.Add((item, priority));
            HeapifyUp(_heap.Count - 1);
        }

        public T Dequeue()
        {
            if (_heap.Count == 0)
                throw new InvalidOperationException("PriorityQueue is empty");

            T result = _heap[0].item;

            if (_heap.Count > 1)
            {
                _heap[0] = _heap[_heap.Count - 1];
                _heap.RemoveAt(_heap.Count - 1);
                HeapifyDown(0);
            }
            else
            {
                _heap.Clear();
            }

            return result;
        }

        public void Clear() => _heap.Clear();

        private void HeapifyUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (_heap[index].priority >= _heap[parent].priority)
                    break;

                Swap(index, parent);
                index = parent;
            }
        }

        private void HeapifyDown(int index)
        {
            while (true)
            {
                int left = 2 * index + 1;
                int right = 2 * index + 2;
                int smallest = index;

                if (left < _heap.Count && _heap[left].priority < _heap[smallest].priority)
                    smallest = left;

                if (right < _heap.Count && _heap[right].priority < _heap[smallest].priority)
                    smallest = right;

                if (smallest == index)
                    break;

                Swap(index, smallest);
                index = smallest;
            }
        }

        private void Swap(int a, int b)
        {
            var temp = _heap[a];
            _heap[a] = _heap[b];
            _heap[b] = temp;
        }
    }
}
