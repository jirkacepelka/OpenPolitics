using OpenPolitics.Core;

if (args.Length == 1 && args[0] == "--version")
{
    Console.WriteLine($"OpenPolitics {CoreInfo.Version}");
    return 0;
}

Console.Error.WriteLine("Usage: openpolitics <validate|run|batch> ... | --version");
return 2;
