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
        Donate
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
    }
}












