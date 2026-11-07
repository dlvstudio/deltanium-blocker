using System.Collections.Generic;
using Deltanium.Blocker.Data;

namespace Deltanium.Blocker.Core
{
    public class NodeState
    {
        private readonly List<Block> _chain = new();
        private readonly IBlockStore _store;
        private readonly object _lock = new();

        public string LatestBlockId => _chain.Count == 0 ? string.Empty : _chain[^1].Id;
        public long Height => _chain.Count == 0 ? -1 : _chain[^1].Height;

        public IReadOnlyList<Block> Chain => _chain.AsReadOnly();

        public NodeState(IBlockStore? store = null)
        {
            _store = store ?? new JsonBlockStore();
            LoadBlocks();
        }

        public void AddBlock(Block block)
        {
            lock (_lock)
            {
                _chain.Add(block);
                _store.SaveAll(_chain);
            }
        }

        private void LoadBlocks()
        {
            var blocks = _store.LoadAll();
            if (blocks.Count == 0) return;
            _chain.Clear();
            _chain.AddRange(blocks);
        }
    }
}
