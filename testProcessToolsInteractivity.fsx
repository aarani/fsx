#!/usr/bin/env fsharpi
open System
open System.IO

#r "System.Configuration"
#load "InfraLib/MiscTools.fs"
#load "InfraLib/ProcessTools.fs"
open FSX.Infrastructure
open ProcessTools

Console.WriteLine("experiment BEGIN")
let procResult = ProcessTools.Execute({ Command = "fsharpi"; Arguments = "testProcessToolsConcurrencyInteractiveSample.fsx" }, Echo.OutputOnly)
Console.WriteLine("experiment END")

Console.WriteLine "Success?"
Environment.Exit 0

