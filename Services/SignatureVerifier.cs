using System;
using System.Security.Cryptography;
using System.Text;
using NBitcoin;

namespace Deltanium.Blocker.Services
{
    public static class SignatureVerifier
    {
        public static bool Verify(string publicKey, string message, string signatureBase64)
        {
            try
            {
                var pubKey = new PubKey(publicKey);
                using var sha256 = SHA256.Create();
                var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(message));
                var signatureBytes = Convert.FromBase64String(signatureBase64);

                var hashLE = new uint256(hashBytes);
                var hashBE = new uint256(hashBytes, lendian: false);

                var resultLE = pubKey.Verify(hashLE, signatureBytes);
                var resultBE = pubKey.Verify(hashBE, signatureBytes);

                if (resultLE || resultBE) return true;

                var ecdsaSig = NBitcoin.Crypto.ECDSASignature.FromDER(signatureBytes).MakeCanonical();
                return pubKey.Verify(hashLE, ecdsaSig) || pubKey.Verify(hashBE, ecdsaSig);
            }
            catch
            {
                return false;
            }
        }
    }
}
