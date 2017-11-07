#!/usr/bin/env fsharpi
open System
open System.IO

#r "System.Configuration"
#load "InfraLib/MiscTools.fs"
#load "InfraLib/ProcessTools.fs"
open FSX.Infrastructure
open ProcessTools

let mutable retryCount = 0
while (retryCount < 20) do //this is a stress test
    let procResult = ProcessTools.Execute({ Command = "fsharpi"; Arguments = "testProcessToolsConcurrencySample.fsx" }, Echo.Off)
    let actual = (procResult.Output.ToString().Replace(Environment.NewLine,"-"))
    let expectedPossibility1 = "foo1-bar1-foo2-bar2-"
    let expectedPossibility2 = "bar1-foo1-bar2-foo2-"
    if (actual <> expectedPossibility1) && (actual <> expectedPossibility2) then
        Console.Error.WriteLine (sprintf "Stress test failed, got `%s`, should have been `%s` or `%s`"
                                     actual expectedPossibility1 expectedPossibility2)
        Environment.Exit 1

Console.WriteLine "Success"
Environment.Exit 0

