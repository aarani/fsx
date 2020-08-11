#!/usr/bin/env fsharpi

open System
open System.IO
open System.Linq
#r "System.Configuration"
#load "../InfraLib/Misc.fs"
#load "../InfraLib/Process.fs"
#load "../InfraLib/Git.fs"
open FSX.Infrastructure
open Process

// mimic https://stackoverflow.com/a/3230241/544947
let GitSpecificPush (remoteName: string) (commitSha: string) (remoteBranchName: string) =
    let gitPush =
        {
            Command = "git"
            Arguments = sprintf "push %s %s:%s -f" remoteName commitSha remoteBranchName
        }
    Process.SafeExecute (gitPush, Echo.OutputOnly) |> ignore

let GitFetch (remoteOpt: Option<string>) =
    let remoteArg =
        match remoteOpt with
        | None -> "--all"
        | Some remote -> remote
    let gitFetch =
        {
            Command = "git"
            Arguments = sprintf "fetch %s" remoteArg
        }
    Process.SafeExecute (gitFetch, Echo.OutputOnly) |> ignore

let GetLastNthCommitFromRemoteBranch (remoteName: string) (remoteBranch: string) (n: uint32) =
    let gitShow =
        {
            Command = "git"
            Arguments = sprintf "show %s/%s~%i --no-patch" remoteName remoteBranch n
        }
    let gitShowProc = Process.SafeExecute(gitShow, Echo.Off)
    let firstLine = (Misc.CrossPlatformStringSplitInLines gitShowProc.Output.StdOut).First()

    // split this line: commit 938634a3e7d4dc7e6dd357927a16165120bbea68 (HEAD -> master, origin/master, origin/HEAD)
    let commitHash = firstLine.Split([|" "|], StringSplitOptions.None).[1]
    commitHash

let GetRemotes () =
    let gitRemote = Process.SafeExecute({ Command = "git"; Arguments = "remote" }, Echo.Off)
    Misc.CrossPlatformStringSplitInLines gitRemote.Output.StdOut

let FindUnpushedCommits (remoteName: string) (remoteBranch: string) =
    let rec findUnpushedCommits localCommitsWalkedSoFar currentSkipCount remoteCommits =
        let rec findIntersection localCommits (remoteCommits: List<string>) =
            match localCommits with
            | [] -> None
            | head::tail ->
                if remoteCommits.Contains head then
                    Some tail
                else
                    findIntersection tail remoteCommits

        Console.WriteLine "Walking tree..."
        let currentHash = Process.SafeExecute({ Command = "git";
                                                Arguments = sprintf "log -1 --skip=%i --format=format:%%H"
                                                                    currentSkipCount },
                                              Echo.Off).Output.StdOut.Trim()
        let newRemoteCommits =
            (GetLastNthCommitFromRemoteBranch remoteName remoteBranch currentSkipCount)::remoteCommits

        let newLocalCommitsWalkedSoFar = currentHash::localCommitsWalkedSoFar
        match findIntersection newLocalCommitsWalkedSoFar newRemoteCommits with
        | Some theCommitsToPush ->
            theCommitsToPush
        | None ->
            findUnpushedCommits newLocalCommitsWalkedSoFar
                                (currentSkipCount + 1u)
                                newRemoteCommits

    GitFetch (Some remoteName)
    findUnpushedCommits List.empty 0u List.empty

let GetLastCommits (count: UInt32) =
    let rec getLastCommits commitsFoundSoFar currentSkipCount currentCount =
        if currentCount = 0u then
            commitsFoundSoFar
        else
            let currentHash = Process.SafeExecute({ Command = "git";
                                                    Arguments = sprintf "log -1 --skip=%i --format=format:%%H"
                                                                        currentSkipCount },
                                                  Echo.Off).Output.StdOut.Trim()
            getLastCommits (currentHash::commitsFoundSoFar) (currentSkipCount + 1u) (currentCount - 1u)

    getLastCommits List.empty 0u count

let args = Misc.FsxArguments()
if args.Length < 1 || args.Length > 2 then
    Console.Error.WriteLine "Usage: gitpush.fsx <remotename> [numberOfCommits(optional)]"
    Environment.Exit 1

let maybeNumberOfCommits =
    if args.Length = 2 then
        match UInt32.TryParse args.[1] with
        | true, 0u ->
            Console.Error.WriteLine "Second argument should be an integer higher than zero"
            Environment.Exit 2
            failwith "Unreachable"
        | true, num -> Some num
        | _ ->
            Console.Error.WriteLine "Second argument should be an integer"
            Environment.Exit 3
            failwith "Unreachable"
    else
        None

let remote = args.[0]
if not (GetRemotes().Any(fun currentRemote -> currentRemote = remote)) then
    Console.Error.WriteLine (sprintf "Remote '%s' not found" remote)
    Environment.Exit 4

let currentBranch = Git.GetCurrentBranch()
let commitsToBePushed =
    match maybeNumberOfCommits with
    | None ->
        let commitsToPush = FindUnpushedCommits remote currentBranch
        if commitsToPush.Length = 0 then
            Console.Error.WriteLine (sprintf "Current branch '%s' in remote '%s' is already up to date. Force push by specifying number of commits as 2nd argument?"
                                             currentBranch remote)
            Environment.Exit 5
            failwith "Unreachable"
        else
            Console.WriteLine (sprintf "Detected a delta of %i commits between local branch '%s' and the one in remote '%s', to be pushed one by one. Press any key to continue or CTRL+C to abort."
                                       commitsToPush.Length currentBranch remote)
            Console.ReadKey true |> ignore
            Console.WriteLine "Pushing..."
            commitsToPush
    | Some numberOfCommits ->
        GetLastCommits numberOfCommits

for commit in commitsToBePushed do
    GitSpecificPush remote commit currentBranch
