using System.Collections.Generic;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Data
{
    public interface IBlockStore
    {
        List<Block> LoadAll();
        void SaveAll(IReadOnlyList<Block> blocks);
    }

    public interface IMempoolStore
    {
        List<Transaction> LoadAll();
        void SaveAll(IReadOnlyCollection<Transaction> txs);
    }
}
