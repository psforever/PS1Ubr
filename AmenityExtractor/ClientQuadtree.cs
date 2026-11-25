using System.Collections.Generic;

namespace AmenityExtractor
{
    /// <summary>
    /// Emulation of the client's map object quadtree (Quadtree_AddObject / QuadtreeNode_Insert / QuadtreeNode_TraverseRec, see docs map-load-flow.md).
    /// Fixed depth; an item is added to every leaf its 2D box overlaps (touching counts); leaves keep insertion order; the walk visits the children
    /// in the order (low x, low y), (high x, low y), (high x, high y), (low x, high y) and emits an item the first time it is seen.
    /// </summary>
    public class ClientQuadtree
    {
        private class Node
        {
            public float MinX, MinY, MaxX, MaxY;
            public Node[] Child = new Node[4];
            public List<int> Items;
        }

        private readonly Node _root;
        private readonly int _depth;

        public ClientQuadtree(float minX, float minY, float maxX, float maxY, int depth)
        {
            _root = new Node { MinX = minX, MinY = minY, MaxX = maxX, MaxY = maxY };
            _depth = depth;
        }

        public void Add(int item, float minX, float minY, float maxX, float maxY) => Insert(_root, item, minX, minY, maxX, maxY, 0);

        private void Insert(Node n, int item, float bx0, float by0, float bx1, float by1, int depth)
        {
            if (by1 < n.MinY || bx1 < n.MinX || n.MaxY < by0 || n.MaxX < bx0) return;
            if (depth == _depth)
            {
                if (n.Items == null) n.Items = new List<int>();
                n.Items.Add(item);
                return;
            }

            var midX = (n.MaxX - n.MinX) * 0.5f + n.MinX;
            var midY = (n.MaxY - n.MinY) * 0.5f + n.MinY;

            // child 0: low x, low y
            if (by0 <= midY && n.MinY <= by1 && bx0 <= midX && n.MinX <= bx1) Insert(Child(n, 0, n.MinX, n.MinY, midX, midY), item, bx0, by0, bx1, by1, depth + 1);
            // child 1: high x, low y
            if (by0 <= midY && n.MinY <= by1 && bx0 <= n.MaxX && midX <= bx1) Insert(Child(n, 1, midX, n.MinY, n.MaxX, midY), item, bx0, by0, bx1, by1, depth + 1);
            // child 2: high x, high y
            if (!(n.MaxY < by0 || by1 < midY || n.MaxX < bx0 || bx1 < midX)) Insert(Child(n, 2, midX, midY, n.MaxX, n.MaxY), item, bx0, by0, bx1, by1, depth + 1);
            // child 3: low x, high y
            if (by0 <= n.MaxY && midY <= by1 && bx0 <= midX && n.MinX <= bx1) Insert(Child(n, 3, n.MinX, midY, midX, n.MaxY), item, bx0, by0, bx1, by1, depth + 1);
        }

        private static Node Child(Node n, int i, float x0, float y0, float x1, float y1)
        {
            if (n.Child[i] == null) n.Child[i] = new Node { MinX = x0, MinY = y0, MaxX = x1, MaxY = y1 };
            return n.Child[i];
        }

        /// <summary>Items in the order the client walk emits them (each once, at its first visit).</summary>
        public List<int> Walk()
        {
            var seen = new HashSet<int>();
            var result = new List<int>();
            Visit(_root, seen, result);
            return result;
        }

        private void Visit(Node n, HashSet<int> seen, List<int> result)
        {
            if (n.Items != null)
            {
                foreach (var i in n.Items) if (seen.Add(i)) result.Add(i);
                return;
            }
            foreach (var c in n.Child) if (c != null) Visit(c, seen, result);
        }
    }
}
