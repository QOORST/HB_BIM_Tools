===============================================================
  HB_BIM Tools - Manual Deployment Package
===============================================================

This package contains all files needed to manually deploy
HB_BIM Tools to computers where the installer cannot run.

===============================================================
  Quick Start
===============================================================

1. Copy this entire "Deployment" folder to the target computer

2. On the target computer, open PowerShell as Administrator:
   - Press Win + X
   - Select "Windows PowerShell (Administrator)"

3. Navigate to the Deployment folder:
   cd "path\to\Deployment"

4. Run the deployment script:
   .\Deploy.ps1

5. Follow the prompts to select which Revit versions to deploy

6. Close all Revit applications and restart

===============================================================
  Package Contents
===============================================================

2022\               - Files for Revit 2022
2024\               - Files for Revit 2024
2025\               - Files for Revit 2025
2026\               - Files for Revit 2026
Deploy.ps1          - Automated deployment script
README.txt          - General information

Each version folder contains:
- YD_RevitTools.LicenseManager.dll (main plugin)
- All required dependency DLLs
- runtimes\ (native SQLite runtime for clarification tracking)
- Resources\Icons\ (icon files)
- Resources\Families\ (Pipe Sleeve default families)
  - 套管-圓形_無.rfa
  - 開孔-矩形_無.rfa

===============================================================
  Manual Installation (if script fails)
===============================================================

If the Deploy.ps1 script cannot run, you can manually copy files:

For Revit 2024:
1. Create folder: C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM\
2. Copy all files from "2024\" folder to the above location
3. Create file: C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM_Tools.addin
4. Copy the content from the .addin template below

For Revit 2025:
- Same steps, but replace "2024" with "2025"

For Revit 2022 or 2026:
- Same steps, but replace "2024" with "2022" or "2026"

===============================================================
  .addin File Template (for Revit 2024)
===============================================================

<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>HB_BIM Tools</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2024\HB_BIM\YD_RevitTools.LicenseManager.dll</Assembly>
    <FullClassName>YD_RevitTools.LicenseManager.App</FullClassName>
    <ClientId>B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7</ClientId>
    <VendorId>LAN</VendorId>
    <VendorDescription>HB_BIM Tools, www.ydbim.com</VendorDescription>
  </AddIn>
</RevitAddIns>

Note: For Revit 2022, 2025, or 2026, change all "2024" to the target Revit version in the paths.

===============================================================
  Troubleshooting
===============================================================

Q: PowerShell script won't run?
A: Run this command first:
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

Q: "Cannot load file or assembly" error?
A: Make sure all DLL files are copied correctly

Q: Clarification Deck history/progress cannot open?
A: Make sure Microsoft.Data.Sqlite.dll, SQLitePCLRaw*.dll, and the runtimes\ folder were copied.

Q: Icons not showing?
A: Verify that Resources\Icons\ folder is copied

Q: Pipe Sleeve default families not loaded?
A: Verify that Resources\Families\ contains 套管-圓形_無.rfa and 開孔-矩形_無.rfa. These files are included in the installer and deployment package.

Q: Need to deploy to multiple computers?
A: Copy this entire Deployment folder to a network share
   and run Deploy.ps1 on each computer

===============================================================
  System Requirements
===============================================================

- Autodesk Revit 2024 or 2025
- Windows 10/11 (64-bit)
- .NET Framework 4.8 or higher
- Administrator privileges (for installation)

===============================================================
  Support
===============================================================

Email: qoorst123456@gmail.com
Phone: 04-2376-1698
Website: www.ydbim.com

===============================================================
