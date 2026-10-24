' Double-click to run local read-only checks without a visible console.
Option Explicit
Dim shell, fs, script
Set shell = CreateObject("WScript.Shell")
Set fs = CreateObject("Scripting.FileSystemObject")
script = fs.BuildPath(fs.GetParentFolderName(WScript.ScriptFullName), "Validate-GpuMonitor.ps1")
shell.Run "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File """ & script & """ -OpenReport", 0, False
