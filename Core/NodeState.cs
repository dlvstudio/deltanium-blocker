using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Deltanium.Blocker.Core
{
    public class NodeState
    {
        private readonly List<Block> _chain = new();
        private readonly string _blocksFilePath;
        private readonly object _lock = new();

        public string LatestBlockId => _chain.Count == 0 ? string.Empty : _chain[^1].Id;
        public long Height => _chain.Count == 0 ? -1 : _chain[^1].Height;

        public IReadOnlyList<Block> Chain => _chain.AsReadOnly();

        public NodeState(string? blocksFilePath = null)
        {
            _blocksFilePath = blocksFilePath ?? "blocks.json";
            LoadBlocks();
        }

        public void AddBlock(Block block)
        {
            lock (_lock)
            {
                _chain.Add(block);
                SaveBlocks();
            }
        }

        private void LoadBlocks()
        {
            try
            {
                if (!File.Exists(_blocksFilePath))
                {
                    File.WriteAllText(_blocksFilePath, "[]");
                    return;
                }

                var json = File.ReadAllText(_blocksFilePath);
                var blocks = JsonSerializer.Deserialize<List<Block>>(json);
                if (blocks != null && blocks.Count > 0)
                {
                    _chain.Clear();
                    _chain.AddRange(blocks);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error loading blocks.json: {ex.Message}");
            }
        }

        private void SaveBlocks()
        {
            try
            {
                var json = JsonSerializer.Serialize(_chain, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_blocksFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error saving blocks.json: {ex.Message}");
            }
        }
    }
}












