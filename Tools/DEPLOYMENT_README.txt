===============================================================
  YD_BIM Tools - Manual Deployment Package
===============================================================

This package contains all files needed to manually deploy
YD_BIM Tools to computers where the installer cannot run.

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

2024\               - Files for Revit 2024
2025\               - Files for Revit 2025
Deploy.ps1          - Automated deployment script
README.txt          - General information

Each version folder contains:
- YD_RevitTools.LicenseManager.dll (main plugin)
- All required dependency DLLs
- Resources\Icons\ (icon files)

===============================================================
  Manual Installation (if script fails)
===============================================================

If the Deploy.ps1 script cannot run, you can manually copy files:

For Revit 2024:
1. Create folder: C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM\
2. Copy all files from "2024\" folder to the above location
3. Create file: C:\ProgramData\Autodesk\Revit\Addins\2024\YD_RevitTools.LicenseManager.addin
4. Copy the content from the .addin template below

For Revit 2025:
- Same steps, but replace "2024" with "2025"

===============================================================
  .addin File Template (for Revit 2024)
===============================================================

<?xml version="1.0" encoding="utf-8"?>
<RevitAddIns>
  <AddIn Type="Application">
    <Name>YD_BIM Tools</Name>
    <Assembly>C:\ProgramData\Autodesk\Revit\Addins\2024\YD_BIM\YD_RevitTools.LicenseManager.dll</Assembly>
    <FullClassName>YD_RevitTools.LicenseManager.App</FullClassName>
    <ClientId>B3F5D2D4-9392-4A9E-9C0D-A6F5DD93FAC7</ClientId>
    <VendorId>YD</VendorId>
    <VendorDescription>YD BIM Tools, www.ydbim.com</VendorDescription>
  </AddIn>
</RevitAddIns>

Note: For Revit 2025, change all "2024" to "2025" in the paths

===============================================================
  Troubleshooting
===============================================================

Q: PowerShell script won't run?
A: Run this command first:
   Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser

Q: "Cannot load file or assembly" error?
A: Make sure all DLL files are copied correctly

Q: Icons not showing?
A: Verify that Resources\Icons\ folder is copied

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

