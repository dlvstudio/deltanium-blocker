using System;
using System.Collections.Generic;

namespace Deltanium.Blocker.Core
{
    public enum TransactionType
    {
        UserRegistration,
        Follow,
        Unfollow,
        CreatePost,
        React,
        Comment,
        NodeRegistration,
        StorageOperation,
        Transfer,
        Donate,
        /// <summary>Fiat on-ramp: treasury/Central credits D to user. Requires Central + Super Admin signatures.</summary>
        TopUp,
        /// <summary>
        /// Central publishes how long stored items are paid for: data.terms is a JSON string of
        /// { fileId, status, paidUntil, deleteAfter, blocks? }. SignPayload = STORAGETERM|id|timestamp|sha256hex(terms),
        /// signed by Central. Stores read it to know, from the chain alone, what they may delete.
        /// </summary>
        StorageTerm,
        /// <summary>
        /// Central's wallet ledger rows of one anchor window (data: anchorSeq, part, parts, rows = JSON string).
        /// SignPayload = LEDGERROWS|id|timestamp|anchorSeq|part|parts|sha256hex(rows), signed by Central. Blockers
        /// replay them into balances and check them against the anchor's LedgerRoot and BalancesRoot.
        /// </summary>
        LedgerRows
    }

    public record Transaction
    {
        public string Id { get; init; } = Guid.NewGuid().ToString("N");
        public TransactionType Type { get; init; }
        public string From { get; init; } = string.Empty;
        public string? To { get; init; }
        public Dictionary<string, object> Data { get; init; } = new();
        public long Timestamp { get; init; } = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        public decimal Fee { get; init; }
        public string Signature { get; init; } = string.Empty;
        /// <summary>Signed build versions of the components that handled this tx (api, store, blocker). Empty on older txs.</summary>
        public List<VersionAttestation> Versions { get; init; } = new();
    }
}












