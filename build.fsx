// --------------------------------------------------------------------------------------
// FAKE build script 
// --------------------------------------------------------------------------------------

#r @"tools/FAKE/tools/FakeLib.dll"

open System
open System.IO
open System.Text.RegularExpressions
open Fake 
open Fake.AssemblyInfoFile
open Fake.Git

Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let files includes = 
  { BaseDirectories = [__SOURCE_DIRECTORY__]
    Includes = includes
    Excludes = [] } |> Scan

// Information about the project to be used at NuGet and in AssemblyInfo files
let project = "FSharp.Formatting"
let authors = ["Tomas Petricek"; "Oleg Pestov"; "Anh-Dung Phan"]
let summary = "A package for building great F# documentation, samples and blogs"
let description = """
  The package is a collection of libraries that can be used for literate programming
  with F# (great for building documentation) and for generating library documentation 
  from inline code comments. The key componments (also available separately) are 
  Markdown parser, tools for formatting F# code snippets, including tool tip
  type information and a tool for generating documentation from library metadata."""

let tags = "F# fsharp formatting markdown code fssnip literate programming"

// Read additional information from the release notes document
// Expected format: "0.9.0-beta - Foo bar." or just "0.9.0 - Foo bar."
// (We need to extract just the number for AssemblyInfo & all version for NuGet
let versionAsm, versionNuGet, releaseNotes = 
    let lastItem = File.ReadLines "RELEASE_NOTES.md" |> Seq.last
    let firstDash = lastItem.IndexOf(" - ")
    let notes = lastItem.Substring(firstDash + 2).Trim()
    let version = lastItem.Substring(0, firstDash).Trim([|'*'|]).Trim()
    // Get just numeric version, if it contains dash
    let versionDash = version.IndexOf('-')
    if versionDash = -1 then version, version, notes
    else version.Substring(0, versionDash), version, notes

// --------------------------------------------------------------------------------------
// Generate assembly info files with the right version & up-to-date information

Target "AssemblyInfo" (fun _ ->
  let fileName = "src/Common/AssemblyInfo.fs"
  CreateFSharpAssemblyInfo fileName
      [ Attribute.Title project
        Attribute.Product project
        Attribute.Description summary
        Attribute.Version versionAsm
        Attribute.FileVersion versionAsm ] 
)

// --------------------------------------------------------------------------------------
// Clean build results & restore NuGet packages

Target "RestorePackages" (fun _ ->
    !! "./**/packages.config"
    |> Seq.iter (RestorePackage (fun p -> { p with ToolPath = "./.nuget/NuGet.exe" }))
)

Target "Clean" (fun _ ->
    CleanDirs ["bin"; "gh-pages"; "release" ]
)

Target "CleanDocs" (fun _ ->
    CleanDirs ["generated"]
)

// --------------------------------------------------------------------------------------
// Build library 

Target "Build" (fun _ ->
    (files ["FSharp.Formatting.sln"; "FSharp.Formatting.Tests.sln"])
    |> MSBuildRelease "" "Rebuild"
    |> ignore
)

// --------------------------------------------------------------------------------------
// Run the unit tests using test runner & kill test runner when complete

Target "RunTests" (fun _ ->
    let nunitVersion = GetPackageVersion "packages" "NUnit.Runners"
    let nunitPath = sprintf "packages/NUnit.Runners.%s/Tools" nunitVersion

    ActivateFinalTarget "CloseTestRunner"

    (files ["tests/*/bin/Release/FSharp.*Tests*.dll"])
    |> NUnit (fun p ->
        { p with
            ToolPath = nunitPath
            DisableShadowCopy = true
            TimeOut = TimeSpan.FromMinutes 20.
            OutputFile = "TestResults.xml" })
)

FinalTarget "CloseTestRunner" (fun _ ->  
    ProcessHelper.killProcess "nunit-agent.exe"
)

// --------------------------------------------------------------------------------------
// Build a NuGet package

Target "NuGet" (fun _ ->
    // Format the description to fit on a single line (remove \r\n and double-spaces)
    let description = description.Replace("\r", "").Replace("\n", "").Replace("  ", " ")
    let nugetPath = ".nuget/nuget.exe"
    NuGet (fun p -> 
        { p with   
            Authors = authors
            Project = project
            Summary = summary
            Description = description
            Version = versionNuGet
            ReleaseNotes = releaseNotes
            Tags = tags
            OutputPath = "bin"
            ToolPath = nugetPath
            AccessKey = getBuildParamOrDefault "nugetkey" ""
            Publish = hasBuildParam "nugetkey"
            Dependencies = [] })
        "nuget/FSharp.Formatting.nuspec"
)

// --------------------------------------------------------------------------------------
// Generate the documentation

Target "JustGenerateDocs" (fun _ ->
    executeFSI "tools" "build.fsx" [] |> ignore
)

Target "GenerateDocs" DoNothing
"CleanDocs" ==> "JustGenerateDocs" ==> "GenerateDocs"

// --------------------------------------------------------------------------------------
// Release Scripts

let gitHome = "https://github.com/tpetricek"

Target "ReleaseDocs" (fun _ ->
    Repository.clone "" (gitHome + "/FSharp.Formatting.git") "gh-pages"
    Branches.checkoutBranch "gh-pages" "gh-pages"
    CopyRecursive "generated" "gh-pages" true |> printfn "%A"
    CommandHelper.runSimpleGitCommand "gh-pages" "add ." |> printfn "%s"
    let cmd = sprintf """commit -a -m "Update generated documentation for version %s""" versionNuGet
    CommandHelper.runSimpleGitCommand "gh-pages" cmd |> printfn "%s"
    Branches.push "gh-pages"
)

Target "ReleaseBinaries" (fun _ ->
    Repository.clone "" (gitHome + "/FSharp.Formatting.git") "release"
    Branches.checkoutBranch "release" "release"
    CopyRecursive "bin" "release" true |> printfn "%A"
    let cmd = sprintf """commit -a -m "Update binaries for version %s""" versionNuGet
    CommandHelper.runSimpleGitCommand "release" cmd |> printfn "%s"
    Branches.push "release"
)

Target "Release" DoNothing

// --------------------------------------------------------------------------------------
// Run all targets by default. Invoke 'build <Target>' to override
  
Target "All" DoNothing

"Clean"
  ==> "RestorePackages"
  ==> "AssemblyInfo"
  ==> "Build"
  ==> "GenerateDocs"
  ==> "RunTests"
  ==> "All"

"All" 
  ==> "ReleaseDocs"
  ==> "ReleaseBinaries"
  ==> "NuGet"
  ==> "Release"

RunTargetOrDefault "All"