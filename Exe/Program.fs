﻿module SourceLink.Program

// ArgU usage based on Paket
// https://github.com/fsprojects/Paket/blob/master/src/Paket/Program.fs

open System
open Argu
open System.Diagnostics
open System.IO
open SourceLink.Commands

let consoleTrace = Logging.event.Publish |> Observable.subscribe Logging.traceToConsole

let stopWatch = Stopwatch()
stopWatch.Start()

tracefn "SourceLink %s" version

let filterGlobalArgs args = 
    let globalResults = 
        ArgumentParser.Create<GlobalArgs>()
            .Parse(ignoreMissing = true, 
                   ignoreUnrecognized = true, 
                   raiseOnUsage = false)
    let verbose = globalResults.Contains <@ GlobalArgs.Verbose @>
    
    let rest = 
        if verbose then 
            args |> Array.filter (fun a -> a <> "-v" && a <> "--verbose")
        else args
    
    verbose, rest

let processWithValidation<'T when 'T :> IArgParserTemplate> validateF commandF command 
    args = 
    let parser = ArgumentParser.Create<'T>()
    let results = parser.Parse(inputs = args, raiseOnUsage = false, ignoreMissing = true)
    let resultsValid = validateF (results)
    if results.IsUsageRequested || not resultsValid then 
        parser.Usage(Commands.cmdLineUsageMessage command parser) |> trace
    else 
        commandF results
        if Logging.verbose then
            let elapsedTime = Utils.TimeSpanToReadableString stopWatch.Elapsed
            tracefn "elapsed time: %s" elapsedTime

let v, args = filterGlobalArgs (Environment.GetCommandLineArgs().[1..])
Logging.verbose <- v

let index (results: ParseResults<_>) =
    let proj = results.TryGetResult <@ IndexArgs.Proj @>
    let projProps = results.GetResults <@ IndexArgs.Proj_Prop @>
    let url = results.TryGetResult <@ IndexArgs.Url @>
    let commit = results.TryGetResult <@ IndexArgs.Commit @>
    let pdbs = results.GetResults <@ IndexArgs.Pdb @>
    let verifyGit = results.Contains <@ IndexArgs.Not_Verify_Git @> = false
    let verifyPdb = results.Contains <@ IndexArgs.Not_Verify_Pdb @> = false
    let files = results.GetResults <@ IndexArgs.File @>
    let notFiles = results.GetResults <@ IndexArgs.Not_File @>
    let repoDir = results.TryGetResult <@ IndexArgs.Repo @>
    let paths = results.GetResults <@ IndexArgs.Map @>
    let runPdbstr = results.Contains <@ IndexArgs.Not_Pdbstr @> = false
    Index.run proj projProps url commit pdbs verifyGit verifyPdb files notFiles repoDir paths runPdbstr

let checksums (results: ParseResults<_>) =
    let pdb = results.GetResult <@ ChecksumsArgs.Pdb @>
    let file = results.Contains <@ ChecksumsArgs.Not_File @> = false
    let url = results.Contains <@ ChecksumsArgs.Url @>
    let check = results.Contains <@ ChecksumsArgs.Check @>
    let username = results.TryGetResult <@ ChecksumsArgs.Username @>
    let password = results.TryGetResult <@ ChecksumsArgs.Password @>
    Checksums.run pdb file url check username password

let pdbstrr (results: ParseResults<_>) =
    let pdb = results.GetResult <@ SrctoolxArgs.Pdb @>
    Pdbstrr.run pdb

let srctoolx (results: ParseResults<_>) =
    let pdb = results.GetResult <@ SrctoolxArgs.Pdb @>
    SrcToolx.run pdb

let lineFeed (results: ParseResults<_>) =
    let proj = results.TryGetResult <@ LineFeedArgs.Proj @>
    let projProps = results.GetResults <@ LineFeedArgs.Proj_Prop @>
    let files = results.GetResults <@ LineFeedArgs.File @>
    let notFiles = results.GetResults <@ LineFeedArgs.Not_File @>
    LineFeed.run proj projProps files notFiles

let validateHasArgs (results : ParseResults<_>) =
    let args = results.GetAllResults()
    args.Length > 0

let processCommand<'T when 'T :> IArgParserTemplate> (commandF : ParseResults<'T> -> unit) = 
    processWithValidation validateHasArgs commandF 

try
    let parser = ArgumentParser.Create<Command>()
    let results = 
        parser.Parse(inputs = args,
                   ignoreMissing = true, 
                   ignoreUnrecognized = true, 
                   raiseOnUsage = false)

    match results.GetAllResults() with
    | [ command ] -> 
        let handler =
            match command with
            | Index -> processCommand index
            | Checksums -> processCommand checksums
            | Pdbstrr -> processCommand pdbstrr
            | Srctoolx -> processCommand srctoolx
            | LineFeed -> processCommand lineFeed

        let args = args.[1..]
        handler command args
        consoleTrace.Dispose()
        exit 0
    | [] ->
        trace "expected a command"
        parser.Usage("available commands:") |> trace
        exit 1
    | _ -> 
        trace "expected a command"
        parser.Usage("available commands:") |> trace
        exit 1
with
| exn when not (exn :? System.NullReferenceException) ->
    traceErrorfn "SourceLink failed with:%s  %s" Environment.NewLine exn.Message
    if verbose then
        traceErrorfn "StackTrace:%s  %s" Environment.NewLine exn.StackTrace
    exit 1