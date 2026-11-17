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
        private readonly Ledger.BalanceCheckService? _balances;
        private DateTime _lastDeferredCheck = DateTime.MinValue;

        public NodeHost(
            NodeConfig config,
            TransactionPool pool,
            BlockBuilder builder,
            BlockValidator validator,
            CentralCoordinatorClient coordinator,
            NodeState state,
            ILogger<NodeHost> logger,
            Ledger.BalanceCheckService? balances = null)
        {
            _balances = balances;
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
                    // A held value tx needs a fresh ledger anchor, which only comes with a block (possibly empty).
                    var needAnchor = _pool.DeferredAwaitingAnchor && _pool.DeferredCount > 0;
                    if (_pool.Count == 0 && !needAnchor)
                    {
                        await CheckDeferredAsync(force: false);
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
                            _pool.DeferredAwaitingAnchor = false;
                            await CheckDeferredAsync(force: true);
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

        /// <summary>Re-check held value txs after each block, and every 30 s while idle (so they expire).</summary>
        private async Task CheckDeferredAsync(bool force)
        {
            if (_balances == null || _pool.DeferredCount == 0) return;
            if (!force && DateTime.UtcNow - _lastDeferredCheck < TimeSpan.FromSeconds(30)) return;
            _lastDeferredCheck = DateTime.UtcNow;
            var (promoted, dropped, waiting) = await _balances.ProcessDeferredAsync();
            if (promoted + dropped > 0)
                _logger.LogInformation("[MEMPOOL] Held value txs: {Promoted} verified, {Dropped} dropped, {Waiting} waiting", promoted, dropped, waiting);
        }
    }
}












