########## DOCUMENTATION ##########
# UpdateVersion version 1.0
#
# To execute this file from the command line (or a post-build event in VisualStudio) you'll want to include the following arguments
# Powershell.exe -ExecutionPolicy Unrestricted -file {Path to your ps1 file} {majorVersion} {minorVersion} {startDate} {createVersionFile}
# 
# NOTE: If you're executing it from a post-build event in VisualStudio, 
# place the file in the same folder as your solution and use this command for the post-build event:
# Powershell.exe -ExecutionPolicy Unrestricted -file "$(ProjectDir)\UpdateVersion.ps1"
# 
# Any further arguments should be placed after the file path
#
# ARGUMENTS:
# majorVersion: hard-coded major version in formatted version string (Default = 1)
# minorVersion: hard-coded minor version in formatted version string (Default = 0)
# buildDate: number of days since startDate
# startDate: day your project was started, format should be DateTime parseable (Default = Jan 1st, 2000)
# useRevision: Outputs Revision number as part of your version number, otherwise it will use Major.Minor.Build
# versionFile: create Version.txt file in your Mod's local folder. Outputs without revision number for misc. use
# 
# EXAMPLE: Powershell.exe -ExecutionPolicy Unrestricted -file "$(ProjectDir)\UpdateVersion.ps1" 1 5 "01-JAN-2021" false true
#

########## Definitions ##########

param(
    [Parameter(Mandatory = $true, Position = 0)]
    [Int32]$major = 1,
    [Parameter(Mandatory = $true, Position = 1)]
    [Int32]$minor = 0,
    [Parameter(Position = 2)]
    [DateTime]$startDate = "01-JAN-2000",
    [Parameter(Position = 3)]
    [string]$useRevision = "false",
    [Parameter(Position = 4)]
    [string]$versionFile = "false"
)

[bool]$createVersionFile = $false
switch ($versionFile){
    "true" { 
        $createVersionFile = $true 
    }
    default {
        $createVersionFile = $false
    }
}

[bool]$revision = $false
switch ($useRevision){
    "true" { 
        $revision = $true 
    }
    default {
        $revision = $false
    }
}

$buildDate = Get-Date

Function UpDirectory {
    cd ..
}

Function MapToContainingDirectory([string]$directoryName, [int]$maxAttempts = 10){
    for ($i = 0; $i -lt $maxAttempts; $i++){
        Write-Output "Searching for About folder in directory: $(Get-Location)"
        if (Test-Path -path $directoryName){
            Write-Output "Successfully mapped to directory: $(Get-Location)"
            return;
        }
        UpDirectory
    }
    throw "Unable to locate About folder"
    exit;
}

Function GetVersionNumber {
    $dateDifference = New-TimeSpan -Start $buildDate -End $startDate
    $build = [Math]::Abs([Math]::Floor(($buildDate - $startDate).TotalDays))
    $buildString = "{0:d4}" -f [int]$build
    return "$($major).$($minor).$($buildString)" 
}

Function GetVersionNumberWithRevision {
    $revision = (($buildDate.Hour * 3600 + $buildDate.Minute * 60 + $buildDate.Second) / 2) #AssemblyVersion.Revision is 1/2 the number of seconds into the day
    $revisionString = "{0:d5}" -f [int]$revision
    return "$(GetVersionNumber).$($revisionString)"
}

########## Version Output ##########

Write-Output "Executing UpdateVersion script"
Write-Output "Project Reference Date: $($startDate)"
Write-Output "Build Date: $($buildDate)"
Write-Output "Create VersionFile: $($createVersionFile)"

MapToContainingDirectory "About"

$version = GetVersionNumber
$versionWithRevision = GetVersionNumber

if ($revision){
    $versionWithRevision = GetVersionNumberWithRevision
}

Write-Output "Version: $($versionWithRevision)"

########## Output Version to File ##########

$aboutFilePath = "$(Get-Location)\About\About.xml"
$versionFilePath = "$(Get-Location)\Version.txt"

if ($createVersionFile){
    Write-Output "Outputting VersionWithRevision to Version.txt file at $($versionFilePath)"
    $version.Trim() | Out-File -encoding ascii -NoNewline -FilePath $versionFilePath
}

[xml]$xml = Get-Content -path $aboutFilePath

$xml.SelectNodes("//modVersion") |
    Foreach-Object {
        $_.InnerText = $versionWithRevision
    }

Write-Output "Saving revised modVersion xml to $($aboutFilePath)"
$xml.Save($aboutFilePath)