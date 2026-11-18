using System;
using System.Security.Cryptography;
using System.Text;
using Deltanium.Blocker.Services;
using NBitcoin;

namespace Deltanium.Blocker.Core
{
    /// <summary>
    /// A component (api, store, blocker) states which build handled a transaction and signs that statement
    /// with its own key: sha256(NetworkScope.Scope(Message(...))), ECDSA DER, base64.
    /// The API's comes with Central's decision, a Store's with the tx it submits, the Blocker's when it accepts the tx.
    /// </summary>
    public record VersionAttestation(string Service, string Version, string Commit, string PublicKey, string Signature)
    {
        public const string Api = "api";
        public const string Store = "store";
        public const string Blocker = "blocker";

        public static string Message(string txId, string service, string version, string commit) =>
            $"DELTANIUM-TX-VERSION\n{txId}\n{service}\n{version}\n{commit}";

        public bool IsValidFor(string txId) =>
            !string.IsNullOrWhiteSpace(Service) && !string.IsNullOrWhiteSpace(Version) &&
            !string.IsNullOrWhiteSpace(PublicKey) && !string.IsNullOrWhiteSpace(Signature) &&
            SignatureVerifier.Verify(PublicKey, Message(txId, Service, Version, Commit ?? ""), Signature);

        public static VersionAttestation Create(string txId, string service, string version, string commit, string privateKeyHex)
        {
            var key = new Key(Convert.FromHexString(privateKeyHex));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(NetworkScope.Scope(Message(txId, service, version, commit))));
            var sig = Convert.ToBase64String(key.Sign(new uint256(hash)).ToDER());
            return new VersionAttestation(service, version, commit, key.PubKey.ToHex(), sig);
        }
    }
}
