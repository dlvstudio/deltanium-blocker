using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Deltanium.Blocker.Core;

namespace Deltanium.Blocker.Services
{
    public class NodeHost
    {
        private readonly NodeConfig _config;
        private readonly TransactionPool _pool;
        private readonly BlockBuilder _builder;
        private readonly BlockValidator _validator;
        private readonly CentralCoordinatorClient _coordinator;
        private readonly NodeState _state;
        private readonly CancellationTokenSource _cts = new();
        private readonly ILogger<NodeHost> _logger;

        public NodeHost(
            NodeConfig config,
            TransactionPool pool,
            BlockBuilder builder,
            BlockValidator validator,
            CentralCoordinatorClient coordinator,
            NodeState state,
            ILogger<NodeHost> logger)
        {
            _config = config;
            _pool = pool;
            _builder = builder;
            _validator = validator;
            _coordinator = coordinator;
            _state = state;
            _logger = logger;
        }

        public async Task RunAsync()
        {
            Console.CancelKeyPress += (_, e) =>
            {
                e.Cancel = true;
                _cts.Cancel();
            };

            // Seed with a dummy genesis block
            if (_state.Height < 0)
            {
                _state.AddBlock(new Block
                {
                    Id = "genesis",
                    Height = 0,
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    CreatorPublicKey = "genesis",
                    MerkleRoot = string.Empty,
                    CentralApiPublicKey = string.Empty,
                    Version = "1.0.0"
                });
            }

            _logger.LogInformation("Node started. Press Ctrl+C to stop.");

            var blockLoop = RunBlockCreationLoop(_cts.Token);
            await blockLoop;
        }

        private async Task RunBlockCreationLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_pool.Count == 0)
                    {
                        await Task.Delay(_config.BlockCreationInterval, token);
                        continue;
                    }

                    var auth = await _coordinator.RequestBlockSlotAsync();
                    if (auth != null)
                    {
                        var block = _builder.BuildBlock(auth);
                        _logger.LogInformation($"[BLOCK] Created block {block.Id} height={block.Height} txs={block.Transactions.Count}");

                        // Validate locally before append
                        if (_validator.Validate(block, _state, out var reason))
                        {
                            _state.AddBlock(block);
                            _pool.Remove(block.Transactions.Select(t => t.Id));
                            _logger.LogInformation($"[BLOCK] Accepted block {block.Id}");
                        }
                        else
                        {
                            _logger.LogWarning($"[BLOCK] Rejected self-built block {block.Id}: {reason}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ERROR] Block loop: {ErrorMessage}", ex.Message);
                }

                await Task.Delay(_config.BlockCreationInterval, token);
            }
        }

        
    }
}












