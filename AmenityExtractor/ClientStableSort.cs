using System;
using System.Collections.Generic;

namespace AmenityExtractor
{
    /// <summary>
    /// Re-implementation of the STLport stable_sort (temporary buffer path) used by the game client to order map objects (client function at 0x008c9a10).
    /// The client's comparator is not a strict weak ordering (near-equal coordinates are treated as ties), so the result depends on the exact
    /// algorithm: insertion sort of chunks of 7, merge passes between the list and a buffer, then a final merge of the two halves.
    /// See the reverse-engineering notes on the map-object sort comparator; the port was verified against client GUID dumps.
    /// </summary>
    public static class ClientStableSort
    {
        private const int ChunkSize = 7;

        public static List<T> Sort<T>(IList<T> input, Func<T, T, bool> less)
        {
            var v = new List<T>(input);
            var n = v.Count;
            if (n == 0) return v;

            var mid = (n + 1) / 2;
            MergeSortWithBuffer(v, 0, mid, less);
            MergeSortWithBuffer(v, mid, n, less);

            var first = v.GetRange(0, mid);
            var second = v.GetRange(mid, n - mid);
            return first.Count <= second.Count ? Merge(first, second, less) : MergeBackward(first, second, less);
        }

        private static void MergeSortWithBuffer<T>(List<T> v, int lo, int hi, Func<T, T, bool> less)
        {
            var seg = v.GetRange(lo, hi - lo);
            var len = seg.Count;
            ChunkInsertionSort(seg, ChunkSize, less);
            var step = ChunkSize;
            while (step < len)
            {
                var buf = MergeSortLoop(seg, step, less);
                step *= 2;
                seg = MergeSortLoop(buf, step, less);
                step *= 2;
            }
            for (var i = 0; i < seg.Count; i++) v[lo + i] = seg[i];
        }

        private static void ChunkInsertionSort<T>(List<T> v, int chunk, Func<T, T, bool> less)
        {
            var lo = 0;
            while (v.Count - lo >= chunk)
            {
                InsertionSort(v, lo, lo + chunk, less);
                lo += chunk;
            }
            InsertionSort(v, lo, v.Count, less);
        }

        private static void InsertionSort<T>(List<T> v, int lo, int hi, Func<T, T, bool> less)
        {
            for (var i = lo + 1; i < hi; i++)
            {
                var val = v[i];
                if (less(val, v[lo]))
                {
                    for (var k = i; k > lo; k--) v[k] = v[k - 1];
                    v[lo] = val;
                }
                else
                {
                    var j = i;
                    while (less(val, v[j - 1]))
                    {
                        v[j] = v[j - 1];
                        j--;
                    }
                    v[j] = val;
                }
            }
        }

        private static List<T> MergeSortLoop<T>(List<T> src, int step, Func<T, T, bool> less)
        {
            var n = src.Count;
            var result = new List<T>(n);
            var i = 0;
            var twoStep = 2 * step;
            while (n - i >= twoStep)
            {
                result.AddRange(Merge(src.GetRange(i, step), src.GetRange(i + step, step), less));
                i += twoStep;
            }
            var s = Math.Min(n - i, step);
            result.AddRange(Merge(src.GetRange(i, s), src.GetRange(i + s, n - i - s), less));
            return result;
        }

        private static List<T> Merge<T>(List<T> a1, List<T> a2, Func<T, T, bool> less)
        {
            var result = new List<T>(a1.Count + a2.Count);
            int i = 0, j = 0;
            while (i < a1.Count && j < a2.Count)
            {
                if (less(a2[j], a1[i])) result.Add(a2[j++]);
                else result.Add(a1[i++]);
            }
            while (i < a1.Count) result.Add(a1[i++]);
            while (j < a2.Count) result.Add(a2[j++]);
            return result;
        }

        private static List<T> MergeBackward<T>(List<T> a1, List<T> a2, Func<T, T, bool> less)
        {
            var result = new T[a1.Count + a2.Count];
            int i = a1.Count - 1, j = a2.Count - 1, k = result.Length - 1;
            while (i >= 0 && j >= 0)
            {
                if (less(a2[j], a1[i])) result[k--] = a1[i--];
                else result[k--] = a2[j--];
            }
            while (i >= 0) result[k--] = a1[i--];
            while (j >= 0) result[k--] = a2[j--];
            return new List<T>(result);
        }
    }
}
