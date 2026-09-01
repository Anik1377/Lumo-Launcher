// Mirror of the app's GlobalUsings.cs: the WindowsDesktop SDK (net8.0-windows
// target, UseWPF=true) removes System.IO from the implicit usings set, so test
// files that touch the file system (DeckTests, PersonaFaceTests) lose Path/File.
// The net8.0 target keeps System.IO implicitly — a duplicate global using there
// is harmless.
global using System.IO;
