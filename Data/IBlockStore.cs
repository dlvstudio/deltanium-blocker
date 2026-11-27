using System.Collections.Generic;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Data
{
    public interface IBlockStore
    {
        List<Block> LoadAll();
        void SaveAll(IReadOnlyList<Block> blocks);
        /// <summary>Persist one new block (the chain already includes it). Stores that can append override this.</summary>
        void Append(Block block, IReadOnlyList<Block> chain) => SaveAll(chain);
    }

    public interface IMempoolStore
    {
        List<Transaction> LoadAll();
        void SaveAll(IReadOnlyCollection<Transaction> txs);
    }
}
