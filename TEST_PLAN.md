# Unit Test Plan for deltanium-blocker

## Overview
This document outlines the unit testing strategy for the `deltanium-blocker` project (Block Creator node).

## Test Project Structure
```
deltanium-blocker.Tests/
├── Core/
│   ├── BlockTests.cs
│   ├── TransactionTests.cs
│   ├── NodeStateTests.cs
│   └── NodeConfigTests.cs
├── Services/
│   ├── TransactionPoolTests.cs
│   ├── BlockBuilderTests.cs
│   ├── BlockValidatorTests.cs
│   ├── CentralCoordinatorClientTests.cs
│   ├── NodeRegistrationServiceTests.cs
│   └── NodeHostTests.cs
└── Helpers/
    ├── TestDataBuilder.cs
    └── TestFileHelper.cs
```

## Dependencies
- xUnit
- Moq
- FluentAssertions
- AutoFixture (optional)
- Microsoft.NET.Test.Sdk

## Test Priorities

### HIGH Priority
1. **TransactionPool** - Core transaction management
2. **BlockBuilder** - Block creation logic
3. **BlockValidator** - Block validation logic
4. **Block.CalculateMerkleRoot** - Critical cryptographic function

### MEDIUM Priority
5. **CentralCoordinatorClient** - Communication with Central API
6. **NodeRegistrationService** - Node registration flow
7. **NodeState** - Chain state management

### LOW Priority
8. **NodeConfig** - Configuration loading
9. **NodeHost** - Integration orchestration (may require integration tests)

## Test Cases

### Core/BlockTests.cs
- `CalculateMerkleRoot_EmptyTransactions_ShouldReturnEmptyString`
- `CalculateMerkleRoot_SingleTransaction_ShouldReturnHash`
- `CalculateMerkleRoot_MultipleTransactions_ShouldCalculateCorrectly`
- `CalculateMerkleRoot_OddNumberOfTransactions_ShouldHandleCorrectly`
- `Hash_ValidInput_ShouldReturnHexString`
- `Hash_DifferentInputs_ShouldReturnDifferentHashes`
- `Hash_EmptyInput_ShouldReturnHash`

### Core/TransactionTests.cs
- `Transaction_DefaultValues_ShouldBeValid`
- `Transaction_WithData_ShouldPreserveData`

### Core/NodeStateTests.cs
- `AddBlock_FirstBlock_ShouldSetHeightToZero`
- `AddBlock_MultipleBlocks_ShouldIncrementHeight`
- `LatestBlockId_EmptyChain_ShouldReturnEmptyString`
- `LatestBlockId_WithBlocks_ShouldReturnLastBlockId`
- `Height_EmptyChain_ShouldReturnMinusOne`
- `Height_WithBlocks_ShouldReturnLastHeight`
- `Chain_ShouldReturnReadOnlyList`

### Core/NodeConfigTests.cs
- `LoadDefaults_NoNodeInfoFile_ShouldReturnDefaults`
- `LoadDefaults_WithNodeInfoFile_ShouldLoadKeys`
- `LoadDefaults_InvalidNodeInfoFile_ShouldReturnDefaults`

### Services/TransactionPoolTests.cs
- `TryAdd_ValidTransaction_ShouldReturnTrue`
- `TryAdd_InvalidTransaction_ShouldReturnFalse`
- `TryAdd_DuplicateTransaction_ShouldReturnFalse`
- `TryAdd_TransactionWithEmptyId_ShouldReturnFalse`
- `TryAdd_TransactionWithEmptyFrom_ShouldReturnFalse`
- `TryAdd_TransactionWithInvalidTimestamp_ShouldReturnFalse`
- `Snapshot_EmptyPool_ShouldReturnEmptyCollection`
- `Snapshot_WithTransactions_ShouldReturnAllTransactions`
- `Snapshot_ShouldReturnReadOnlyCollection`
- `Remove_ExistingTransactions_ShouldRemoveThem`
- `Remove_NonExistentTransactions_ShouldNotThrow`
- `Count_ShouldReflectPoolSize`

### Services/BlockBuilderTests.cs
- `BuildBlock_WithAuthorization_ShouldCreateBlock`
- `BuildBlock_EmptyPool_ShouldCreateEmptyBlock`
- `BuildBlock_WithTransactions_ShouldIncludeTransactions`
- `BuildBlock_ShouldSetPreviousBlockId`
- `BuildBlock_ShouldIncrementHeight`
- `BuildBlock_ShouldCalculateMerkleRoot`
- `BuildBlock_ShouldSetCreatorPublicKey`
- `BuildBlock_ShouldSetCentralAuthorizationSignature`
- `BuildBlock_ShouldGenerateBlockId`
- `BuildBlock_ShouldSignBlock`
- `BuildBlock_TransactionsOrderedByFee_ShouldPrioritizeHighFee`
- `BuildBlock_MoreThan100Transactions_ShouldCapAt100`

### Services/BlockValidatorTests.cs
- `Validate_ValidBlock_ShouldReturnTrue`
- `Validate_BlockWithEmptyId_ShouldReturnFalse`
- `Validate_BlockWithWrongHeight_ShouldReturnFalse`
- `Validate_BlockWithInvalidMerkleRoot_ShouldReturnFalse`
- `Validate_BlockWithInvalidTransactionTimestamp_ShouldReturnFalse`
- `Validate_GenesisBlock_ShouldReturnTrue`
- `Validate_BlockWithEmptyTransactions_ShouldReturnTrue`

### Services/CentralCoordinatorClientTests.cs
- `RequestBlockSlotAsync_ShouldReturnAuthorizationOrNull`
- `RequestBlockSlotAsync_ShouldReturnValidSignature`
- `ReportInvalidBlockAsync_ShouldNotThrow`

### Services/NodeRegistrationServiceTests.cs
- `RegisterNodeIfNeededAsync_NodeInfoExists_ShouldReturnAlreadyRegistered`
- `RegisterNodeIfNeededAsync_NewNode_ShouldRegisterWithAPI`
- `RegisterNodeIfNeededAsync_APISuccess_ShouldSaveNodeInfo`
- `RegisterNodeIfNeededAsync_APIFailure_ShouldReturnError`
- `LoadNodeInfoAsync_FileExists_ShouldReturnNodeInfo`
- `LoadNodeInfoAsync_FileNotExists_ShouldReturnNull`
- `LoadNodeInfoAsync_InvalidJson_ShouldReturnNull`

### Services/NodeHostTests.cs
- `RunAsync_ShouldInitializeGenesisBlock`
- `RunAsync_WithCancellation_ShouldStopGracefully`
- `RunAsync_BlockCreationLoop_ShouldCreateBlocksWhenAuthorized`

## Coverage Goals
- **Target**: 80%+ code coverage
- **Focus Areas**: 
  - Transaction validation logic
  - Block building and validation
  - Merkle root calculation
  - State management

## Notes
- Use `Mock<HttpMessageHandler>` for HTTP client testing in `NodeRegistrationService`
- Use temporary directories for file-based tests
- Mock `CentralCoordinatorClient` in `BlockBuilder` tests if needed
- Consider integration tests for `NodeHost` orchestration logic







