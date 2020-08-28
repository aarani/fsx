#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#r "System.Configuration"
open System.Configuration
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Network.fs"
open FSX.Infrastructure
open Process

let args = Misc.FsxArguments()
if args.Length > 2 then
    Console.Error.WriteLine "Usage: nugetPush.fsx [baseVersion] <nugetApiKey>"
    Environment.Exit 1

// this is a translation of doing this in unix:
// 0.1.0-date`date +%Y%m%d-%H%M`.git-`echo $GITHUB_SHA | cut -c 1-7`
let GetIdealNugetVersion (initialVersion: string) =
    let dateSegment = sprintf "date%s" (DateTime.UtcNow.ToString "yyyyMMdd-hhmm")
    let githubEnvVarNameForGitHash = "GITHUB_SHA"
    let gitHash = Environment.GetEnvironmentVariable githubEnvVarNameForGitHash
    if null = gitHash then
        //TODO: in this case we should just launch a git command
        Console.Error.WriteLine (sprintf "Environment variable %s not found, not running under GitHubActions?"
                                         githubEnvVarNameForGitHash)
        Environment.Exit 2

    let gitHashDefaultShortLength = 7
    let gitShortHash = gitHash.Substring(0, gitHashDefaultShortLength)
    let gitSegment = sprintf "git-%s" gitShortHash
    let finalVersion = sprintf "%s.0-%s.%s"
                               initialVersion dateSegment gitSegment
    finalVersion

let FindOrGenerateNugetPackages (): seq<FileInfo> =
    let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, // Tools/
                                             "..",                 // fsx root
                                             "..")                 // repo root
                               )
    let nuspecFiles = rootDir.EnumerateFiles "*.nuspec"
    if nuspecFiles.Any() then
        if args.Length < 1 then
            Console.Error.WriteLine "Usage: nugetPush.fsx [baseVersion] <nugetApiKey>"
            Environment.Exit 1
        let baseVersion = args.First()

        // we need to download nuget.exe because `dotnet pack` doesn't support using standalone (i.e.
        // without a project association) .nuspec files, see https://github.com/NuGet/Home/issues/4254
        let nugetTargetDir = Path.Combine(rootDir.FullName, ".nuget") |> DirectoryInfo
        if not nugetTargetDir.Exists then
            nugetTargetDir.Create()
        let prevCurrentDir = Directory.GetCurrentDirectory()
        Directory.SetCurrentDirectory nugetTargetDir.FullName
        let nugetDownloadUri = Uri "https://dist.nuget.org/win-x86-commandline/v4.5.1/nuget.exe"
        Network.DownloadFile nugetDownloadUri |> ignore
        let nugetExe = Path.Combine(nugetTargetDir.FullName, "nuget.exe") |> FileInfo
        Directory.SetCurrentDirectory prevCurrentDir

        seq {
            for nuspecFile in nuspecFiles do
                let packageName = Path.GetFileNameWithoutExtension nuspecFile.FullName

                let nugetVersion = GetIdealNugetVersion baseVersion
                let nugetPackCmd =
                    {
                        Command = nugetExe.FullName
                        Arguments = sprintf "pack %s -Version %s"
                                            nuspecFile.FullName nugetVersion
                    }

                Process.SafeExecute (nugetPackCmd, Echo.All) |> ignore
                yield FileInfo (sprintf "%s.%s.nupkg" packageName nugetVersion)
        }
    else
        rootDir.EnumerateFiles("*.nupkg", SearchOption.AllDirectories)

let NugetUpload (packageFile: FileInfo) (nugetApiKey: string) =

    let defaultNugetFeedUrl = "https://api.nuget.org/v3/index.json"
    let nugetPushCmd =
        {
            Command = "dotnet"
            Arguments = sprintf "nuget push %s -k %s -s %s"
                                packageFile.FullName nugetApiKey defaultNugetFeedUrl
        }
    Process.SafeExecute (nugetPushCmd, Echo.All) |> ignore


let nugetPkgs = FindOrGenerateNugetPackages () |> List.ofSeq
if not (nugetPkgs.Any()) then
    Console.Error.WriteLine "No nuget packages found or generated"
    Environment.Exit 3

if args.Length < 1 then
    Console.Error.WriteLine "nugetApiKey argument was not passed to the script (running in a fork?), skipping upload..."
    Environment.Exit 0
let nugetApiKey = args.Last()

let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"
if githubRef <> "refs/heads/master" then
    Console.WriteLine "Branch different than master, skipping upload..."
    Environment.Exit 0

for nugetPkg in nugetPkgs do
    NugetUpload nugetPkg nugetApiKey

