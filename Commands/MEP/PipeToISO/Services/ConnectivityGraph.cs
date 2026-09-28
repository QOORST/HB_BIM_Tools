using System;
using System.Collections.Generic;
using System.Linq;

namespace YD_RevitTools.LicenseManager.Commands.MEP.PipeToISO.Services
{
    // 元件層級的無向圖；外部端點只列為邊界，不自動擴大算量範圍。
    public sealed class ConnectivityGraph
    {
        private readonly SortedDictionary<long, SortedSet<long>> neighbors;
        public ConnectivityGraph(IEnumerable<long> ids)
        {
            neighbors = new SortedDictionary<long, SortedSet<long>>();
            foreach (long id in ids.Distinct()) neighbors.Add(id, new SortedSet<long>());
        }
        public void Connect(long a, long b)
        {
            if (a == b || !neighbors.ContainsKey(a) || !neighbors.ContainsKey(b)) return;
            neighbors[a].Add(b);
            neighbors[b].Add(a);
        }
        public IEnumerable<long> Neighbors(long id) => neighbors[id];
        public Dictionary<long, int> Components()
        {
            var result = new Dictionary<long, int>();
            int group = 0;
            foreach (long root in neighbors.Keys)
            {
                if (result.ContainsKey(root)) continue;
                group++;
                var queue = new Queue<long>();
                queue.Enqueue(root);
                result.Add(root, group);
                while (queue.Count > 0)
                {
                    long current = queue.Dequeue();
                    foreach (long next in neighbors[current])
                        if (!result.ContainsKey(next)) { result.Add(next, group); queue.Enqueue(next); }
                }
            }
            return result;
        }
    }
}
