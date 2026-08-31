# HB_BIM Tools License Records

This folder is the working area for license issuing records.

## What is tracked

- `README.md`: license record workflow.
- `issued_licenses.template.csv`: example columns for issued license records.
- `.gitignore`: keeps real issued records and generated license keys out of Git.

## What is not tracked

- `issued_licenses.csv`: real license issuing ledger.
- `Generated\*.txt`: generated license key files.
- Private keys, `.pfx`, `.p12`, `.key`, `.pem`, or XML private key files.

## Standard workflow

1. Get the user's machine code from HB_BIM Tools license manager.
2. Generate a signed license key with `Tools\GenerateLicenseKey.ps1`.
3. Send only the generated license key text to the user.
4. Keep `issued_licenses.csv` as the local issuing ledger.
5. Never commit generated license keys or private keys.

Example:

```powershell
.\Tools\GenerateLicenseKey.ps1 `
  -LicenseType Professional `
  -UserName "User Name" `
  -Company "Company Name" `
  -Days 365 `
  -MachineCode "XXXX-XXXX-XXXX-XXXX" `
  -PrivateKeyPath "D:\SecureKeys\HB_BIM_License_Private.xml"
```

By default, generated license keys are saved to `LicenseRecords\Generated`, and the issuing summary is appended to `LicenseRecords\issued_licenses.csv`.
