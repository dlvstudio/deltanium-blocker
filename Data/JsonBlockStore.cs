using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Data
{
    public class JsonBlockStore : IBlockStore
    {
        private readonly string _path;

        public JsonBlockStore(string? path = null)
        {
            _path = path ?? "blocks.json";
        }

        public List<Block> LoadAll()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path, "[]");
                    return new List<Block>();
                }

                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<Block>>(json) ?? new List<Block>();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error loading blocks.json: {ex.Message}");
                return new List<Block>();
            }
        }

        public void SaveAll(IReadOnlyList<Block> blocks)
        {
            try
            {
                var json = JsonSerializer.Serialize(blocks, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_path, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error saving blocks.json: {ex.Message}");
            }
        }
    }

    public class JsonMempoolStore : IMempoolStore
    {
        private readonly string _path;

        public JsonMempoolStore(string? path = null)
        {
            _path = path ?? "mempool.json";
        }

        public List<Transaction> LoadAll()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    File.WriteAllText(_path, "[]");
                    return new List<Transaction>();
                }

                var json = File.ReadAllText(_path);
                return JsonSerializer.Deserialize<List<Transaction>>(json) ?? new List<Transaction>();
            }
            catch
            {
                return new List<Transaction>();
            }
        }

        public void SaveAll(IReadOnlyCollection<Transaction> txs)
        {
            var json = JsonSerializer.Serialize(txs, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
    }
}
