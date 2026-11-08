using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Port.Bridge;

using var runLog = args.Length > 0 && args[0] == "--run" ? RunLog.Start() : null;
Console.WriteLine($"Authored launcher loaded {typeof(Game).Assembly.GetName().Name} {typeof(Game).Assembly.GetName().Version}.");

if (args.Length > 0 && args[0] == "--run")
    return RunSession.Start(args);

if (args.Length == 2 && args[0] == "--check-load")
{
    try
    {
        _ = typeof(BridgeRuntime).Assembly;
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[1]));
        if (assembly.GetName().Name != "ZombiesWP7")
            throw new InvalidDataException("Prepared assembly identity is not ZombiesWP7.");
        var types = assembly.GetTypes();
        var members = 0;
        var prepared = 0;
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            var methods = type.GetMethods(flags).Cast<MethodBase>()
                .Concat(type.GetConstructors(flags));
            foreach (var method in methods)
            {
                members++;
                if (method.IsAbstract || method.ContainsGenericParameters || method.GetMethodBody() is null)
                    continue;
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                prepared++;
            }
            members += type.GetFields(flags).Length;
            members += type.GetProperties(flags).Length;
            members += type.GetEvents(flags).Length;
        }
        // Metadata's 178 TypeDefs include the synthetic <Module> row.
        if (types.Length != 177)
            throw new InvalidDataException($"Expected 177 loadable game types; found {types.Length}.");
        Console.WriteLine($"CHECK_LOAD_OK types={types.Length} members={members} jitted={prepared}");
        return 0;
    }
    catch (ReflectionTypeLoadException error)
    {
        foreach (var cause in error.LoaderExceptions.Where(cause => cause is not null))
            Console.Error.WriteLine($"CHECK_LOAD_FAIL {cause!.GetType().Name}: {cause.Message}");
        return 1;
    }
    catch (Exception error)
    {
        Console.Error.WriteLine($"CHECK_LOAD_FAIL {error.GetType().Name}: {error.Message}");
        return 1;
    }
}

Console.Error.WriteLine("Usage: Z0MB1ES --check-load <private/prepared/run/ZombiesWP7.dll>");
Console.Error.WriteLine("   or: Z0MB1ES --run <prepared.dll> --content <private/content-prepared/run> [--frames N] [--windowed] [--save-dir <directory>] [--probe-services] [--probe-mode normal|endless|viking] [--probe-menus]");
return 2;
