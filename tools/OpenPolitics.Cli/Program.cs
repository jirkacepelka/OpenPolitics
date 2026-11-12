using OpenPolitics.Cli.Commands;
using OpenPolitics.Core;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine($"OpenPolitics {CoreInfo.Version}");
    return 0;
}

if (args.Length >= 1 && args[0] == "validate")
    return ValidateCommand.Execute(args[1..], Console.Out);

Console.Error.WriteLine("Usage: openpolitics <validate|run|batch> ... | --version");
return 2;
