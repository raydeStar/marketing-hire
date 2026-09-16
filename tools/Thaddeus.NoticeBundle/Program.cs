using Thaddeus.Packaging;

if (args.Length < 3) throw new ArgumentException("Use: Thaddeus.NoticeBundle SOURCE PACKAGE PROJECT-ASSETS-JSON [PROJECT-ASSETS-JSON...]");
var report = NoticeBundle.Create(args[0], args[1], args[2..]);
Console.WriteLine($"Preserved notices for {report.Components.Count} dependencies in {report.Files} files. The raven gives credit where it is due.");
