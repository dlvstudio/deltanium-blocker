# StorageOperation Transaction Validation - Technical Reference

## Overview
This document details the enhanced BlockValidator's StorageOperation transaction validation logic implemented in `deltanium-blocker/Services/BlockValidator.cs`.

## Validation Flow

### Entry Point
```csharp
public bool Validate(Block block, NodeState state, out string reason)
    └─> ValidateTransaction(Transaction tx, out string reason)
        └─> ValidateStorageOperation(Transaction tx, out string reason)
```

## Six-Rule Validation System

### Rule 1: Extract & Parse Contract
**Step**: Deserialize contract object from `tx.Data["contract"]`

**Code**:
```csharp
if (!tx.Data.TryGetValue("contract", out var contractObj))
    return false; // "StorageOperation must include contract in Data"

StorageContract contract = null;
if (contractObj is System.Text.Json.JsonElement je)
    contract = JsonSerializer.Deserialize<StorageContract>(je.GetRawText());
else if (contractObj is StorageContract sc)
    contract = sc;
else if (contractObj is string contractJson)
    contract = JsonSerializer.Deserialize<StorageContract>(contractJson);
```

**Valid Formats**:
- `tx.Data["contract"]` as direct StorageContract object
- `tx.Data["contract"]` as JsonElement (from JSON deserialization)
- `tx.Data["contract"]` as JSON string

**Failure**: "Failed to parse contract from StorageOperation: {error}"

---

### Rule 2: Verify Both Signatures Present
**Step**: Check that both App and Storage Node have signed the contract

**Code**:
```csharp
if (string.IsNullOrWhiteSpace(contract.AppSignature))
    return false; // "Contract missing App signature"

if (string.IsNullOrWhiteSpace(contract.StorageNodeSignature))
    return false; // "Contract missing Storage Node signature"
```

**Security Rationale**:
- Ensures mutual agreement documented on-chain
- App signature proves commitment to terms
- Storage Node signature proves offer to store

**Note**: Actual signature verification happens off-chain by app clients; Blocker only verifies presence

---

### Rule 3: Verify Fee Matches
**Step**: Confirm transaction fee equals contract's total fee

**Code**:
```csharp
if (tx.Fee != (long)contract.TotalFee)
    return false; // "Transaction fee {fee} does not match contract fee {contractFee}"
```

**Security Rationale**:
- Prevents Storage Node from claiming higher fees than agreed
- Ensures Block Creator receives correct portion (30%)
- Ensures Storage Node receives correct portion (70%)

**Example**:
```
Contract.TotalFee = 100
tx.Fee must be exactly 100
Distribution: 70 → StorageNode, 30 → BlockCreator
```

---

### Rule 4: Verify FileIds Match Contract
**Step**: Ensure all files in transaction are covered by contract

**Code**:
```csharp
if (tx.Data.TryGetValue("FileIds", out var fileIdsObj) || 
    tx.Data.TryGetValue("fileIds", out fileIdsObj))
{
    var txFileIds = ExtractFileIds(fileIdsObj);
    foreach (var fileId in txFileIds)
    {
        if (!contract.CoversFileId(fileId))
            return false; // "File ID {fileId} not covered by contract"
    }
}
```

**Supported Data Formats**:
- `tx.Data["FileIds"] = List<string>`
- `tx.Data["fileIds"] = List<object> → cast to strings`
- `tx.Data["FileIds"] = JsonElement (array)`

**Security Rationale**:
- Prevents Storage Node from hiding unauthorized files in transaction
- Ensures only pre-agreed files count toward storage
- Prevents fee evasion via "surprise" files

---

### Rule 5: Verify File Size Within Limits
**Step**: Confirm total file size doesn't exceed contract allowance

**Code**:
```csharp
if (tx.Data.TryGetValue("FileSize", out var fileSizeObj) ||
    tx.Data.TryGetValue("fileSize", out fileSizeObj))
{
    long fileSize = 0;
    if (fileSizeObj is long ls) fileSize = ls;
    else if (fileSizeObj is int isize) fileSize = isize;
    else if (long.TryParse(fileSizeObj?.ToString() ?? "0", out var parsed)) 
        fileSize = parsed;

    if (fileSize > 0 && !contract.CoversFileSize(fileSize))
        return false; // "File size {size} exceeds contract limit {limit}"
}
```

**Note**: 
- If fileSize is 0 or not provided, this check is skipped
- Production should track cumulative usage, not just per-transaction

**Security Rationale**:
- Prevents Storage Node from storing more than agreed
- Protects storage network resource constraints
- Ensures fee structure matches actual storage used

---

### Rule 6: Verify Contract Time Validity
**Step**: Check that contract is active at block inclusion time

**Code**:
```csharp
if (!contract.IsValidNow())
    return false; // "Contract time period is invalid"
```

**`IsValidNow()` Logic**:
```csharp
public bool IsValidNow()
{
    var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    
    if (Status != "Active") return false;
    if (now < StartDateUnix) return false; // Not yet started
    
    if (ContractType == "TimeFixed" && EndDateUnix.HasValue && now > EndDateUnix)
        return false; // Expired
    
    if (ContractType == "OpenEnded" && now > StartDateUnix + (365 * 24 * 60 * 60))
        return false; // Auto-expires after 1 year
    
    return true;
}
```

**Scenarios**:
```
TimeFixed: 2024-01-01 to 2024-12-31
  ✓ Valid: 2024-06-15
  ✗ Invalid: 2024-12-32

OpenEnded: 2024-01-01
  ✓ Valid: 2024-12-31
  ✗ Invalid: 2025-01-02
```

**Security Rationale**:
- Prevents double-storage claims after contract expires
- Enforces contract duration terms
- OpenEnded auto-expiry prevents indefinite claims

