# Deltanium Block Creator Node

A .NET implementation of the Block Creator node for the Deltanium blockchain network. This node is responsible for processing transactions, creating blocks, and validating blocks created by other nodes in the network.

## Production

| | URL |
|--|-----|
| **Block creator 1** | [https://blocker-1.deltanium.com](https://blocker-1.deltanium.com) |
| Central API | [https://api.deltanium.com](https://api.deltanium.com) |
| App | [https://app.deltanium.com](https://app.deltanium.com) |

Local default: `http://127.0.0.1:5003`.

## Overview

The Deltanium Block Creator node is a critical component of the Deltanium blockchain infrastructure. It works in coordination with the Central Coordination Node ([api.deltanium.com](https://api.deltanium.com)) to maintain the blockchain by:

- Receiving and validating transactions from the network
- Creating new blocks when authorized by the Central Node
- Validating blocks created by other Block Creator nodes
- Maintaining local blockchain state
- Collecting transaction fees

## Features

### Current Implementation (MVP)

- ✅ **Transaction Pool Management**
  - Receive and store pending transactions
  - Prioritize transactions by fee and timestamp
  - Remove transactions after block inclusion

- ✅ **Block Creation**
  - Request block creation authorization from Central Node
  - Select transactions from pool
  - Build block structure with Merkle root
  - Sign blocks with node's private key

- ✅ **Block Validation**
  - Validate block structure and signatures
  - Verify all transactions in block
  - Check block against local state

- ✅ **Node Host**
  - Periodic block creation loop
  - Integration with Central Coordinator
  - Graceful shutdown handling

### Planned Features

- [x] **Central API decision + block-slot signing:** Central API signs decisions and block-slot grants; Blocker verifies with trusted Central pubkey. Blocker signs blocks with node private key; BlockValidator verifies CreatorSignature.
- [ ] Full P2P cryptographic signing (beyond Central/block)
- [ ] P2P network communication
- [ ] Block broadcasting to network
- [ ] Fork detection and resolution
- [x] State persistence (MySQL preferred; JSON fallback for mempool/blocks)
- [x] Tx pull from Central API protected by `X-Internal-Token` (`INTERNAL_API_TOKEN`)
- [ ] Metrics and monitoring
- [ ] Health reporting to Central Node
- [ ] Penalty handling for missed blocks

## Architecture

### Core Components

```
┌─────────────────────────────────────────┐
│           NodeHost (Orchestrator)        │
├─────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐    │
│  │TransactionPool│  │ BlockBuilder │    │
│  └──────────────┘  └──────────────┘    │
│  ┌──────────────┐  ┌──────────────┐    │
│  │BlockValidator│  │CentralClient │    │
│  └──────────────┘  └──────────────┘    │
│  ┌──────────────┐                       │
│  │  NodeState   │                       │
│  └──────────────┘                       │
└─────────────────────────────────────────┘
```

### Project Structure

```
deltanium-blocker/
├── Core/
│   ├── Block.cs              # Block data structure and Merkle tree calculation
│   ├── Transaction.cs        # Transaction data structure
│   ├── NodeConfig.cs         # Node configuration
│   └── NodeState.cs          # Local blockchain state management
├── Services/
│   ├── TransactionPool.cs    # Transaction pool management
│   ├── BlockBuilder.cs       # Block creation logic
│   ├── BlockValidator.cs    # Block validation logic
│   ├── CentralCoordinatorClient.cs  # Communication with Central Node
│   └── NodeHost.cs          # Main orchestration service
├── Program.cs               # Application entry point
└── deltanium-blocker.csproj # Project file
```

## Getting Started

### Prerequisites

- .NET 8.0 SDK or later
- Access to Deltanium Central Coordination Node (deltanium.com)
- Node public/private key pair (for production)

### Installation

1. Clone the repository:
```bash
git clone https://dlvstudio.visualstudio.com/Deltanium/_git/Deltanium%20Blocker
cd deltanium-blocker
```

2. Restore dependencies:
```bash
dotnet restore
```

3. Build the project:
```bash
dotnet build
```

### Configuration

The node can be configured through `NodeConfig` class. Current default settings:

```csharp
public class NodeConfig
{
    public string NodePublicKey { get; init; } = "NODE_PUBKEY_PLACEHOLDER";
    public string NodePrivateKey { get; init; } = "NODE_PRIVKEY_PLACEHOLDER";
    public Uri CentralEndpoint { get; init; } = new Uri("https://api.deltanium.com");
    public TimeSpan BlockCreationInterval { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan ValidationPollInterval { get; init; } = TimeSpan.FromSeconds(5);
}
```

**Configuration Options:**
- `NodePublicKey`: Your node's public key (must be registered with Central Node)
- `NodePrivateKey`: Your node's private key (keep secure!)
- `CentralEndpoint`: URL of the Central Coordination Node
- `CentralApiPublicKey`: Trusted Central API public key for verifying decision and block-slot signatures. Set via **`CENTRAL_API_PUBLIC_KEY`** env, or the node will try to fetch it from `GET /api/blockcreatornode/decision-key` at startup. If not set, the node falls back to using the key from the API response (less secure; use in dev only).
- `BlockCreationInterval`: How often to check for block creation authorization
- `ValidationPollInterval`: How often to validate incoming blocks

### Running the Node

1. **Development Mode:**
```bash
dotnet run
```

2. **Production Mode:**
```bash
dotnet build -c Release
dotnet run -c Release
```

3. **As a Service (Linux):**
```bash
# Create systemd service file
sudo nano /etc/systemd/system/deltanium-blocker.service

# Start service
sudo systemctl start deltanium-blocker
sudo systemctl enable deltanium-blocker
```

## How It Works

### Block Creation Flow

```
1. NodeHost requests block creation authorization from Central Node
   ↓
2. If authorized, BlockBuilder selects transactions from pool
   ↓
3. BlockBuilder creates block structure:
   - Previous block hash
   - Merkle root of transactions
   - Block height
   - Timestamps
   - Signatures
   ↓
4. BlockValidator validates the created block
   ↓
5. If valid, add block to local state
   ↓
6. Remove included transactions from pool
   ↓
7. Broadcast block to network (TODO)
```

### Transaction Processing

1. Transactions are received and added to the transaction pool
2. Transactions are validated before being added:
   - Signature verification
   - State validation
   - Fee validation
3. Transactions are prioritized by fee and timestamp
4. Selected transactions are included in blocks

### Block Validation

When receiving blocks from other nodes:
1. Verify block structure (hash, Merkle root, signatures)
2. Verify Central Node authorization
3. Validate all transactions in the block
4. Check block against local state
5. Accept or reject block

## Development

### Building

```bash
# Debug build
dotnet build

# Release build
dotnet build -c Release
```

### Running Tests

```bash
# TODO: Add test project
dotnet test
```

### Code Structure

- **Core/**: Core data structures and models
- **Services/**: Business logic and services
- **Program.cs**: Application bootstrap and entry point

## Security Considerations

⚠️ **Important Security Notes:**

1. **Private Key Security:**
   - Never commit private keys to version control
   - Use secure key storage (HSM, encrypted storage)
   - Rotate keys periodically

2. **Network Security:**
   - Use TLS/SSL for all communications
   - Authenticate all messages
   - Implement rate limiting

3. **State Security:**
   - Verify all state transitions
   - Check for double-spending
   - Validate all signatures

## Current Status

This is an **MVP (Minimum Viable Product)** implementation. The following features are stubbed or TODO:

- [ ] Real cryptographic signing (currently placeholder)
- [ ] Network communication (P2P)
- [ ] Block broadcasting
- [ ] State persistence
- [ ] Fork handling
- [ ] Metrics and monitoring
- [ ] Health reporting
- [ ] Penalty handling

## Contributing

Contributions are welcome! Please follow these guidelines:

1. Fork the repository
2. Create a feature branch
3. Make your changes
4. Add tests if applicable
5. Submit a pull request

### Development Guidelines

- Follow C# coding conventions
- Add XML documentation for public APIs
- Write unit tests for new features
- Update README for significant changes

## License

MIT — see [LICENSE](LICENSE).

## Related Projects

- **deltanium-core**: Core blockchain functionality
- **deltanium-api**: Central Coordination Node API
- **deltanium-store**: Content Store nodes
- **deltanium-app**: User client application

## Support

For issues and questions:
- Create an issue in Azure DevOps
- Contact the development team
- Check the main Deltanium documentation

## References

- [Deltanium Blockchain Proposal](../deltanium-app/BLOCKCHAIN_PROPOSAL.md)
- [Block Creator Node Details](../deltanium-app/BLOCK_CREATOR_NODE_DETAILS.md)
- [Deltanium Core README](../deltanium-core/README.md)
