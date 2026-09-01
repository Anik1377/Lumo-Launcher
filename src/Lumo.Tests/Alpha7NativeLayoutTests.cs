using System;
using System.Collections.Generic;
using System.IO;
using Lumo.Core;
using Xunit;

namespace Lumo.Tests;

/// <summary>
/// v2.6.0-alpha.7 — the Whisper.net native layout rule. alpha.5/6 embedded the
/// whisper.cpp dlls in the single-file exe and relied on .NET's temp self-extract,
/// but Whisper.net 1.9.1's loader probes ONLY runtimes/win-x64/ next to the exe —
/// so every field install died with "Native Library not found in default paths"
/// while dev runs (build output has the folder) worked. The fix is packaging
/// (loose runtimes/win-x64/ beside Lumo.exe) plus a pre-flight in WhisperEngine
/// that refuses to start with a readable message. These tests pin the rule: the
/// required dll set, path composition, first-missing detection and the
/// actionable wording — all pure, no disk access.
/// </summary>
public class Alpha7NativeLayoutTests
{
    private const string Base = "/fake/app";   // never touches the real filesystem

    [Fact]
    public void FolderPath_Is_Runtimes_WinX64_Under_The_Base_Directory()
    {
        string p = WhisperNative.FolderPath(Base);
        Assert.StartsWith(Base, p);
        Assert.EndsWith(Path.Combine("runtimes", "win-x64"), p);
    }

    [Fact]
    public void FilePath_Lands_Inside_The_Runtime_Folder()
    {
        Assert.Equal(
            Path.Combine(WhisperNative.FolderPath(Base), "whisper.dll"),
            WhisperNative.FilePath(Base, "whisper.dll"));
    }

    [Fact]
    public void Complete_Folder_Misses_Nothing()
    {
        var present = new HashSet<string>(WhisperNative.RequiredFiles);
        Assert.Null(WhisperNative.MissingFile(Base, f => present.Contains(Path.GetFileName(f))));
    }

    [Fact]
    public void MissingFile_Reports_The_First_Absent_Dll()
    {
        var present = new HashSet<string>(WhisperNative.RequiredFiles);
        present.Remove("ggml-cpu-whisper.dll");
        Assert.Equal("ggml-cpu-whisper.dll",
            WhisperNative.MissingFile(Base, f => present.Contains(Path.GetFileName(f))));
    }

    [Fact]
    public void Half_Extracted_Zip_Is_Caught_At_The_First_Required_Dll()
    {
        // the field bug: only Lumo.exe made it out of the zip — every dll absent
        Assert.Equal("whisper.dll", WhisperNative.MissingFile(Base, _ => false));
    }

    [Fact]
    public void Required_Files_Match_The_WhisperNet_Runtime_Package()
    {
        // exactly what Whisper.net.Runtime 1.9.1's build targets copy into
        // runtimes/win-x64/ — a package upgrade that renames or adds natives
        // must update this list (the publish output shows the truth).
        Assert.Equal(
            new[] { "whisper.dll", "ggml-whisper.dll", "ggml-base-whisper.dll", "ggml-cpu-whisper.dll" },
            WhisperNative.RequiredFiles);
    }

    [Fact]
    public void MissingMessage_Names_The_File_And_The_Fix()
    {
        string msg = WhisperNative.MissingMessage("whisper.dll");
        Assert.Contains("whisper.dll", msg);
        Assert.Contains(WhisperNative.RuntimeFolder, msg);
        Assert.Contains("re-extract", msg);
        // the chat failure line is a placeholder — one readable sentence
        Assert.True(msg.Length <= 220, $"message too long for the UI: {msg.Length}");
    }
}