---

### Rule 7: Verify App Cosignature
**Step**: Confirm App has approved the transaction with secondary signature

**Code**:
```csharp
if (tx.Cosignatures == null || tx.Cosignatures.Count == 0)
    return false; // "StorageOperation must include App cosignature"

if (!tx.Cosignatures.ContainsKey(contract.AppPublicKey))
    return false; // "App cosignature not found for specified App public key"
```

**Cosignature Structure**:
```csharp
tx.Cosignatures = new Dictionary<string, string>
{
    { contract.AppPublicKey, appSignature }  // App approves storage operation
};
```

**Security Rationale**:
- Proves App (user) approved file storage execution
- Dual signatures: Storage Node creates tx, App verifies
- Prevents Storage Node from claiming storage without user consent

---

## Error Responses

### Validation Failures
All failures are caught at block validation level, causing entire block rejection:

| Error | Cause | Recovery |
|-------|-------|----------|
| "StorageOperation must include contract in Data" | Contract not embedded | Resubmit with contract |
| "Contract missing App signature" | incomplete signing | Get both signatures |
| "Contract missing Storage Node signature" | Incomplete signing | Get both signatures |
| "Transaction fee ... does not match contract fee" | Fee mismatch | Align fee with contract |
| "File ID {id} not covered by contract" | Unauthorized file | Add to contract fileIds |
| "File size ... exceeds contract limit" | Size overage | Reduce file size |
| "Contract time period is invalid" | Expired/not-started | Create new contract |
| "StorageOperation must include App cosignature" | Missing App approval | Get App signature |
| "App cosignature not found for specified App public key" | Wrong signer | Use correct App pubkey |

---

## Edge Cases

### Missing Fields
- `FileIds` not in Data: Skipped (optional check)
- `FileSize` not in Data: Skipped (optional check)
- Contract extracted but null: Rejected as invalid

### JSON Deserialization Errors
- Caught in try-catch block
- Returns: "Failed to parse contract from StorageOperation: {error}"

### Multiple Cosigners
- System designed for single App cosigner (contract.AppPublicKey)
- Can extend to multiple signers via multiple entries in Cosignatures dict

### Floating Point Precision
- Fee comparison uses long (after casting)
- Potential loss if totalFee has decimals
- Store should enforce integer fee amounts

---

## Integration Points

### Block Creation Flow
```
1. Block Creator receives StorageOperation tx
2. BlockValidator.Validate(block, state) called
3. For each tx, ValidateTransaction() dispatches to ValidateStorageOperation()
4. All 7 rules checked
5. If ANY rule fails → Block is rejected
6. If ALL rules pass → StorageOperation included in block
7. Block Creator earns 30% of tx fee
8. Storage Node earns 70% of tx fee (via settlement layer)
```

### Storage Node Submission Flow
```
1. Store accepts file upload (validates contract upload-time)
2. Store creates StorageOperation tx with:
   - contract object (full, with signatures)
   - storage operation data (fileIds, blockId, size)
   - Storage Node's tx signature
3. App provides cosignature
4. tx submitted to API /api/tx/submit
5. Blocker receives and validates with BlockValidator
6. If passes: included in next block
7. If fails: rejected with error reason
```

---

## Performance Considerations

- **Contract Parsing**: One-time deserialization per StorageOperation
- **FileIds Extraction**: O(n) where n = fileIds count
- **Cosignature Lookup**: O(1) dictionary lookup
- **Time Check**: O(1) arithmetic comparison

**Total Complexity**: O(n) dominated by FileIds iteration

---

## Testing Scenarios

### Valid Scenarios (Should Pass All 7 Rules)
```csharp
// Scenario 1: Complete TimeFixed contract
var contract = new StorageContract
{
    ContractType = "TimeFixed",
    StartDateUnix = DateTime.Now.AddDays(-1).ToUnixTimestamp(),
    EndDateUnix = DateTime.Now.AddDays(30).ToUnixTimestamp(),
    AppSignature = "valid_signature",
    StorageNodeSignature = "valid_signature",
    FileIds = ["file1", "file2"],
    TotalFileSize = 1000000,
    TotalFee = 100
};

var tx = new Transaction
{
    Fee = 100,
    Data = {
        { "contract", contract },
        { "FileIds", new List<string> { "file1" } },
        { "FileSize", 500000 }
    },
    Cosignatures = { { contract.AppPublicKey, "app_cosig" } }
};
// Should pass all rules
```

### Invalid Scenarios (Should Fail Specific Rule)
```csharp
// Fail Rule 2: Missing App signature
contract.AppSignature = "";
// Error: "Contract missing App signature"

// Fail Rule 3: Fee mismatch
tx.Fee = 99; // Not 100
// Error: "Transaction fee 99 does not match contract fee 100"

// Fail Rule 4: FileId not covered
tx.Data["FileIds"] = new List<string> { "file3" };
// Error: "File ID file3 not covered by contract"

// Fail Rule 6: Expired contract
contract.EndDateUnix = DateTime.Now.AddDays(-1).ToUnixTimestamp();
// Error: "Contract time period is invalid"

// Fail Rule 7: No cosignature
tx.Cosignatures = new();
// Error: "StorageOperation must include App cosignature"
```

---

## Future Enhancements

1. **Signature Verification**: Implement actual cryptographic verification of signatures
2. **Nonce/Replay Prevention**: Add contract version/nonce to prevent replay attacks
3. **Storage Proof**: Add merkle proof of actual file storage on Storage Node
4. **Cumulative Usage Tracking**: Maintain state of how much storage used vs contracted
5. **Partial Failure Recovery**: Allow partial acceptance of fileIds if some exceed limits

