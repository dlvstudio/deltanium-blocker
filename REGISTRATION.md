# Block Creator Node Registration Guide

## Overview

Block Creator nodes must register with the Central API before they can participate in the Deltanium blockchain network. Registration requires a valid voucher code.

## Prerequisites

1. **Voucher Code**: Obtain a voucher code from the Deltanium team
2. **Central API URL**: URL of the Central Coordination Node (default: `http://localhost:5002`)
3. **Node Endpoint**: Public endpoint where your node will be accessible (default: `http://localhost:5001`)

## Registration Process

### Automatic Registration

The node will automatically register on first startup if `node_info.json` doesn't exist.

### Environment Variables / secrets

Do **not** commit real vouchers into `appsettings.json`. Prefer env or user-secrets:

```bash
export CENTRAL_API_URL="http://localhost:5002"
export NODE_ENDPOINT="http://localhost:5003"
export VOUCHER="YOUR_ONE_TIME_VOUCHER"
export INTERNAL_API_TOKEN="shared-with-central-api"   # for GET /api/tx/pull
```

Or:

```bash
dotnet user-secrets set "NodeRegistration:Voucher" "YOUR_ONE_TIME_VOUCHER"
dotnet user-secrets set "InternalApi:Token" "shared-with-central-api"
dotnet user-secrets set "ConnectionStrings:Default" "Server=...;Database=deltanium_blocker;..."
```

Central API consumes vouchers **atomically** (second use fails).

### Running with Registration

```bash
export VOUCHER="YOUR_ONE_TIME_VOUCHER"
export CENTRAL_API_URL="http://localhost:5002"
export NODE_ENDPOINT="http://localhost:5003"
export INTERNAL_API_TOKEN="shared-with-central-api"

dotnet run
```

### What Happens During Registration

1. **Key Generation**: 
   - A new 12-word mnemonic is generated
   - A key pair (public/private) is derived from the mnemonic
   - Keys are generated using BIP39 and secp256k1

2. **Registration Request**:
   - Node sends registration request to Central API: `POST /api/blockcreator/register`
   - Request includes: `endpoint`, `voucher`, `publicKey`

3. **Voucher Validation**:
   - Central API validates the voucher with an **atomic** mark-used (`Used` flips false→true in one statement)
   - Concurrent double-registration with the same voucher: only one succeeds

4. **Node Storage**:
   - Central API stores the node information
   - Voucher cannot be reused

5. **Local Storage**:
   - Node saves credentials to `node_info.json`:
     - Mnemonic (12 words)
     - Private key (hex)
     - Public key (hex)
     - Endpoint
     - API response

### Node Info File

After successful registration, `node_info.json` will be created:

```json
{
  "Mnemonic": "word1 word2 word3 ... word12",
  "PrivateKey": "hex_private_key",
  "PublicKey": "hex_public_key",
  "Endpoint": "http://localhost:5001",
  "ApiResponse": {
    "Id": "node_id",
    "PublicKey": "hex_public_key",
    "Endpoint": "http://localhost:5001"
  }
}
```

⚠️ **IMPORTANT**: Keep `node_info.json` secure! It contains your private key and mnemonic.

### Re-registration

If `node_info.json` already exists, the node will:
- Load existing credentials
- Skip registration
- Use existing keys

To re-register:
1. Delete `node_info.json`
2. Get a new voucher
3. Run the node again

## Central API Endpoints

### Register Block Creator Node

```
POST /api/blockcreator/register
Content-Type: application/json

{
  "endpoint": "http://localhost:5001",
  "voucher": "VOUCHER_CODE",
  "publicKey": "hex_public_key"
}
```

**Response:**
```json
{
  "id": "node_id",
  "publicKey": "hex_public_key",
  "endpoint": "http://localhost:5001"
}
```

### List Block Creator Nodes

```
GET /api/blockcreator/list
```

**Response:**
```json
[
  {
    "id": "node_id",
    "publicKey": "hex_public_key",
    "endpoint": "http://localhost:5001",
    "isActive": true,
    "createdAt": "2024-01-01T00:00:00Z",
    "lastSeen": "2024-01-01T00:00:00Z",
    "stakeAmount": 0
  }
]
```

## Troubleshooting

### Registration Failed: "Invalid or already used voucher"
- The voucher code is invalid or has already been used
- Get a new voucher from the Deltanium team

### Registration Failed: "Block Creator node already registered"
- A node with the same public key is already registered
- If this is intentional, delete `node_info.json` and use a different voucher
- If this is an error, contact support

### Node keys not found after registration
- Check that `node_info.json` was created successfully
- Verify file permissions
- Check console output for errors

### VOUCHER environment variable is required
- Set the `VOUCHER` environment variable before running
- Or modify `Program.cs` to read from config file

## Security Best Practices

1. **Protect node_info.json**:
   - Store in secure location
   - Use file system permissions (chmod 600)
   - Never commit to version control

2. **Backup Mnemonic**:
   - Write down the mnemonic phrase
   - Store in secure location
   - Can be used to recover keys

3. **Private Key Security**:
   - Never share private key
   - Never expose in logs or errors
   - Use secure key storage in production

4. **Network Security**:
   - Use HTTPS for Central API in production
   - Use secure endpoints for node
   - Implement firewall rules

## Production Deployment

For production, consider:

1. **Configuration File**: Use `appsettings.json` instead of environment variables
2. **Key Management**: Use HSM or secure key storage
3. **Monitoring**: Add health checks and monitoring
4. **Logging**: Secure logging without exposing keys
5. **Backup**: Regular backups of `node_info.json` (encrypted)











