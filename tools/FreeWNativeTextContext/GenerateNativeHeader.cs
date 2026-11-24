using MicroCom.CodeGenerator;

if (args.Length != 2)
    throw new ArgumentException("Expected pinned IDL input and native header output paths.");

// This is the same generation operation as pinned Avalonia nukebuild/Build.cs GenerateCppHeaders,
// without building the unrelated Nuke packaging/designer projects.
var idl = MicroComCodeGenerator.Parse(File.ReadAllText(args[0]));
File.WriteAllText(args[1], idl.GenerateCppHeader());
