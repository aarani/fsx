#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq

#r "System.Configuration"
open System.Configuration
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Network.fs"
#load "../InfraLib/Git.fs"
open FSX.Infrastructure
open Process

let PrintUsage() =
    Console.Error.WriteLine "Usage: nugetPush.fsx [--output-version] [baseVersion] <nugetApiKey>"
    Environment.Exit 1

let args = Misc.FsxArguments()
if args.Length > 3 then
    PrintUsage ()
if args.Length > 2 && args.[0] <> "--output-version" then
    PrintUsage ()


// this is a translation of doing this in unix (assuming initialVersion="0.1.0"):
// 0.1.0--date`date +%Y%m%d-%H%M`.git-`git rev-parse --short=7 HEAD`
let GetIdealNugetVersion (initialVersion: string) =
    let dateSegment = sprintf "date%s" (DateTime.UtcNow.ToString "yyyyMMdd-hhmm")

    let gitHash = Git.GetLastCommit()
    if null = gitHash then
        Console.Error.WriteLine "Not in a git repository?"
        Environment.Exit 2

    let gitHashDefaultShortLength = 7
    let gitShortHash = gitHash.Substring(0, gitHashDefaultShortLength)
    let gitSegment = sprintf "git-%s" gitShortHash
    let finalVersion = sprintf "%s.0--%s.%s"
                               initialVersion dateSegment gitSegment
    finalVersion

let DoesDotnetSDKExists() = 
    try
        let dotnetVersionCmd =
            {
                Command = "dotnet"
                Arguments = "--version"
            }
        Process.SafeExecute (dotnetVersionCmd, Echo.All) |> ignore 
        true
    with
        :?  ProcessCouldNotStart -> false

let DownloadNugetIfNotExists() =
    let rootDir = DirectoryInfo(Path.Combine(__SOURCE_DIRECTORY__, // Tools/
                                         "..",                 // fsx root
                                         "..")                 // repo root
                           )
    let nugetTargetDir = Path.Combine(rootDir.FullName, ".nuget") |> DirectoryInfo
    if not nugetTargetDir.Exists then
        nugetTargetDir.Create()
    let prevCurrentDir = Directory.GetCurrentDirectory()
    Directory.SetCurrentDirectory nugetTargetDir.FullName
    let nugetDownloadUri = Uri "https://dist.nuget.org/win-x86-commandline/v4.5.1/nuget.exe"
    Network.DownloadFile nugetDownloadUri |> ignore
    let nugetExe = Path.Combine(nugetTargetDir.FullName, "nuget.exe") |> FileInfo
    Directory.SetCurrentDirectory prevCurrentDir

    nugetExe.FullName

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

        seq {
            for nuspecFile in nuspecFiles do
                let packageName = Path.GetFileNameWithoutExtension nuspecFile.FullName

                let nugetVersion = GetIdealNugetVersion baseVersion
                
                // we need to download nuget.exe here because `dotnet pack` doesn't support using standalone (i.e.
                // without a project association) .nuspec files, see https://github.com/NuGet/Home/issues/4254
                
                let nugetPackCmd =
                    {
                        Command = DownloadNugetIfNotExists()
                        Arguments = sprintf "pack %s -Version %s"
                                            nuspecFile.FullName nugetVersion
                    }

                Process.SafeExecute (nugetPackCmd, Echo.All) |> ignore
                yield FileInfo (sprintf "%s.%s.nupkg" packageName nugetVersion)
        }
    else
        let FindNugetPackages() =
            rootDir.Refresh()
            rootDir.EnumerateFiles("*.nupkg", SearchOption.AllDirectories)

        if not (FindNugetPackages().Any()) then
            if args.Length < 1 then
                Console.Error.WriteLine "Usage: nugetPush.fsx [baseVersion] <nugetApiKey>"
                Environment.Exit 1
            let baseVersion = args.First()
            let nugetVersion = GetIdealNugetVersion baseVersion

            if DoesDotnetSDKExists() then
                let dotnetPackCmd =
                    {
                        Command = "dotnet"
                        Arguments = sprintf "pack -c Release -p:Version=%s"
                                            nugetVersion
                    }
                Process.SafeExecute (dotnetPackCmd, Echo.All) |> ignore
            else 
                failwith "Please install .NET SDK to build nuget packages without nuspec file"

        FindNugetPackages()


let NugetUpload (packageFile: FileInfo) (nugetApiKey: string) =

    let defaultNugetFeedUrl = "https://api.nuget.org/v3/index.json"
    if DoesDotnetSDKExists() then
        let nugetPushCmd =
            {
                Command = "dotnet"
                Arguments = sprintf "nuget push %s -k %s -s %s"
                                    packageFile.FullName nugetApiKey defaultNugetFeedUrl
            }
        Process.SafeExecute (nugetPushCmd, Echo.All) |> ignore
    else 
        let nugetPushCmd =
            {
                Command = DownloadNugetIfNotExists()
                Arguments = sprintf "push %s -ApiKey %s -Source %s"
                                    packageFile.FullName nugetApiKey defaultNugetFeedUrl
            }
        Process.SafeExecute (nugetPushCmd, Echo.All) |> ignore

if args.Length > 0 && args.[0] = "--output-version" then
    if args.Length < 2 then
        Console.Error.WriteLine "When using --output-version, pass the base version as the second argument"
        Environment.Exit 4
    let baseVersion = args.[1]
    Console.WriteLine (GetIdealNugetVersion baseVersion)
    Environment.Exit 0

let nugetPkgs = FindOrGenerateNugetPackages () |> List.ofSeq
if not (nugetPkgs.Any()) then
    Console.Error.WriteLine "No nuget packages found or generated"
    Environment.Exit 3

if args.Length < 1 then
    Console.Error.WriteLine "nugetApiKey argument was not passed to the script (running in a fork?), skipping upload..."
    Environment.Exit 0
let nugetApiKey = args.Last()

let IsMasterBranch(): bool =
    let githubRef = Environment.GetEnvironmentVariable "GITHUB_REF"
    if null <> githubRef then
        githubRef = "master" || githubRef = "refs/heads/master"
    else
        // https://docs.gitlab.com/ee/ci/variables/predefined_variables.html
        let gitlabRef = Environment.GetEnvironmentVariable "CI_COMMIT_REF_NAME"
        if null <> gitlabRef then
            gitlabRef = "master" || gitlabRef = "refs/heads/master"
        else
            Git.GetCurrentBranch() = "master"

if not (IsMasterBranch()) then
    Console.WriteLine "Branch different than master, skipping upload..."
    Environment.Exit 0

for nugetPkg in nugetPkgs do
    NugetUpload nugetPkg nugetApiKey