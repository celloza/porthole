# Sandbox Testing Guide for Dashboard Path Resolution Fix

## What You Should See

A Windows Sandbox instance will open with the Porthole MSI installer on the Desktop.

## Testing Procedure

### 1. Install Porthole
- Double-click the MSI installer on the desktop
- Complete the installation wizard
- The installer will place Porthole in the standard Windows installation path

### 2. Test Opening Dashboard from Tray
- After installation completes, look for the Porthole tray icon
- **Double-click the tray icon** to open the dashboard
- **Or** right-click the tray icon and select "Open dashboard"

### Expected Behavior (After Fix)
✓ Dashboard should launch successfully
✓ No error message about "Porthole.App.exe could not be found"
✓ The dashboard should open and display the container management UI

### If an Error Occurs
- The error message should now be user-friendly:
  - "The Porthole dashboard could not be opened. If this issue persists after reinstalling Porthole, please report it at https://github.com/celloza/porthole/issues."
  - Window title: "Porthole — Dashboard unavailable"
  - Icon: Warning icon (not Information)

## Technical Details of the Fix

The fix improves the path resolution logic in `src/Porthole.Tray/Program.cs`:

1. **Prefers Environment.ProcessPath** over AppContext.BaseDirectory
   - More reliable when called from MSI custom actions with relative paths
   
2. **Checks sibling installation folders**
   - MSI layout: ...\Porthole\Tray and ...\Porthole\App
   
3. **Falls back to %LOCALAPPDATA%**
   - `%LOCALAPPDATA%\Porthole\App\Porthole.App.exe`
   - Reliable fallback for standard user installations

4. **Better error messages**
   - User-friendly message (not technical)
   - Links to issue reporting
   - Warning icon instead of Information icon

## Cleanup
- Uninstall Porthole through Control Panel → Programs and Features
- Close the Sandbox window to discard changes
